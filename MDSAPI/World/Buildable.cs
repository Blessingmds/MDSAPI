using System;
using System.Collections.Generic;
using MDSAPI.Internal.Interop;
using MDSAPI.Unity;
using UnityEngine;

namespace MDSAPI.World
{
    /// <summary>The game's work stations, by what they are rather than by class name.</summary>
    public enum StationType
    {
        /// <summary>Not a station.</summary>
        None,
        /// <summary>Drying rack.</summary>
        DryingRack,
        /// <summary>Mixing station, either mark.</summary>
        MixingStation,
        /// <summary>Brick press.</summary>
        BrickPress,
        /// <summary>Cauldron.</summary>
        Cauldron,
        /// <summary>Chemistry station.</summary>
        ChemistryStation,
        /// <summary>Lab oven.</summary>
        LabOven,
        /// <summary>Packaging station, either mark.</summary>
        PackagingStation,
    }

    /// <summary>Stations: finding them and telling them apart.</summary>
    public static class Stations
    {
        // Upgraded marks (MixingStationMk2, PackagingStationMk2) derive from the first mark,
        // so a type test against the base catches both.
        private static readonly (StationType Type, string GameType)[] Table =
        {
            (StationType.DryingRack, "ObjectScripts.DryingRack"),
            (StationType.MixingStation, "ObjectScripts.MixingStation"),
            (StationType.BrickPress, "ObjectScripts.BrickPress"),
            (StationType.Cauldron, "ObjectScripts.Cauldron"),
            (StationType.ChemistryStation, "ObjectScripts.ChemistryStation"),
            (StationType.LabOven, "ObjectScripts.LabOven"),
            (StationType.PackagingStation, "ObjectScripts.PackagingStation"),
        };

        internal static readonly Access<int> RackCapacity = new("ObjectScripts.DryingRack", "ItemCapacity");
        internal static readonly Access<int> MixerMax = new("ObjectScripts.MixingStation", "MaxMixQuantity");

        static Stations() => Modules.Register("Stations", () =>
            GameTypeOf(StationType.DryingRack) != null && GameTypeOf(StationType.MixingStation) != null &&
            RackCapacity.IsAvailable && MixerMax.IsAvailable);

        /// <summary>True when the station types were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Stations");

        internal static Type GameTypeOf(StationType t)
        {
            foreach (var row in Table) if (row.Type == t) return GameTypes.Find(row.GameType);
            return null;
        }

        /// <summary>What kind of station <paramref name="obj"/> is; None if it is not one.</summary>
        internal static StationType TypeOf(object obj)
        {
            if (obj is null) return StationType.None;
            foreach (var row in Table)
            {
                var t = GameTypes.Find(row.GameType);
                if (t != null && Cast.Is(obj, t)) return row.Type;
            }
            return StationType.None;
        }

        /// <summary>
        /// Every placed station of one type. Searches the scene: call it on a timer or a
        /// change, not per frame.
        /// </summary>
        public static List<Buildable> All(StationType type)
        {
            var list = new List<Buildable>();
            foreach (var o in UnityFind.All(GameTypeOf(type))) list.Add(new Buildable(o));
            return list;
        }
    }

    /// <summary>
    /// Something the player placed: a station, a shelf, a bed, a light. Identified by a
    /// GUID that survives save and load, which makes it the key for per-object settings.
    /// </summary>
    public readonly struct Buildable : IEquatable<Buildable>
    {
        /// <summary>The game object behind this handle.</summary>
        public object Native { get; }

        internal Buildable(object native) => Native = native;

        /// <summary>False for an empty handle or a destroyed object.</summary>
        public bool Exists => UnityFind.Alive(Native);

        /// <summary>Identity for this session. Use <see cref="Guid"/> to persist.</summary>
        public ObjectKey Key => ObjectKey.Of(Native);

        /// <summary>The persistent ID, as text.</summary>
        public string Guid => Seq.Text(WorldTypes.Guid.Get(Native));

        /// <summary>The Unity component, for transforms and the like.</summary>
        public Component Component => UnityFind.AsComponent(Native);

        /// <summary>The real class name ("MixingStationMk2").</summary>
        public string TypeName => Cast.TypeName(Native);

        /// <summary>The name the player sees ("Mixing Station Mk II").</summary>
        public string Name
        {
            get
            {
                var instance = WorldTypes.Instance.Get(Native);
                var name = instance is null ? null : MDSAPI.Items.Slots.InstanceName.Get(instance);
                if (!string.IsNullOrEmpty(name)) return name;
                var c = Component;
                return c == null ? "" : c.gameObject.name.Replace("(Clone)", "").Trim();
            }
        }

        /// <summary>Which station this is; None for anything else.</summary>
        public StationType Station => Stations.TypeOf(Native);

        /// <summary>
        /// The station's own capacity number: items a drying rack dries at once, or the most a
        /// mixing station mixes in one go. Null for anything else. Writing sets it on this one
        /// object until the game resets it (a reload).
        /// </summary>
        public int? StationCapacity
        {
            get => Station switch
            {
                StationType.DryingRack => Stations.RackCapacity.Get(Native),
                StationType.MixingStation => Stations.MixerMax.Get(Native),
                _ => (int?)null,
            };
            set
            {
                if (value is null) return;
                switch (Station)
                {
                    case StationType.DryingRack: Stations.RackCapacity.Set(Native, value.Value); break;
                    case StationType.MixingStation: Stations.MixerMax.Set(Native, value.Value); break;
                }
            }
        }

        /// <summary>The storage on this object (a shelf's, a trunk's), if it has one.</summary>
        public Storage Storage => new(UnityFind.InChildren(Native, WorldTypes.StorageType));

        /// <summary>True when this is (or holds) a bed.</summary>
        public bool IsBed => UnityFind.InChildren(Native, WorldTypes.BedType) != null;

        /// <summary>
        /// True when this is a bed with an employee assigned. An employee's bed has a case for
        /// their pay; a bed without one holds nothing.
        /// </summary>
        public bool IsEmployeeBed
        {
            get
            {
                var bed = UnityFind.InChildren(Native, WorldTypes.BedType);
                return bed != null && WorldTypes.BedEmployee.Get(bed) != null;
            }
        }

        /// <inheritdoc/>
        public bool Equals(Buildable other) => Key == other.Key;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Buildable b && Equals(b);
        /// <inheritdoc/>
        public override int GetHashCode() => Key.GetHashCode();
    }

    internal static class WorldTypes
    {
        internal const string BuildableTypeName = "EntityFramework.BuildableItem";
        internal static readonly Access<object> Guid = new(BuildableTypeName, "GUID");
        internal static readonly Access<object> Instance = new(BuildableTypeName, "ItemInstance");
        internal static readonly Access<object> BedEmployee = new("ObjectScripts.Bed", "AssignedEmployee");

        private static Type _buildable, _storage, _bed, _land, _delivery, _npcInv, _dealer;
        internal static Type BuildableType => _buildable ??= GameTypes.Find(BuildableTypeName);
        internal static Type StorageType => _storage ??= GameTypes.Find("Storage.StorageEntity");
        internal static Type BedType => _bed ??= GameTypes.Find("ObjectScripts.Bed");
        internal static Type LandVehicleType => _land ??= GameTypes.Find("Vehicles.LandVehicle");
        internal static Type DeliveryVehicleType => _delivery ??= GameTypes.Find("Delivery.DeliveryVehicle");
        internal static Type NpcInventoryType => _npcInv ??= GameTypes.Find("NPCs.NPCInventory");
        internal static Type DealerType => _dealer ??= GameTypes.Find("Economy.Dealer");
    }
}
