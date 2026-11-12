using System;
using MDSAPI.Internal.Interop;

namespace MDSAPI.Items
{
    /// <summary>
    /// The player dropping a dragged stack onto a slot, just before the game moves anything.
    /// One instance is reused for every drop: read it inside the handler, do not keep it.
    /// </summary>
    public sealed class ItemDrop
    {
        private object _manager;

        internal ItemDrop() { }

        internal void Reset(object manager, ItemSlot from, ItemSlot to, bool isCash)
        {
            _manager = manager; From = from; To = to; IsCash = isCash;
        }

        /// <summary>The slot the stack came from.</summary>
        public ItemSlot From { get; private set; }

        /// <summary>The slot under the cursor; may not exist (dropped on nothing).</summary>
        public ItemSlot To { get; private set; }

        /// <summary>True when the player is dragging cash rather than items.</summary>
        public bool IsCash { get; private set; }

        /// <summary>How many will be moved. Lower it to move fewer.</summary>
        public int Amount
        {
            get => ItemDrag.DraggedAmount.Get(_manager);
            set => ItemDrag.DraggedAmount.Set(_manager, value);
        }
    }

    /// <summary>The player's drag and drop between slots.</summary>
    public static class ItemDrag
    {
        private const string Manager = "UI.Items.ItemUIManager";
        internal static readonly Access<int> DraggedAmount = new(Manager, "draggedAmount");
        private static readonly Access<object> DraggedSlot = new(Manager, "draggedSlot");
        private static readonly Access<object> HoveredSlot = new(Manager, "HoveredSlot");
        private static readonly Access<bool> DraggingCash = new(Manager, "isDraggingCash");
        private static readonly Access<object> AssignedSlot = new("UI.ItemSlotUI", "assignedSlot");
        private static readonly GameMethod EndDrag = new(Manager, null, "EndDrag");

        static ItemDrag() => Modules.Register("ItemDrag", () =>
            DraggedAmount.IsAvailable && DraggedSlot.IsAvailable && HoveredSlot.IsAvailable &&
            AssignedSlot.IsAvailable && EndDrag.IsAvailable);

        private static readonly ItemDrop Drop = new();
        private static Action<ItemDrop> _beforeDrop;
        private static bool _hooked;

        /// <summary>
        /// Raised when the player releases a dragged stack, before the game moves it. Lower
        /// <see cref="ItemDrop.Amount"/> to cap what moves. The game itself adds items one at a
        /// time up to the item's stack limit when dropping onto a matching stack, without
        /// asking the target slot — so per-slot limits need this as well as a capacity filter.
        /// </summary>
        public static event Action<ItemDrop> BeforeDrop
        {
            add
            {
                if (!_hooked)
                {
                    _hooked = true;
                    Hooks.Install("ItemUIManager.EndDrag", EndDrag.Method, typeof(ItemDrag),
                                  nameof(EndDragPrefix), Hooks.Kind.Prefix);
                }
                _beforeDrop += value;
            }
            remove => _beforeDrop -= value;
        }

        private static void EndDragPrefix(object __instance)
        {
            var handlers = _beforeDrop;
            if (handlers is null) return;
            try
            {
                var from = new ItemSlot(AssignedSlot.Get(DraggedSlot.Get(__instance)));
                var to = new ItemSlot(AssignedSlot.Get(HoveredSlot.Get(__instance)));
                Drop.Reset(__instance, from, to, DraggingCash.Get(__instance));
            }
            catch (Exception ex) { Failures.Once("ItemDrag.BeforeDrop (reading the drag)", ex); return; }

            foreach (Action<ItemDrop> h in handlers.GetInvocationList())
            {
                try { h(Drop); }
                catch (Exception ex) { Failures.Once("a BeforeDrop handler from " + h.Method.DeclaringType?.Assembly.GetName().Name, ex); }
            }
        }
    }
}
