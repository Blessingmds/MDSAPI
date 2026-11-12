using System;
using System.Collections.Generic;
using MDSAPI.Internal.Interop;

namespace MDSAPI.Items
{
    /// <summary>
    /// Decides how many of an item a slot takes. Receives what the game (and any filter
    /// before this one) decided; returns the new number. Runs very often — every inventory
    /// check by the player, workers and NPCs — so keep it allocation-free and cache anything
    /// you look up.
    /// </summary>
    public delegate int CapacityFilter(ItemSlot slot, ItemInstance item, int capacity);

    /// <summary>Items: definitions, stack limits, and the hooks around slot capacity.</summary>
    public static class Items
    {
        static Items() => Modules.Register("Items", () =>
            Defs.StackLimit.IsAvailable && Defs.Id.IsAvailable && Slots.Quantity.IsAvailable &&
            Slots.Owner.IsAvailable && Slots.Capacity.IsAvailable && Slots.Definition.IsAvailable);

        /// <summary>True when the item members MDSAPI needs were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Items");

        /// <summary>
        /// Every item definition currently loaded, including runtime ones such as mixes.
        /// Scans the whole asset database: call it when opening a menu, not per frame.
        /// </summary>
        public static List<ItemDefinition> AllDefinitions()
        {
            var list = new List<ItemDefinition>();
            var type = Defs.DefinitionType;
            // Cast once here, so every later read through these handles is a direct one.
            foreach (var o in UnityFind.Everything(type))
                list.Add(new ItemDefinition(Cast.To(o, type) ?? o));
            return list;
        }

        // ---- capacity -------------------------------------------------------------------

        private static CapacityFilter[] _filters = Array.Empty<CapacityFilter>();
        private static bool _capacityHooked;

        /// <summary>
        /// Adds a filter on how many of an item a slot takes. The game asks this whenever
        /// anything moves into a slot, so a filter here is enforced everywhere — the player
        /// dragging, workers restocking, NPC inventories. Returns false if the hook could not
        /// be installed on this game version.
        /// </summary>
        public static bool AddCapacityFilter(CapacityFilter filter)
        {
            if (filter is null) return false;
            if (!_capacityHooked)
            {
                _capacityHooked = true;
                CapacityHookOk = Hooks.Install("ItemSlot.GetCapacityForItem", Slots.Capacity.Method,
                                               typeof(Items), nameof(CapacityPostfix), Hooks.Kind.Postfix);
            }
            var next = new CapacityFilter[_filters.Length + 1];
            _filters.CopyTo(next, 0);
            next[_filters.Length] = filter;
            _filters = next; // swapped whole, so a postfix mid-iteration never sees a half-built array
            return CapacityHookOk;
        }

        private static bool CapacityHookOk;

        private static void CapacityPostfix(object __instance, object __0, ref int __result)
        {
            var filters = _filters;
            if (filters.Length == 0 || __0 is null) return;
            var slot = new ItemSlot(__instance);
            var item = new ItemInstance(__0);
            for (var i = 0; i < filters.Length; i++)
            {
                try { __result = filters[i](slot, item, __result); }
                catch (Exception ex) { Failures.Once("a capacity filter from " + filters[i].Method.DeclaringType?.Assembly.GetName().Name, ex); }
            }
        }

        // ---- registration -----------------------------------------------------------------

        private static Action<ItemDefinition> _registered;
        private static bool _registryHooked;

        /// <summary>
        /// Raised when the game registers an item definition after startup — every mixed
        /// product, on load and while playing, on host and clients. The place to apply
        /// per-item settings to items that did not exist when you last scanned.
        /// </summary>
        public static event Action<ItemDefinition> DefinitionRegistered
        {
            add
            {
                if (!_registryHooked)
                {
                    _registryHooked = true;
                    Hooks.Install("Registry.AddToRegistry", Registry.Method, typeof(Items),
                                  nameof(RegistryPostfix), Hooks.Kind.Postfix);
                }
                _registered += value;
            }
            remove => _registered -= value;
        }

        private static readonly GameMethod Registry = new("Registry", null, "AddToRegistry");

        private static void RegistryPostfix(object __0)
        {
            var handlers = _registered;
            if (handlers is null || __0 is null) return;
            var def = new ItemDefinition(__0);
            foreach (Action<ItemDefinition> h in handlers.GetInvocationList())
            {
                try { h(def); }
                catch (Exception ex) { Failures.Once("a DefinitionRegistered handler from " + h.Method.DeclaringType?.Assembly.GetName().Name, ex); }
            }
        }
    }
}
