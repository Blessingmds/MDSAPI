using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// MDSAPI's own patches on the game. Each is installed lazily, the first time a mod
    /// subscribes to the event it powers, so a player running only mods that never use a
    /// hook pays nothing for it. Results go into the same report as mods' patches.
    /// </summary>
    internal static class Hooks
    {
        private static HarmonyLib.Harmony _harmony;

        private static HarmonyLib.Harmony Harmony => _harmony ??= new HarmonyLib.Harmony("MDSAPI");

        internal enum Kind { Prefix, Postfix }

        /// <summary>
        /// Patches <paramref name="method"/> with the static method <paramref name="patchName"/>
        /// on <paramref name="patchClass"/>. Returns false, having logged, if it could not.
        /// </summary>
        internal static bool Install(string label, MethodInfo method, Type patchClass, string patchName, Kind kind)
        {
            if (method is null)
            {
                Log.Warn("Could not hook " + label + ": the game method was not found. Mods relying on it " +
                         "lose that feature this session.");
                MDSAPI.Patching.Patch.Record(label, false);
                return false;
            }

            var patch = patchClass.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                                  .FirstOrDefault(m => m.Name == patchName);
            try
            {
                var hm = new HarmonyMethod(patch);
                if (kind == Kind.Prefix) Harmony.Patch(method, prefix: hm);
                else Harmony.Patch(method, postfix: hm);
                MDSAPI.Patching.Patch.Record(label, true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Hooking " + label + " failed; mods relying on it lose that feature this session.", ex);
                MDSAPI.Patching.Patch.Record(label, false);
                return false;
            }
        }

        /// <summary>Applies Harmony's own transpiler to <paramref name="method"/>. Mono only.</summary>
        internal static bool Transpile(string label, MethodInfo method, MethodInfo transpiler)
        {
            try
            {
                Harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Rewriting " + label + " failed.", ex);
                return false;
            }
        }
    }
}
