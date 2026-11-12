using System.Collections.Generic;
using MDSAPI.Internal.Interop;

namespace MDSAPI.Items
{
    /// <summary>Products — the drugs — beyond what every item has.</summary>
    public static class Products
    {
        private const string Manager = "Product.ProductManager";

        // The game's own field per drug type. Keyed by the drug type's name as
        // ItemDefinition.DrugType reports it.
        private static readonly Dictionary<string, Access<object>> Defaults = new()
        {
            ["Marijuana"] = new Access<object>(Manager, "DefaultWeed"),
            ["Methamphetamine"] = new Access<object>(Manager, "DefaultMeth"),
            ["Cocaine"] = new Access<object>(Manager, "DefaultCocaine"),
            ["Shrooms"] = new Access<object>(Manager, "DefaultShroom"),
        };

        static Products() => Modules.Register("Products", () =>
            Defs.DrugTypes.IsAvailable && Defs.DrugTypeOfContainer.IsAvailable &&
            Defaults["Marijuana"].IsAvailable);

        /// <summary>True when the product members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Products");

        /// <summary>
        /// The base product of a drug type ("Marijuana" gives OG Kush). Every mix the player
        /// makes is a copy of its drug type's base product — including whatever stack limit
        /// that product had at the moment of mixing. Empty before the world has loaded.
        /// </summary>
        public static ItemDefinition DefaultFor(string drugType)
        {
            if (drugType is null || !Defaults.TryGetValue(drugType, out var access)) return default;
            var manager = Singletons.TryResolve(Manager);
            return manager is null ? default : new ItemDefinition(access.Get(manager));
        }
    }
}
