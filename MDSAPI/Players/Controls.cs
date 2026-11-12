using System;
using System.Collections.Generic;
using System.Reflection;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;
using UnityEngine;

namespace MDSAPI.Players
{
    /// <summary>The local player.</summary>
    public static class LocalPlayer
    {
        private static readonly Access<object> Local = new("PlayerScripts.Player", "Local");

        /// <summary>
        /// True once the local player has spawned — the point from which player-bound
        /// managers (camera, inventory, phone) exist. False on the menu and while loading.
        /// </summary>
        public static bool Exists
        {
            get
            {
                if (UnityFind.Alive(Local.Get(null))) return true;
                var go = GameObject.Find("Player_Local");
                return go != null && go.activeInHierarchy;
            }
        }
    }

    /// <summary>
    /// Taking the player's controls for a menu, and giving them back exactly as they were.
    /// <para>
    /// A mod window that does not do all of this lets the camera swing while the player
    /// drags a slider, lets them walk off, or scrolls their hotbar under the cursor — and one
    /// that restores carelessly locks the cursor while the game's own storage screen is still
    /// open. Each mod blocks under its own name; controls come back when the last one lets go.
    /// </para>
    /// </summary>
    public static class PlayerControls
    {
        private const string Camera = "PlayerScripts.PlayerCamera";
        private const string Movement = "PlayerScripts.PlayerMovement";
        private const string Inventory = "PlayerScripts.PlayerInventory";

        private static readonly GameMethod AddUi = new(Camera, null, "AddActiveUIElement");
        private static readonly GameMethod RemoveUi = new(Camera, null, "RemoveActiveUIElement");
        private static readonly GameMethod FreeMouse = new(Camera, null, "FreeMouse");
        private static readonly GameMethod LockMouse = new(Camera, null, "LockMouse");
        private static readonly GameMethod SetCanLook = new(Camera, null, "SetCanLook");
        private static readonly Access<int> UiCount = new(Camera, "ActiveUIElementCount");
        private static readonly Access<bool> CanMove = new(Movement, "CanMove");
        private static readonly GameMethod SetEquipping = new(Inventory, null, "SetEquippingEnabled");

        private static readonly HashSet<string> Owners = new();
        private static bool _couldMove = true;

        static PlayerControls() => Modules.Register("PlayerControls", () =>
            AddUi.IsAvailable && RemoveUi.IsAvailable && FreeMouse.IsAvailable && LockMouse.IsAvailable &&
            SetCanLook.IsAvailable && UiCount.IsAvailable && CanMove.IsAvailable && SetEquipping.IsAvailable);

        /// <summary>True when these members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("PlayerControls");

        /// <summary>True while any mod holds the controls.</summary>
        public static bool IsBlocked => Owners.Count > 0;

        /// <summary>
        /// How many UI screens the game considers open — its own and mods'. Zero means the
        /// player is free-roaming.
        /// </summary>
        public static int OpenScreens
        {
            get
            {
                var cam = Singletons.TryResolve(Camera);
                return cam is null ? 0 : UiCount.Get(cam);
            }
        }

        /// <summary>
        /// Frees the cursor and stops look, movement and equipping, under
        /// <paramref name="owner"/> (use your mod's name). Calling it again with the same
        /// owner does nothing.
        /// </summary>
        public static void Block(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !Owners.Add(owner)) return;

            var cam = Singletons.TryResolve(Camera);
            if (cam != null)
            {
                AddUi.TryInvoke(cam, owner);
                FreeMouse.TryInvoke(cam, true);
                SetCanLook.TryInvoke(cam, false);
            }
            if (Owners.Count == 1)
            {
                var move = Singletons.TryResolve(Movement);
                if (move != null)
                {
                    _couldMove = CanMove.Get(move, true);
                    CanMove.Set(move, false);
                }
                var inv = Singletons.TryResolve(Inventory);
                if (inv != null) SetEquipping.TryInvoke(inv, false);
            }
        }

        /// <summary>
        /// Gives back what <see cref="Block"/> took. Look and cursor lock return only when no
        /// other screen — the game's or another mod's — still needs the cursor.
        /// </summary>
        public static void Unblock(string owner)
        {
            if (string.IsNullOrEmpty(owner) || !Owners.Remove(owner)) return;

            var cam = Singletons.TryResolve(Camera);
            if (cam != null)
            {
                RemoveUi.TryInvoke(cam, owner);
                if (UiCount.Get(cam) == 0)
                {
                    SetCanLook.TryInvoke(cam, true);
                    LockMouse.TryInvoke(cam, true);
                }
            }
            if (Owners.Count == 0)
            {
                var move = Singletons.TryResolve(Movement);
                if (move != null) CanMove.Set(move, _couldMove);
                var inv = Singletons.TryResolve(Inventory);
                if (inv != null) SetEquipping.TryInvoke(inv, true);
            }
        }

        /// <summary>Scene change destroyed the player: forget holds without touching anything.</summary>
        internal static void Reset()
        {
            Owners.Clear();
            _couldMove = true;
        }
    }

    /// <summary>
    /// Handles the "back" input (Escape, or the pad's back button) for a mod screen.
    /// Return true to consume it, so the game does not also act on it.
    /// </summary>
    public delegate bool ExitHandler();

    /// <summary>Keyboard and the game's input state.</summary>
    public static class Controls
    {
        private static readonly Access<bool> Typing = new("GameInput", "IsTyping");
        private static readonly GameMethod Register = new("GameInput", "ExitDelegate", "RegisterExitListener");
        private static readonly Access<bool> Used = new("ExitAction", "Used");

        static Controls() => Modules.Register("Controls", () =>
            Typing.IsAvailable && Register.IsAvailable && Used.IsAvailable);

        /// <summary>True when these members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Controls");

        /// <summary>
        /// True while a text field has focus. Check it before acting on a hotkey, or typing
        /// a name that contains the key opens your menu. Set it while your own text field has
        /// focus so the game does not act on the keys either.
        /// </summary>
        public static bool IsTyping
        {
            get => Typing.Get(null);
            set => Typing.Set(null, value);
        }

        private static readonly List<object> Keep = new();

        /// <summary>
        /// Adds a handler for the back input. Higher <paramref name="priority"/> runs first;
        /// the game's own screens use 0–10. The handler is not called when something earlier
        /// already consumed the input. Lasts for the session — add it once.
        /// </summary>
        public static bool AddExitHandler(ExitHandler handler, int priority = 10)
        {
            if (handler is null || !Register.IsAvailable) return false;
            try
            {
                var method = Register.Method;
                var delegateType = method.GetParameters()[0].ParameterType;
                var actionType = delegateType.GetMethod("Invoke").GetParameters()[0].ParameterType;

                // Il2CppInterop marshals by the managed delegate's signature, so it has to be
                // an Action of the game's exact ExitAction type, built at runtime.
                var bridge = new ExitBridge(handler);
                var closed = typeof(ExitBridge).GetMethod(nameof(ExitBridge.Handle), BindingFlags.Instance | BindingFlags.NonPublic)
                                               .MakeGenericMethod(actionType);
                var action = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(actionType), bridge, closed);
                var native = Delegates.ConvertTo(delegateType, action);
                if (native is null)
                {
                    Log.Warn("Could not build an exit listener on this runtime; the back key will not close mod screens.");
                    return false;
                }
                Keep.Add(native); // the game holds it natively; keep the managed side alive too
                Keep.Add(action);
                Register.Invoke(null, native, priority);
                return true;
            }
            catch (Exception ex)
            {
                Failures.Once("GameInput.RegisterExitListener", ex);
                return false;
            }
        }

        private sealed class ExitBridge
        {
            private readonly ExitHandler _handler;
            internal ExitBridge(ExitHandler handler) => _handler = handler;

            internal void Handle<TExit>(TExit exit)
            {
                try
                {
                    if (exit == null || Used.Get(exit)) return;
                    if (_handler()) Used.Set(exit, true);
                }
                catch (Exception ex) { Failures.Once("an exit handler from " + _handler.Method.DeclaringType?.Assembly.GetName().Name, ex); }
            }
        }
    }
}
