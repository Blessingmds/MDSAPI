using System;
using System.Reflection;
using MDSAPI.Internal.Interop;
using UnityEngine;

namespace MDSAPI.UI
{
    /// <summary>
    /// The game's "[E] Use ..." interaction prompt. A mod hint that copies it looks native
    /// instead of like a debug label.
    /// </summary>
    public static class InteractionPrompt
    {
        private const string Type = "UI.InteractionCanvas";
        private static readonly Access<object> ContainerAccess = new(Type, "Container");
        private static readonly Access<object> Message = new(Type, "MessageText");
        private static readonly Access<object> Key = new(Type, "IconText");
        private static readonly Access<Color> MessageColor = new(Type, "DefaultMessageColor");

        static InteractionPrompt() => Modules.Register("InteractionPrompt", () =>
            ContainerAccess.IsAvailable && Message.IsAvailable && Key.IsAvailable && MessageColor.IsAvailable);

        /// <summary>True when these members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("InteractionPrompt");

        private static object Canvas => Singletons.TryResolve(Type) ?? UnityFind.First(GameTypes.Find(Type));

        /// <summary>True once the HUD (and so the prompt) exists.</summary>
        public static bool Exists => Container != null;

        /// <summary>The prompt's root: one line, key box and text. Null before the HUD exists.</summary>
        public static RectTransform Container => Rect(ContainerAccess);

        /// <summary>The prompt's message text ("Use Packaging Station").</summary>
        public static Transform MessageText => Rect(Message);

        /// <summary>The text inside the key box ("E").</summary>
        public static Transform KeyText => Rect(Key);

        /// <summary>The colour the game draws the message in.</summary>
        public static Color DefaultMessageColor
        {
            get
            {
                var c = Canvas;
                return c is null ? Color.white : MessageColor.Get(c, Color.white);
            }
        }

        private static RectTransform Rect(Access<object> member)
        {
            var c = Canvas;
            var o = c is null ? null : member.Get(c);
            if (o is null) return null;
            if (o is RectTransform rt) return rt;
            var comp = UnityFind.AsComponent(o);
            return comp == null ? null : comp.GetComponent<RectTransform>();
        }
    }

    /// <summary>
    /// A quest in the game's own quest list on the HUD — same font, animation and tick as
    /// the game's quests — for tutorials and objectives.
    /// </summary>
    public sealed class HudQuest
    {
        private const string Journal = "UI.Phone.JournalApp";
        private static readonly Access<object> QuestPrefab = new(Journal, "QuestHUDUIPrefab");
        private static readonly Access<object> EntryPrefab = new(Journal, "QuestEntryHUDUIPrefab");
        private static readonly Access<object> HudContainer = new("UI.HUD", "QuestEntryContainer");
        private static readonly Access<object> QuestLabel = new("UI.QuestHUDUI", "MainLabel");
        private static readonly Access<object> QuestEntries = new("UI.QuestHUDUI", "EntryContainer");
        private static readonly Access<object> EntryLabel = new("UI.QuestEntryHUDUI", "MainLabel");
        private static readonly GameMethod EntryComplete = new("UI.QuestEntryHUDUI", null, "Complete");

        static HudQuest() => Modules.Register("HudQuest", () =>
            QuestPrefab.IsAvailable && EntryPrefab.IsAvailable && HudContainer.IsAvailable &&
            QuestLabel.IsAvailable && QuestEntries.IsAvailable && EntryLabel.IsAvailable && EntryComplete.IsAvailable);

        private GameObject _root;
        private object _entry;

        /// <summary>The quest's root object on the HUD, for anything not covered here.</summary>
        public GameObject Root => _root;

        private HudQuest() { }

        /// <summary>
        /// Adds a quest titled <paramref name="title"/> with one objective line, at the top of
        /// the quest list. Null if the HUD is not up or the game's quest UI has changed.
        /// </summary>
        public static HudQuest Show(string title, string objective)
        {
            object journal = null;
            foreach (var j in UnityFind.Everything(GameTypes.Find(Journal)))
            {
                var c = UnityFind.AsComponent(j);
                if (c != null && c.gameObject.scene.IsValid()) { journal = j; break; }
            }
            var hud = Singletons.TryResolve("UI.HUD") ?? UnityFind.First(GameTypes.Find("UI.HUD"));
            var questPrefab = UnityFind.AsComponent(journal is null ? null : QuestPrefab.Get(journal));
            var entryPrefab = UnityFind.AsComponent(journal is null ? null : EntryPrefab.Get(journal));
            var container = AsTransform(hud is null ? null : HudContainer.Get(hud));
            if (questPrefab == null || entryPrefab == null || container == null) return null;

            var q = new HudQuest();
            try
            {
                q._root = UnityEngine.Object.Instantiate(questPrefab.gameObject, container);
                var quest = UnityFind.On(q._root.transform, GameTypes.Find("UI.QuestHUDUI"));
                SetText(QuestLabel.Get(quest), title);
                var entries = AsTransform(QuestEntries.Get(quest));
                var entry = UnityEngine.Object.Instantiate(entryPrefab.gameObject, entries);
                q._entry = UnityFind.On(entry.transform, GameTypes.Find("UI.QuestEntryHUDUI"));
                SetText(EntryLabel.Get(q._entry), objective);
                q._root.transform.SetAsFirstSibling();
                return q;
            }
            catch (Exception ex)
            {
                Failures.Once("HudQuest.Show", ex);
                q.Remove();
                return null;
            }
        }

        /// <summary>Ticks the objective off with the game's animation, then removes the quest.</summary>
        public void Complete(float removeAfterSeconds = 2.5f)
        {
            if (_root == null) return;
            if (!EntryComplete.TryInvoke(_entry))
                SetColor(EntryLabel.Get(_entry), new Color(0.56f, 0.85f, 0.56f));
            UnityEngine.Object.Destroy(_root, removeAfterSeconds);
            _root = null;
            _entry = null;
        }

        /// <summary>Removes the quest immediately.</summary>
        public void Remove()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null;
            _entry = null;
        }

        private static Transform AsTransform(object o) =>
            o is null ? null : o as Transform ?? UnityFind.AsComponent(o)?.transform;

        // The labels are TextMeshPro objects. Set through reflection so MDSAPI does not take a
        // compile-time dependency on TextMeshPro for one property.
        private static void SetText(object label, string text) => SetProperty(label, "text", text);
        private static void SetColor(object label, Color color) => SetProperty(label, "color", color);

        private static void SetProperty(object target, string name, object value)
        {
            if (target is null) return;
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            p?.SetValue(target, value);
        }
    }
}
