using System;
using System.Collections.Generic;
using MDSAPI.Internal.Interop;
using MDSAPI.Items;
using MDSAPI.Unity;
using UnityEngine;

namespace MDSAPI.World
{
    /// <summary>
    /// A container: a shelf, a safe, a vehicle trunk, the case on an employee's bed.
    /// </summary>
    public readonly struct Storage : IEquatable<Storage>
    {
        private static readonly Access<object> SlotList = new("Storage.StorageEntity", "ItemSlots");

        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal Storage(object native) => Native = native;

        /// <summary>False for an empty handle or a destroyed object.</summary>
        public bool Exists => UnityFind.Alive(Native);

        /// <summary>Identity for this session.</summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>The Unity component.</summary>
        public Component Component => UnityFind.AsComponent(Native);

        /// <summary>The storage's slots, in order.</summary>
        public ItemSlot[] Slots => Exists ? MDSAPI.Items.Slots.FromList(SlotList.Get(Native)) : Array.Empty<ItemSlot>();

        /// <summary>The first slot, without building the whole array.</summary>
        public ItemSlot FirstSlot => Exists ? new ItemSlot(Seq.At(SlotList.Get(Native), 0)) : default;

        /// <summary>The vehicle this storage is the trunk of, if any.</summary>
        public Vehicle Vehicle => new(UnityFind.InParent(Native, WorldTypes.LandVehicleType));

        /// <summary>True for the storage in a delivery van (shop deliveries, not the player's cars).</summary>
        public bool IsDeliveryVehicle => UnityFind.InParent(Native, WorldTypes.DeliveryVehicleType) != null;

        /// <summary>The placed object this storage belongs to, if any.</summary>
        public Buildable Buildable => new(UnityFind.InParent(Native, WorldTypes.BuildableType));

        /// <inheritdoc/>
        public bool Equals(Storage other) => Key == other.Key;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Storage s && Equals(s);
        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();
    }

    /// <summary>
    /// What an <see cref="ItemSlot"/> belongs to. The game hands this out as a bare
    /// interface; these properties say what is actually behind it.
    /// </summary>
    public readonly struct SlotOwner
    {
        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal SlotOwner(object native) => Native = native;

        /// <summary>False when the slot has no owner (the player's own pockets report none).</summary>
        public bool Exists => Native != null;

        /// <summary>Identity for this session.</summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>Which station owns the slot; None if not a station.</summary>
        public StationType Station => Stations.TypeOf(Native);

        /// <summary>The owner as a placed object (stations are); empty otherwise.</summary>
        public Buildable Buildable => new(Cast.To(Native, WorldTypes.BuildableType));

        /// <summary>The owner as a container (shelves, trunks); empty otherwise.</summary>
        public Storage Storage => new(Cast.To(Native, WorldTypes.StorageType));

        /// <summary>True for an NPC's or a dealer's inventory.</summary>
        public bool IsNpc => Cast.Is(Native, WorldTypes.NpcInventoryType) || Cast.Is(Native, WorldTypes.DealerType);
    }

    /// <summary>A vehicle, placed or as a prefab in the vehicle list.</summary>
    public readonly struct Vehicle
    {
        private static readonly Access<string> NameAccess = new("Vehicles.LandVehicle", "VehicleName");
        private static readonly Access<string> CodeAccess = new("Vehicles.LandVehicle", "VehicleCode");

        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal Vehicle(object native) => Native = native;

        /// <summary>False for an empty handle.</summary>
        public bool Exists => UnityFind.Alive(Native);

        /// <summary>The model code ("shitbox"). Variants of one model can have different codes.</summary>
        public string Code => CodeAccess.Get(Native) ?? "";

        /// <summary>The display name; falls back to the code.</summary>
        public string Name
        {
            get
            {
                var n = NameAccess.Get(Native);
                return string.IsNullOrEmpty(n) ? Code : n;
            }
        }
    }

    /// <summary>Vehicles.</summary>
    public static class Vehicles
    {
        private const string Manager = "Vehicles.VehicleManager";
        private static readonly Access<object> Prefabs = new(Manager, "VehiclePrefabs");

        /// <summary>
        /// Every vehicle model the game can spawn, from the vehicle manager's prefab list.
        /// Empty before the world has loaded.
        /// </summary>
        public static List<Vehicle> AllModels()
        {
            var list = new List<Vehicle>();
            var manager = Singletons.TryResolve(Manager) ?? UnityFind.First(GameTypes.Find(Manager));
            if (manager is null) return list;
            foreach (var v in Seq.Items(Prefabs.Get(manager)))
                if (v != null) list.Add(new Vehicle(v));
            return list;
        }
    }
}
