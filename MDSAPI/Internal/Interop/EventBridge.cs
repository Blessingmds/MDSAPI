using System;
using System.Collections.Generic;
using System.Reflection;
using MDSAPI.Logging;

namespace MDSAPI.Internal.Interop
{
    /// <summary>
    /// The one place a managed <see cref="Action"/> is attached to or detached from a game
    /// event, on either runtime.
    /// <para>
    /// There used to be two paths for this — this class, and a second one inside
    /// GameLifecycle — and only the second learned that an IL2CPP listener is an
    /// <c>Il2CppSystem.Action</c> rather than a delegate. Two paths means a fix lands in
    /// one of them, so there is now one.
    /// </para>
    /// </summary>
    internal static class EventBridge
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static;

        /// <summary>
        /// What was actually handed to AddListener, per (event, handler) pair.
        /// <para>
        /// Keeping it is what makes detaching possible: the object passed to AddListener is
        /// built here, so without remembering it RemoveListener is given a different object
        /// and silently removes nothing. Keyed on the event as well as the handler, because
        /// the same handler attached to two events is two subscriptions.
        /// </para>
        /// </summary>
        private static readonly Dictionary<(object, Action), object> Bound = new();

        /// <summary>Attaches <paramref name="handler"/>. Returns false, having logged, on failure.</summary>
        internal static bool Attach(object gameEvent, Action handler, string describedAs)
        {
            if (gameEvent is null || handler is null) return false;

            var key = (gameEvent, handler);
            if (Bound.ContainsKey(key)) return true;   // attaching twice is a bug, not a double-fire

            var add = Reflect.FindSingleParameterMethod(gameEvent.GetType(), "AddListener", Flags);
            if (add is null)
            {
                Log.Error("'" + describedAs + "' is a " + gameEvent.GetType().Name +
                          ", which MDSAPI does not know how to subscribe to.");
                return false;
            }

            var parameterType = add.GetParameters()[0].ParameterType;
            var listener = Delegates.CreateHandlerFor(parameterType, handler);
            if (listener is null)
            {
                Log.Error("Could not build a " + parameterType.Name + " to attach to '" +
                          describedAs + "'. Please report this with your game version.");
                return false;
            }

            try
            {
                add.Invoke(gameEvent, new[] { listener });
                Bound[key] = listener;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not attach to '" + describedAs + "'.", ex);
                return false;
            }
        }

        /// <summary>Detaches a handler attached by <see cref="Attach"/>.</summary>
        internal static bool Detach(object gameEvent, Action handler)
        {
            if (gameEvent is null || handler is null) return false;

            var key = (gameEvent, handler);
            // Return rather than call RemoveListener(null), which does nothing useful and
            // throws on some shapes.
            if (!Bound.TryGetValue(key, out var listener)) return false;
            Bound.Remove(key);

            var remove = Reflect.FindSingleParameterMethod(gameEvent.GetType(), "RemoveListener", Flags);
            if (remove is null) return false;

            try { remove.Invoke(gameEvent, new[] { listener }); return true; }
            catch (Exception ex) { Log.Error("Could not detach from a game event.", ex); return false; }
        }

        /// <summary>
        /// Forgets every binding. Called on scene unload so destroyed UnityEvents are not
        /// held alive for the rest of the session.
        /// </summary>
        internal static void Clear() => Bound.Clear();
    }
}
