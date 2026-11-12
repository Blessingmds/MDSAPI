using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Reflection helpers used to bridge members whose names or shapes differ between
    /// runtimes or between game versions. Resolve once and cache; never per frame.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags All =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// Every field on the type and its bases. Save and load must both use this, or a
        /// field declared on a base class is written and never read back.
        /// </summary>
        internal static IEnumerable<FieldInfo> AllFields(Type t)
        {
            for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (var f in cur.GetFields(All | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        /// <summary>
        /// Non-abstract types deriving from <typeparamref name="T"/> across loaded
        /// assemblies.
        /// <para>
        /// Every step here is guarded, because this walks assemblies MDSAPI does not own.
        /// A player's folder can hold a library built for the other runtime — UniverseLib
        /// ships an IL2CPP build that sits happily in UserLibs on the Mono branch — and
        /// merely asking whether one of its types derives from something forces that type
        /// to load and throws. One unloadable type in someone else's library must not stop
        /// MDSAPI from finding a mod's save data.
        /// </para>
        /// </summary>
        internal static IEnumerable<Type> DerivedFrom<T>()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsNoise(asm.GetName().Name)) continue;

                Type[] types;
                // A partially loadable assembly — another mod, usually — throws here.
                // Take the types that did load rather than letting one bad assembly
                // break discovery for everyone.
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t is null) continue;

                    bool derives;
                    // IsAssignableFrom resolves the type's base chain and field types, so
                    // it is the call that actually throws on a cross-runtime assembly.
                    try { derives = !t.IsAbstract && typeof(T).IsAssignableFrom(t) && t != typeof(T); }
                    catch { continue; }

                    if (derives) yield return t;
                }
            }
        }

        /// <summary>
        /// The one method of this name taking a single parameter, preferring a
        /// delegate-typed one. Returns null rather than choosing when several remain.
        /// <para>
        /// Use this instead of <c>Type.GetMethod(name, flags)</c>, which throws
        /// <see cref="System.Reflection.AmbiguousMatchException"/> the moment a type has
        /// two methods of that name — which UnityEvent does.
        /// </para>
        /// </summary>
        internal static MethodInfo FindSingleParameterMethod(Type type, string name, BindingFlags flags)
        {
            if (type is null) return null;

            var candidates = type.GetMethods(flags)
                .Where(m => m.Name == name && m.GetParameters().Length == 1)
                .ToList();

            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            var delegates = candidates
                .Where(m => typeof(Delegate).IsAssignableFrom(m.GetParameters()[0].ParameterType))
                .ToList();

            return delegates.Count == 1 ? delegates[0] : null;
        }

        private static bool IsNoise(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;

            foreach (var prefix in NoisePrefixes)
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;

            // Catches UniverseLib.IL2CPP and anything else that names its runtime
            // somewhere other than the start. Matched case-insensitively because the
            // casing in the wild is inconsistent ("Il2Cpp", "IL2CPP").
            return name.IndexOf("Il2Cpp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly string[] NoisePrefixes =
        {
            "System", "Unity", "Mono.", "mscorlib", "netstandard", "Newtonsoft",
            "0Harmony", "AsmResolver", "MonoMod", "Tomlet", "Cpp2IL", "AssetRipper",
            "FishNet", "GameKit", "Microsoft", "Il2CppInterop",
        };
    }
}
