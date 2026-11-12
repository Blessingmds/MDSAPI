using MDSAPI.Internal.Interop;

namespace MDSAPI.World
{
    /// <summary>What the player is looking at and has open.</summary>
    public static class Interaction
    {
        private const string Manager = "Interaction.InteractionManager";
        private static readonly Access<object> Hovered = new(Manager, "HoveredValidInteractableObject");

        private const string Menu = "UI.StorageMenu";
        private static readonly Access<bool> MenuOpen = new(Menu, "IsOpen");
        private static readonly Access<object> MenuStorage = new(Menu, "OpenedStorageEntity");

        static Interaction() => Modules.Register("Interaction", () =>
            Hovered.IsAvailable && MenuOpen.IsAvailable && MenuStorage.IsAvailable);

        /// <summary>True when these members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Interaction");

        /// <summary>
        /// The placed object the player's "[E] Use ..." prompt is for, or empty. This is
        /// exactly what the game considers interactable under the crosshair, so a hint shown
        /// for it appears on the same parts of the object as the game's own.
        /// </summary>
        public static Buildable HoveredBuildable
        {
            get
            {
                var manager = Singletons.TryResolve(Manager);
                var hovered = manager is null ? null : Hovered.Get(manager);
                return hovered is null ? default : new Buildable(UnityFind.InParent(hovered, WorldTypes.BuildableType));
            }
        }

        /// <summary>True while the game's storage screen is open.</summary>
        public static bool StorageMenuOpen
        {
            get
            {
                var menu = Singletons.TryResolve(Menu);
                return menu != null && MenuOpen.Get(menu);
            }
        }

        /// <summary>The storage the game's storage screen is showing, or empty.</summary>
        public static Storage OpenStorage
        {
            get
            {
                var menu = Singletons.TryResolve(Menu);
                return menu != null && MenuOpen.Get(menu) ? new Storage(MenuStorage.Get(menu)) : default;
            }
        }
    }
}
