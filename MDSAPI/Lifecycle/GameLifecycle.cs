using System;
using System.Linq;
using System.Reflection;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;

namespace MDSAPI.Lifecycle
{
    /// <summary>
    /// When a mod is allowed to do things.
    /// <para>
    /// The game's managers do not exist during <c>OnInitializeMelon</c> — the main scene
    /// has not loaded. Subscribe there, act here. These are the two moments that matter:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="OnPreLoad"/> — register content the save file will need to resolve
    /// (item and product definitions, anything with a durable id). The world is not up yet.</item>
    /// <item><see cref="OnLoadComplete"/> — the world, the managers and the player exist.
    /// Everything else goes here.</item>
    /// </list>
    /// </summary>
    public static class GameLifecycle
    {
        /// <summary>Before the save is read. Register durable content here.</summary>
        public static event Action OnPreLoad;

        /// <summary>The save is loaded and the world exists. The normal place to start.</summary>
        public static event Action OnLoadComplete;

        /// <summary>A save is about to be written.</summary>
        public static event Action OnSaveStart;

        /// <summary>A save has finished being written.</summary>
        public static event Action OnSaveComplete;

        /// <summary>True between <see cref="OnLoadComplete"/> and returning to the menu.</summary>
        public static bool IsInGame { get; private set; }

        private static bool _bound;

        /// <summary>
        /// Asks the game to save now. Returns false, having logged, if it could not.
        /// <para>
        /// <b>Use this sparingly, and never on a timer.</b> Players rely on quitting
        /// without saving as an undo — loading a save, trying something, then reloading to
        /// discard it. A mod that saves on its own takes that away silently, and the player
        /// has no way to know which mod did it.
        /// </para>
        /// <para>
        /// Legitimate uses are rare and player-initiated: they pressed a button that
        /// completed something, or a mod feature explicitly promises to persist
        /// immediately. "I would rather not lose this" is not a good enough reason —
        /// MDSAPI already writes mod data whenever the game itself saves, so doing nothing
        /// is the well-behaved default.
        /// </para>
        /// </summary>
        public static bool RequestSave()
        {
            var save = Singletons.Resolve("Persistence.SaveManager");
            if (save is null) return false;

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

            // SaveManager has two public Save overloads, so asking for it by name alone
            // throws AmbiguousMatchException. Take the parameterless one.
            var method = save.GetType().GetMethods(Flags)
                .FirstOrDefault(m => m.Name == "Save" && m.GetParameters().Length == 0);

            if (method is null)
            {
                Log.Error("Could not find a parameterless SaveManager.Save(). " +
                          "A game update likely changed it; saving on request is " +
                          "unavailable this session.");
                return false;
            }

            try { method.Invoke(save, null); return true; }
            catch (Exception ex) { Log.Error("Asking the game to save failed.", ex); return false; }
        }

        internal static void Bind()
        {
            if (_bound) return;

            // LoadManager and SaveManager are PersistentSingleton<T>, so they exist before
            // the main scene and survive it. Verified against game 0.4.7f12:
            //   LoadManager.onPreLoad, onLoadComplete
            //   SaveManager.onSaveStart, onSaveComplete
            // The declared type of those members is resolved at runtime rather than
            // assumed, because UnityEvent and a plain delegate need different subscription
            // and guessing wrong fails silently — the worst possible failure here.
            var load = Singletons.Resolve("Persistence.LoadManager");
            var save = Singletons.Resolve("Persistence.SaveManager");

            var shapes = new[]
            {
                Subscribe(load, "onPreLoad",      () => { IsInGame = false; Raise(OnPreLoad, "OnPreLoad"); }),
                Subscribe(load, "onLoadComplete", () => { IsInGame = true;  Raise(OnLoadComplete, "OnLoadComplete"); }),
                Subscribe(save, "onSaveStart",    () => Raise(OnSaveStart, "OnSaveStart")),
                Subscribe(save, "onSaveComplete", () => Raise(OnSaveComplete, "OnSaveComplete")),
            };
            var bound = shapes.Count(x => x != null);

            _bound = true;

            if (bound == shapes.Length)
            {
                Log.Info("Lifecycle hooks attached (" + shapes[0] + ").");
            }
            else
            {
                Log.Error(bound + " of " + shapes.Length + " lifecycle hooks attached. " +
                          "Mods built on MDSAPI will not receive the rest this session. " +
                          "This usually means a game update moved them — please report " +
                          "with your game version.");
            }
        }

        private static void Raise(Action handlers, string name)
        {
            if (handlers is null) return;
            // One misbehaving mod must not stop the others from being told.
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception ex)
                {
                    Log.Error($"A handler for {name} threw. Its mod may be in a bad state.", ex);
                }
            }
        }

        /// <summary>
        /// Resolves a game lifecycle member and hands it to <see cref="EventBridge"/>.
        /// <para>
        /// Measured at 0.4.7f12: these members are fields on Mono and properties on
        /// IL2CPP, so the shape cannot be named at compile time without the source
        /// diverging per runtime. Returns what it bound to, or null on failure.
        /// </para>
        /// </summary>
        private static string Subscribe(object owner, string memberName, Action handler)
        {
            if (owner is null)
            {
                Log.Error("Could not resolve the manager that owns '" + memberName + "'.");
                return null;
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
            var type = owner.GetType();

            var prop = type.GetProperty(memberName, Flags);
            if (prop != null && prop.CanRead)
                return EventBridge.Attach(prop.GetValue(owner), handler, memberName) ? "property" : null;

            var field = type.GetField(memberName, Flags);
            if (field != null)
                return EventBridge.Attach(field.GetValue(owner), handler, memberName) ? "field" : null;

            // A C# event exposes no readable member — attach through add_X instead. This
            // shape has no AddListener, so it does not go through EventBridge.
            var adder = Reflect.FindSingleParameterMethod(type, "add_" + memberName, Flags);
            if (adder is null)
            {
                Log.Error("'" + memberName + "' was not found on " + type.Name +
                          ". A game update likely moved or renamed it.");
                return null;
            }

            var listener = Delegates.CreateHandlerFor(adder.GetParameters()[0].ParameterType, handler);
            if (listener is null)
            {
                Log.Error("Could not build a handler for the event '" + memberName + "'.");
                return null;
            }

            try { adder.Invoke(owner, new[] { listener }); return "event"; }
            catch (Exception ex) { Log.Error("Could not attach to the event '" + memberName + "'.", ex); return null; }
        }
    }
}
