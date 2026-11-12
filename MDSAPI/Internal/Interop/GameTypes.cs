using System;
using System.Collections.Generic;
using System.Reflection;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Finds game types by their name relative to the game root ("ItemFramework.ItemSlot").
    /// <para>
    /// The game is no longer one assembly. Since the 2026 update the item base classes live
    /// in ScheduleOne.Core (Il2CppScheduleOne.Core under IL2CPP), and <c>StackLimit</c> moved
    /// there with them — a mod that looked only in Assembly-CSharp lost its stack limits
    /// overnight. This searches every ScheduleOne assembly, Assembly-CSharp first.
    /// </para>
    /// </summary>
    internal static class GameTypes
    {
#if MDS_IL2CPP
        internal const string Root = "Il2CppScheduleOne.";
#else
        internal const string Root = "ScheduleOne.";
#endif

        private static readonly Dictionary<string, Type> Cache = new();
        private static List<Assembly> _assemblies;

        /// <summary>The type, or null. Cached, including misses.</summary>
        internal static Type Find(string relativeName)
        {
            if (string.IsNullOrEmpty(relativeName)) return null;
            if (Cache.TryGetValue(relativeName, out var hit)) return hit;

            EnsureLoaded();
            var full = Root + relativeName;
            var found = Search(full);
            if (found is null)
            {
                // An assembly may have loaded since the list was built; look once more.
                _assemblies = null;
                found = Search(full);
            }
            Cache[relativeName] = found;
            return found;
        }

        private static Type Search(string fullName)
        {
            foreach (var asm in Assemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static bool _forced;

        /// <summary>
        /// Loads the game assemblies if nothing has yet. Under IL2CPP the interop assemblies
        /// load on first use, and a mod that names no game type at compile time — the point of
        /// MDSAPI — may be the first thing that wants them.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_forced) return;
            _forced = true;
            foreach (var name in new[] { "Assembly-CSharp", Root + "Core" })
            {
                try { Assembly.Load(name); } catch { }
            }
        }

        /// <summary>Tries each name in order; returns the first that exists.</summary>
        internal static Type Find(string[] candidates, out string boundTo)
        {
            boundTo = null;
            foreach (var name in candidates)
            {
                var t = Find(name);
                if (t != null) { boundTo = name; return t; }
            }
            return null;
        }

        private static List<Assembly> Assemblies()
        {
            if (_assemblies != null) return _assemblies;

            var main = new List<Assembly>();
            var rest = new List<Assembly>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = asm.GetName().Name; } catch { continue; }

                if (name == "Assembly-CSharp") main.Add(asm);
                else if (name.StartsWith("ScheduleOne", StringComparison.Ordinal) ||
                         name.StartsWith("Il2CppScheduleOne", StringComparison.Ordinal))
                    rest.Add(asm);
            }
            main.AddRange(rest);
            _assemblies = main;
            return _assemblies;
        }
    }
}
