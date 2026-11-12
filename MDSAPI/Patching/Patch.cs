using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;

namespace MDSAPI.Patching
{
    /// <summary>
    /// Harmony patches that survive game updates, or say loudly that they did not.
    /// <para>
    /// The usual <c>[HarmonyPatch(typeof(ItemSlot), nameof(ItemSlot.GetCapacityForItem))]</c>
    /// names a game type and a member directly. When an update renames the method, the
    /// patch throws during auto-patching and the feature silently stops; when it renames the
    /// type, the whole mod fails to load. This resolves both through candidate lists, applies
    /// each patch on its own, and reports any that did not apply — so the player's log says
    /// which feature broke instead of the mod quietly doing nothing.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// // In OnInitializeMelon. Returns false if it could not apply — turn the feature off.
    /// var ok = Patch.Target("ItemFramework.ItemSlot")
    ///               .Method("GetCapacityForItem", "GetCapacity")
    ///               .Postfix(typeof(SlotPatches), nameof(SlotPatches.Capacity));
    ///
    /// static class SlotPatches
    /// {
    ///     // object, not ItemSlot: naming the game type here would put it back in your mod.
    ///     static void Capacity(object __instance, object item, ref int __result) { ... }
    /// }
    /// </code>
    /// There is deliberately no transpiler option: transpilers do not work under IL2CPP.
    /// </example>
    public static class Patch
    {
        /// <summary>Starts describing what to patch: a game type, relative to the game root.</summary>
        public static PatchTarget Target(string relativeType) => new PatchTarget(relativeType);

        private static readonly Dictionary<string, HarmonyLib.Harmony> Instances = new();
        private static readonly List<(string What, bool Ok)> Results = new();
        private static bool _reported;

        internal static HarmonyLib.Harmony HarmonyFor(Assembly owner)
        {
            // One Harmony ID per mod, so a mod's patches can be told apart in a conflict
            // and unpatched without touching anyone else's.
            var id = "MDSAPI.Patching:" + owner.GetName().Name;
            if (!Instances.TryGetValue(id, out var h)) Instances[id] = h = new HarmonyLib.Harmony(id);
            return h;
        }

        internal static void Record(string what, bool ok) => Results.Add((what, ok));

        /// <summary>
        /// One line, once per session, after mods have applied their patches. Individual
        /// failures were already logged as they happened; this is the summary a bug report
        /// needs.
        /// </summary>
        internal static void Report()
        {
            if (_reported || Results.Count == 0) return;
            _reported = true;

            var failed = Results.Where(r => !r.Ok).Select(r => r.What).ToList();
            if (failed.Count == 0)
                Log.Info("All " + Results.Count + " patch(es) applied.");
            else
                Log.Warn((Results.Count - failed.Count) + " of " + Results.Count +
                         " patch(es) applied. Not applied: " + string.Join(", ", failed) +
                         ". The features relying on those are off this session.");
        }
    }

    /// <summary>A patch target being described. See <see cref="Patch"/>.</summary>
    public sealed class PatchTarget
    {
        private readonly List<string> _types = new();
        private readonly List<string> _methods = new();

        internal PatchTarget(string relativeType) => _types.Add(relativeType);

        /// <summary>Another name the type has had or could take. Tried in order.</summary>
        public PatchTarget OrType(string relativeType) { _types.Add(relativeType); return this; }

        /// <summary>The method to patch: current name first, then older or likely names.</summary>
        public PatchTarget Method(params string[] candidates) { _methods.AddRange(candidates); return this; }

        // Type + name rather than a generic <TPatches>: patch classes are conventionally
        // static, and C# forbids static classes as type arguments. A generic here would
        // force every mod to write non-static patch classes for no reason. This is also
        // the shape Harmony's own AccessTools uses, so it reads familiarly.

        /// <summary>Runs before the original. Return false from it to skip the original.</summary>
        public bool Prefix(Type patchClass, string patchMethod) => Apply(patchClass, patchMethod, Kind.Prefix);

        /// <summary>Runs after the original. The usual choice — it cannot break the original.</summary>
        public bool Postfix(Type patchClass, string patchMethod) => Apply(patchClass, patchMethod, Kind.Postfix);

        /// <summary>Wraps the original and other patches in a try/catch.</summary>
        public bool Finalizer(Type patchClass, string patchMethod) => Apply(patchClass, patchMethod, Kind.Finalizer);

        private enum Kind { Prefix, Postfix, Finalizer }

        private bool Apply(Type patchClass, string patchMethodName, Kind kind)
        {
            var label = _types[0] + "." + (_methods.FirstOrDefault() ?? "?");

            var patchMethod = patchClass.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == patchMethodName);
            if (patchMethod is null)
            {
                // A bug in the calling mod, not a game update — say so differently.
                Log.Error(patchClass.Name + "." + patchMethodName + " does not exist or is " +
                          "not static, so the patch on " + label + " was not applied.");
                Patch.Record(label, false);
                return false;
            }

            var original = Resolve(patchMethod, out var boundType, out var boundMethod);
            if (original is null)
            {
                Log.Warn("Could not patch " + label + " (tried types " + string.Join(", ", _types) +
                         "; methods " + string.Join(", ", _methods) + "). The game has likely " +
                         "been updated; whatever this patch powers is off this session.");
                Patch.Record(label, false);
                return false;
            }

            if (boundType != _types[0] || boundMethod != _methods[0])
                Log.Warn(label + " was not found; patched " + boundType + "." + boundMethod +
                         " instead. The game has likely been updated — this still works, " +
                         "but it is worth reporting.");

            try
            {
                var hm = new HarmonyMethod(patchMethod);
                var harmony = Patch.HarmonyFor(patchClass.Assembly);
                switch (kind)
                {
                    case Kind.Prefix: harmony.Patch(original, prefix: hm); break;
                    case Kind.Postfix: harmony.Patch(original, postfix: hm); break;
                    case Kind.Finalizer: harmony.Patch(original, finalizer: hm); break;
                }
                Patch.Record(label, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Harmony refused the patch on " + boundType + "." + boundMethod +
                          ". The feature it powers is off this session.", ex);
                Patch.Record(label, false);
                return false;
            }
        }

        /// <summary>
        /// The original to patch. When the name has several overloads, picks the one whose
        /// parameter names include every ordinary parameter the patch method asks for —
        /// the same rule Harmony uses to bind them, so the choice cannot disagree with it.
        /// Refuses rather than guesses if that still leaves more than one.
        /// </summary>
        private MethodBase Resolve(MethodInfo patchMethod, out string boundType, out string boundMethod)
        {
            boundType = null; boundMethod = null;

            var wanted = patchMethod.GetParameters()
                .Select(p => p.Name)
                .Where(n => n != null && !n.StartsWith("__", StringComparison.Ordinal))
                .ToList();

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var typeName in _types)
            {
                var type = Singletons.GameType(typeName);
                if (type is null) continue;

                foreach (var methodName in _methods)
                {
                    var overloads = type.GetMethods(Flags).Where(m => m.Name == methodName).ToList();
                    if (overloads.Count == 0) continue;

                    var matches = overloads.Count == 1
                        ? overloads
                        : overloads.Where(o =>
                          {
                              var names = o.GetParameters().Select(p => p.Name).ToList();
                              return wanted.All(names.Contains);
                          }).ToList();

                    if (matches.Count == 1)
                    {
                        boundType = typeName; boundMethod = methodName;
                        return matches[0];
                    }

                    if (matches.Count > 1)
                    {
                        Log.Error(typeName + "." + methodName + " has " + matches.Count +
                                  " overloads the patch could apply to. Name one of its " +
                                  "parameters in the patch method so the choice is unambiguous.");
                        return null;
                    }
                }
            }
            return null;
        }
    }
}
