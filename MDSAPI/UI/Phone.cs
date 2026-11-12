using System;
using MDSAPI.Internal.Interop;
using MDSAPI.Logging;
using UnityEngine;

namespace MDSAPI.UI
{
    /// <summary>
    /// The player's phone, and the pieces a mod needs to put an app on it.
    /// All of it is per-player and exists only once the local player has spawned.
    /// </summary>
    public static class Phone
    {
        private const string PhoneType = "UI.Phone.Phone";
        private const string HomeType = "UI.Phone.HomeScreen";

        private static readonly Access<bool> IsOpenAccess = new(PhoneType, "IsOpen");
        private static readonly GameMethod Horizontal = new(PhoneType, null, "SetIsHorizontal");
        private static readonly GameMethod LookOffset = new(PhoneType, null, "SetLookOffsetMultiplier");
        private static readonly Access<object> CloseApps = new(PhoneType, "closeApps");

        private static readonly Access<object> IconPrefab = new(HomeType, "appIconPrefab");
        private static readonly Access<object> IconContainer = new(HomeType, "appIconContainer");
        private static readonly GameMethod HomeOpen = new(HomeType, null, "SetIsOpen");

        private static readonly GameMethod MenuClose = new("UI.GameplayMenu", null, "Close");
        private static readonly Access<object> AppContainer = new("UI.Phone.Delivery.DeliveryApp", "appContainer");

        static Phone() => Modules.Register("Phone", () =>
            IsOpenAccess.IsAvailable && Horizontal.IsAvailable && LookOffset.IsAvailable &&
            CloseApps.IsAvailable && IconPrefab.IsAvailable && IconContainer.IsAvailable &&
            HomeOpen.IsAvailable && AppContainer.IsAvailable);

        /// <summary>True when the phone members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Phone");

        private static object Instance => Singletons.TryResolve(PhoneType);
        private static object Home => Singletons.TryResolve(HomeType) ?? UnityFind.First(GameTypes.Find(HomeType));

        /// <summary>True when the phone exists (the player has spawned).</summary>
        public static bool Exists => Instance != null;

        /// <summary>
        /// True once the home screen and the app area exist — the moment a mod can add its
        /// icon and build its app. Retry on a timer until then.
        /// </summary>
        public static bool IsReadyForApps =>
            AppIconPrefab != null && AppIconContainer != null && TryGetAppArea(out _, out _);

        /// <summary>True while the phone is up.</summary>
        public static bool IsOpen
        {
            get
            {
                var p = Instance;
                return p != null && IsOpenAccess.Get(p);
            }
        }

        /// <summary>The phone's component — for its transform, its colliders, its camera.</summary>
        public static Component Component => UnityFind.AsComponent(Instance);

        /// <summary>Turns the phone sideways (as the map app does) or back upright.</summary>
        public static void SetHorizontal(bool horizontal)
        {
            var p = Instance;
            if (p != null) Horizontal.TryInvoke(p, horizontal);
        }

        /// <summary>
        /// How far the phone sits from the camera: 1 is normal, the map app uses 0.6 for its
        /// close-up view.
        /// </summary>
        public static void SetLookOffset(float multiplier)
        {
            var p = Instance;
            if (p != null) LookOffset.TryInvoke(p, multiplier);
        }

        /// <summary>
        /// Calls <paramref name="handler"/> when the game closes all apps (putting the phone
        /// away, opening another app). The phone is rebuilt with every scene, so call this
        /// each time you build your app — not once per session.
        /// </summary>
        public static bool OnCloseApps(Action handler)
        {
            var p = Instance;
            if (p is null || handler is null || !CloseApps.IsAvailable) return false;
            try
            {
                var current = CloseApps.Get(p);
#if MDS_IL2CPP
                var add = (Il2CppSystem.Action)handler;
                var cur = current is null ? null : Cast.To(current, typeof(Il2CppSystem.Action)) as Il2CppSystem.Action;
                var combined = cur is null ? add : Il2CppSystem.Delegate.Combine(cur, add).Cast<Il2CppSystem.Action>();
                return CloseApps.Set(p, combined);
#else
                return CloseApps.Set(p, Delegate.Combine(current as Delegate, handler));
#endif
            }
            catch (Exception ex)
            {
                Failures.Once("Phone.closeApps", ex);
                return false;
            }
        }

        /// <summary>Puts the phone away the way the game does (closing the gameplay menu).</summary>
        public static void PutAway()
        {
            var menu = Singletons.TryResolve("UI.GameplayMenu") ?? UnityFind.First(GameTypes.Find("UI.GameplayMenu"));
            if (menu != null) MenuClose.TryInvoke(menu);
        }

        /// <summary>The home screen's app-icon prefab; instantiate it under <see cref="AppIconContainer"/>.</summary>
        public static GameObject AppIconPrefab
        {
            get
            {
                var h = Home;
                return h is null ? null : AsGameObject(IconPrefab.Get(h));
            }
        }

        /// <summary>Where the home screen's app icons live.</summary>
        public static Transform AppIconContainer
        {
            get
            {
                var h = Home;
                var c = h is null ? null : IconContainer.Get(h);
                return c is null ? null : (c as Transform ?? UnityFind.AsComponent(c)?.transform);
            }
        }

        /// <summary>Shows or hides the home screen (hide it while your app is up).</summary>
        public static void SetHomeScreenOpen(bool open)
        {
            var h = Home;
            if (h != null) HomeOpen.TryInvoke(h, open);
        }

        /// <summary>
        /// Where the game's own apps draw: the rect of a built-in app (Deliveries) and the rect
        /// of its screen area. Put your app beside it, with the same rects, and it lines up
        /// with the phone screen exactly. Nulls if the phone is not built yet.
        /// </summary>
        public static bool TryGetAppArea(out RectTransform appRect, out RectTransform screenRect)
        {
            appRect = null; screenRect = null;
            foreach (var app in UnityFind.Everything(GameTypes.Find("UI.Phone.Delivery.DeliveryApp")))
            {
                var c = UnityFind.AsComponent(app);
                if (c == null || !c.gameObject.scene.IsValid()) continue; // skip the prefab asset
                appRect = c.GetComponent<RectTransform>();
                var container = AppContainer.Get(app);
                screenRect = container as RectTransform ?? UnityFind.AsComponent(container)?.GetComponent<RectTransform>();
                break;
            }
            return appRect != null && screenRect != null && appRect.parent != null;
        }

        private static GameObject AsGameObject(object o)
        {
            if (o is null) return null;
            if (o is GameObject g) return g;
            var c = UnityFind.AsComponent(o);
            if (c != null) return c.gameObject;
            return Cast.To(o, typeof(GameObject)) as GameObject;
        }
    }
}
