using System;
using MDSAPI.Internal.Interop;
using MDSAPI.Unity;
using UnityEngine;

namespace MDSAPI.Items
{
    /// <summary>
    /// A kind of item — "OG Kush", "Baggie", "Cash" — as opposed to a stack of it
    /// (<see cref="ItemInstance"/>). A handle: copying it is free, and two handles to the same
    /// definition compare equal.
    /// </summary>
    public readonly struct ItemDefinition : IEquatable<ItemDefinition>
    {
        /// <summary>The game object behind this handle, for anything MDSAPI does not wrap yet.</summary>
        public object Native { get; }

        internal ItemDefinition(object native) => Native = native;

        /// <summary>False for an empty handle or a definition Unity has destroyed.</summary>
        public bool Exists => UnityFind.Alive(Native);

        /// <summary>Stable identity, for dictionary keys.</summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>The item ID ("ogkush"). The thing to persist — never the display name.</summary>
        public string Id => Defs.Id.Get(Native);

        /// <summary>The display name.</summary>
        public string Name => Defs.Name.Get(Native);

        /// <summary>The inventory icon.</summary>
        public Sprite Icon => Defs.Icon.Get(Native) as Sprite;

        /// <summary>The game's category, by name ("Product", "Packaging", ...).</summary>
        public string Category => Defs.Category.Get(Native)?.ToString();

        /// <summary>How many fit in one slot. Writing it changes the game's limit for every slot.</summary>
        public int StackLimit
        {
            get => Defs.StackLimit.Get(Native);
            set => Defs.StackLimit.Set(Native, value);
        }

        /// <summary>
        /// True for items that carry their own count inside one unit (cash and similar) —
        /// the game's <c>IntegerItemDefinition</c>. Their stack size means something else.
        /// </summary>
        public bool HasOwnCount => Cast.Is(Native, Defs.IntegerType);

        /// <summary>True when this is a product (a drug, including mixes).</summary>
        public bool IsProduct => Cast.Is(Native, Defs.ProductType);

        /// <summary>
        /// The product's drug type by name ("Marijuana", "Methamphetamine", "Cocaine",
        /// "Shrooms"), or null for non-products and for product templates the game has not
        /// filled in yet — reading those through the game's own getter throws.
        /// </summary>
        public string DrugType
        {
            get
            {
                if (!IsProduct) return null;
                var list = Defs.DrugTypes.Get(Native);
                if (Seq.Count(list) == 0) return null;
                return Defs.DrugTypeOfContainer.Get(Seq.At(list, 0))?.ToString();
            }
        }

        /// <inheritdoc/>
        public bool Equals(ItemDefinition other) => Key == other.Key;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemDefinition d && Equals(d);
        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();
        /// <inheritdoc/>
        public override string ToString() => Exists ? Id : "(no item)";
    }

    internal static class Defs
    {
        internal const string Type = "ItemFramework.ItemDefinition";

        internal static readonly Access<string> Id = new(Type, "ID");
        internal static readonly Access<string> Name = new(Type, "Name");
        internal static readonly Access<object> Icon = new(Type, "Icon");
        internal static readonly Access<object> Category = new(Type, "Category");
        internal static readonly Access<int> StackLimit = new(Type, "StackLimit");
        internal static readonly Access<object> DrugTypes = new("Product.ProductDefinition", "DrugTypes");
        internal static readonly Access<object> DrugTypeOfContainer = new("Product.DrugTypeContainer", "DrugType");

        private static Type _integer, _product, _definition;
        internal static Type IntegerType => _integer ??= GameTypes.Find("ItemFramework.IntegerItemDefinition");
        internal static Type ProductType => _product ??= GameTypes.Find("Product.ProductDefinition");
        internal static Type DefinitionType => _definition ??= GameTypes.Find(Type);
    }
}
