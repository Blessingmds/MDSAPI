using System;
using System.Collections.Generic;
using System.Reflection;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// Reaches the game's managers.
    /// <para>
    /// Nearly every Schedule I system is a generic singleton from
    /// <c>ScheduleOne.DevUtilities</c>, in four flavours that behave differently:
    /// <c>NetworkSingleton&lt;T&gt;</c> (FishNet, server-authoritative),
    /// <c>Singleton&lt;T&gt;</c> (per-scene, dies on load),
    /// <c>PersistentSingleton&lt;T&gt;</c> (survives loads),
    /// <c>PlayerSingleton&lt;T&gt;</c> (needs the local player).
    /// </para>
    /// <para>
    /// Resolution is by name and cached. The cache is cleared on load so per-scene
    /// singletons are not held across a reload — that stale reference is the bug that
    /// looks like "the mod works until I load a save".
    /// </para>
    /// </summary>
    internal static class Singletons
    {
        private static readonly Dictionary<string, object> Cache = new();
        // Types already warned about this scene. Wrappers are read every frame, and a
        // manager that is legitimately absent (no player yet, main menu) must not print
        // a line per frame.
        private static readonly HashSet<string> Warned = new();
        private const string Root = GameTypes.Root;

        internal static void Clear() { Cache.Clear(); Warned.Clear(); }

        /// <summary>Like <see cref="Resolve"/>, but silent when the instance is absent.</summary>
        internal static object TryResolve(string relativeTypeName)
        {
            if (Cache.TryGetValue(relativeTypeName, out var hit) && !IsDead(hit)) return hit;
            var type = GameTypes.Find(relativeTypeName);
            if (type is null) return null;
            var instance = ReadInstance(type);
            if (instance != null) Cache[relativeTypeName] = instance;
            return instance;
        }

        /// <summary>
        /// A cached Unity object can be destroyed while the C# side still holds it. Under
        /// IL2CPP the wrapper then points at freed memory, so test before handing it out.
        /// </summary>
        private static bool IsDead(object o) =>
            o is null || (o is UnityEngine.Object u && u == null);

        private static object ReadInstance(Type type)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.FlattenHierarchy;
            try
            {
                return type.GetProperty("Instance", Flags)?.GetValue(null)
                    ?? type.GetField("Instance", Flags)?.GetValue(null);
            }
            catch { return null; }
        }

        /// <summary>
        /// Resolves a manager by its name relative to the game root, e.g.
        /// "Money.MoneyManager" or "Persistence.LoadManager". Returns null and logs once
        /// if it cannot be found.
        /// </summary>
        internal static object Resolve(string relativeTypeName)
        {
            if (Cache.TryGetValue(relativeTypeName, out var hit) && !IsDead(hit)) return hit;

            var type = GameTypes.Find(relativeTypeName);
            if (type is null)
            {
                if (Warned.Add(relativeTypeName))
                    Log.Error($"Game type '{Root}{relativeTypeName}' was not found. " +
                              "A game update probably renamed or moved it.");
                return null;
            }

            // Singleton<T> exposes Instance; which base it is does not change how we read it.
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Static | BindingFlags.FlattenHierarchy;
            object instance = null;
            try
            {
                instance = type.GetProperty("Instance", Flags)?.GetValue(null)
                        ?? type.GetField("Instance", Flags)?.GetValue(null);
            }
            catch (Exception ex)
            {
                Log.Error($"Reading {type.Name}.Instance threw.", ex);
            }

            if (instance is null && Warned.Add(relativeTypeName))
                Log.Warn($"{type.Name}.Instance is null right now. " +
                         "Managers do not exist until the world loads — resolve from " +
                         "GameLifecycle.OnLoadComplete, not OnInitializeMelon.");
            else if (instance != null)
                Cache[relativeTypeName] = instance;

            return instance;
        }

        /// <summary>
        /// A game type by its name relative to the game root ("ItemFramework.ItemSlot"),
        /// or null. Works from OnInitializeMelon — the assemblies are loaded even though
        /// the managers are not.
        /// </summary>
        internal static Type GameType(string relativeTypeName) => GameTypes.Find(relativeTypeName);
    }
}
