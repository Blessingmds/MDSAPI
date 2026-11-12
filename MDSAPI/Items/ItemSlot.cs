using System;
using MDSAPI.Internal.Interop;
using MDSAPI.Unity;
using MDSAPI.World;

namespace MDSAPI.Items
{
    /// <summary>A stack of items: a definition and a quantity, sitting in a slot.</summary>
    public readonly struct ItemInstance : IEquatable<ItemInstance>
    {
        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal ItemInstance(object native) => Native = native;

        /// <summary>False for an empty handle.</summary>
        public bool Exists => Native != null;

        /// <summary>Stable identity, for dictionary keys.</summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>What kind of item this is.</summary>
        public ItemDefinition Definition => new(Slots.Definition.Get(Native));

        /// <summary>The display name, which can differ from the definition's (a named mix).</summary>
        public string Name => Slots.InstanceName.Get(Native);

        /// <summary>How many are in this stack.</summary>
        public int Quantity => Slots.InstanceQuantity.Get(Native);

        /// <summary>
        /// Whether the game would merge <paramref name="other"/> into this stack — same item,
        /// same quality, same packaging. Quantity is not considered.
        /// </summary>
        public bool CanStackWith(ItemInstance other) =>
            Exists && other.Exists && Slots.CanStackWith.Call(Native, false, other.Native, false);

        /// <inheritdoc/>
        public bool Equals(ItemInstance other) => Key == other.Key;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemInstance i && Equals(i);
        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();
    }

    /// <summary>
    /// One inventory slot — in the player's pockets, a shelf, a station, a vehicle trunk, an
    /// NPC. Slots are where stack limits are enforced.
    /// </summary>
    public readonly struct ItemSlot : IEquatable<ItemSlot>
    {
        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal ItemSlot(object native) => Native = native;

        /// <summary>False for an empty handle.</summary>
        public bool Exists => Native != null;

        /// <summary>
        /// Stable identity, for dictionary keys. Slots live as long as their owner, so a cache
        /// keyed by this should be cleared on scene change.
        /// </summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>How many items are in the slot.</summary>
        public int Quantity => Slots.Quantity.Get(Native);

        /// <summary>What is in the slot; check <see cref="ItemInstance.Exists"/>.</summary>
        public ItemInstance Item => new(Slots.Instance.Get(Native));

        /// <summary>What the slot belongs to — a rack, a shelf, an NPC, the player.</summary>
        public SlotOwner Owner => new(Slots.Owner.Get(Native));

        /// <summary>
        /// How many of <paramref name="item"/> the game would put into this slot right now,
        /// with every capacity filter (including other mods') applied.
        /// </summary>
        public int CapacityFor(ItemInstance item) =>
            Exists && item.Exists ? Slots.Capacity.Call(Native, 0, item.Native, false) : 0;

        /// <inheritdoc/>
        public bool Equals(ItemSlot other) => Key == other.Key;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ItemSlot s && Equals(s);
        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();
    }

    internal static class Slots
    {
        internal const string SlotType = "ItemFramework.ItemSlot";
        internal const string InstanceType = "ItemFramework.ItemInstance";

        internal static readonly Access<int> Quantity = new(SlotType, "Quantity");
        internal static readonly Access<object> Instance = new(SlotType, "ItemInstance");
        internal static readonly Access<object> Owner = new(SlotType, "SlotOwner");
        internal static readonly GameMethod Capacity = new(SlotType, null, "GetCapacityForItem");

        internal static readonly Access<object> Definition = new(InstanceType, "Definition");
        internal static readonly Access<string> InstanceName = new(InstanceType, "Name");
        internal static readonly Access<int> InstanceQuantity = new(InstanceType, "Quantity");
        internal static readonly GameMethod CanStackWith = new(InstanceType, null, "CanStackWith");

        /// <summary>The slots in a game collection of them.</summary>
        internal static ItemSlot[] FromList(object list)
        {
            var items = Seq.Items(list);
            var result = new ItemSlot[items.Count];
            for (var i = 0; i < items.Count; i++) result[i] = new ItemSlot(items[i]);
            return result;
        }
    }
}
