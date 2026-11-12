using System;
using System.Collections.Generic;
using System.Linq;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Which parts of the API found what they needed this session.
    /// <para>
    /// A module that cannot resolve its game members goes unavailable on its own. It does
    /// not throw, and it does not take the rest of MDSAPI with it — so a game update that
    /// reworks growing costs a grow mod and leaves the other three running. That blast
    /// radius is the whole point of the layer.
    /// </para>
    /// </summary>
    internal static class Modules
    {
        private static readonly Dictionary<string, Func<bool>> Registered = new();
        private static readonly List<MemberSpec> Specs = new();

        /// <summary>Declares a module and the singleton members it needs to be useful.</summary>
        internal static void Register(string name, params MemberSpec[] required)
        {
            Specs.AddRange(required);
            Registered[name] = () => required.All(s => s.IsAvailable);
        }

        /// <summary>
        /// Declares a module whose availability is a check of its own — for modules built on
        /// <see cref="Access{T}"/> and <see cref="GameMethod"/>, which bind to types rather
        /// than to live singletons.
        /// </summary>
        internal static void Register(string name, Func<bool> available) => Registered[name] = available;

        /// <summary>True when everything this module needs resolved.</summary>
        internal static bool IsAvailable(string name)
        {
            if (!Registered.TryGetValue(name, out var check)) return false;
            try { return check(); } catch { return false; }
        }

        /// <summary>
        /// Drops singleton bindings, so the next use re-resolves. Scene unload calls this.
        /// Type-bound accessors are kept: types do not change within a session.
        /// </summary>
        internal static void Forget()
        {
            foreach (var spec in Specs) spec.Forget();
        }

        /// <summary>
        /// One line at startup naming what bound and what did not. This is the line that
        /// turns "my mod stopped working" into a diagnosis instead of a guessing game, so
        /// it is worth the console space exactly once per load.
        /// </summary>
        internal static void Report()
        {
            if (Registered.Count == 0) return;

            var missing = Registered.Keys.Where(k => !IsAvailable(k)).OrderBy(k => k).ToList();
            var bound = Registered.Count - missing.Count;

            if (missing.Count == 0)
            {
                Log.Info("All " + Registered.Count + " module(s) bound.");
                return;
            }

            Log.Warn(bound + " of " + Registered.Count + " module(s) bound. Unavailable: " +
                     string.Join(", ", missing) + ". Mods using those features will say so " +
                     "themselves; everything else works normally. This usually means a game " +
                     "update moved something — worth reporting with your game version.");
        }
    }
}
