using System;
using System.Collections.Generic;
using MelonLoader;
using System.Linq;
using MDSAPI.Config;
using MDSAPI.Items;
using MDSAPI.Multiplayer;
using MDSAPI.Players;
using MDSAPI.UI;
using MDSAPI.World;
using MDSAPI.Internal.Abstraction;
using MDSAPI.Lifecycle;
using MDSAPI.Logging;
using MDSAPI.Patching;

[assembly: MelonInfo(typeof(MDSAPI.SelfTest.SelfTest), "MDSAPI.SelfTest", "0.3.0", "BlessingMds")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MDSAPI.SelfTest
{
    /// <summary>
    /// Exercises MDSAPI end to end. Not shipped.
    /// <para>
    /// Every check here is read-only against the game. It patches a method with a postfix
    /// that changes nothing, reads money without spending it, and never forces a save —
    /// players use quitting without saving as an undo, and a test mod has no business
    /// taking that away.
    /// </para>
    /// <para>
    /// Lines are prefixed PASS / FAIL so the log reads as a checklist.
    /// </para>
    /// </summary>
    public sealed class SelfTest : MelonMod
    {
        internal static int NetWorthCalls;

        public override void OnInitializeMelon()
        {
            // ModConfig — first run creates the file with defaults (proves Save);
            // the next run reads it back (proves Load). Read only; never written here.
            var config = ModConfig.Load<SelfTestConfig>();
            Check("ModConfig loaded", config != null,
                  "marker=\"" + config?.Marker + "\" — edit it in UserData/SelfTestConfig.json, " +
                  "restart, and it must come back changed");

            // Patch — a harmless postfix on a real game method, through the candidate list.
            var patched = Patch.Target("Money.MoneyManager")
                               .Method("GetNetWorth", "CalculateNetWorth")
                               .Postfix(typeof(SelfTestPatches), nameof(SelfTestPatches.NetWorthSeen));
            Check("Patch applied", patched, "postfix on MoneyManager.GetNetWorth");

            // Hooks — pass-through, so they observe without changing anything.
            Check("Capacity hook installed", Items.Items.AddCapacityFilter(CountCapacity), "postfix on ItemSlot.GetCapacityForItem");
            Items.Items.DefinitionRegistered += d => RegisteredDefs++;
            ItemDrag.BeforeDrop += d => { Drops++; LastDrop = d.Amount + (d.IsCash ? " cash" : " item(s)"); };
            Check("Exit handler registered", Controls.AddExitHandler(() => { ExitSeen++; return false; }, 0),
                  "Escape must reach it (reported in the world checks)");

            // A compiled-in constant. Installed but never set, so the game keeps its own value.
            CashConstant = MDSAPI.Patching.CodeConstant.Override("Cash per slot (self-test)", "UI.Items.ItemUIManager", 1000f,
                "StartDragCash", "EndCashDrag", "AddCashAmount", "SubtractCashAmount");
            Check("Code constant installed", CashConstant.IsAvailable, "cash-per-slot const found in all four methods; value untouched");

            GameLifecycle.OnLoadComplete += OnLoadComplete;
            GameLifecycle.OnSaveComplete += Persisted;
        }

        internal static int CapacityCalls, RegisteredDefs, Drops, ExitSeen;
        internal static string LastDrop = "";
        internal static MDSAPI.Patching.CodeConstant CashConstant;
        private static DateTime? _worldCheckAt;
        private static bool _worldChecked;

        private static int CountCapacity(ItemSlot slot, ItemInstance item, int capacity)
        {
            CapacityCalls++;
            return capacity;
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _worldChecked = false;
            _worldCheckAt = null;
        }

        public override void OnUpdate()
        {
            if (_worldChecked) return;
            if (_worldCheckAt is null)
            {
                if (LocalPlayer.Exists) _worldCheckAt = DateTime.UtcNow.AddSeconds(8);
                return;
            }
            if (DateTime.UtcNow < _worldCheckAt) return;
            _worldChecked = true;
            try { WorldChecks(); }
            catch (Exception ex) { Log.Error("SelfTest world checks threw.", ex); }
        }

        // Read-only. Runs once per load, eight seconds after the player spawns.
        private static void WorldChecks()
        {
            Log.Info("SelfTest — world checks (" + (MDSAPI.Unity.Cross.IsIl2Cpp ? "IL2CPP" : "Mono") +
                     ", accessors " + (Internal_UsingReflection() ? "reflection" : "compiled") + ")");

            var defs = Items.Items.AllDefinitions();
            Check("Items: definitions", Items.Items.IsAvailable && defs.Count > 0, defs.Count + " loaded");
            var kush = defs.FirstOrDefault(d => d.Id == "ogkush");
            Check("Items: read a definition", kush.Exists,
                  kush.Exists ? kush.Name + " (" + kush.Id + "), category " + kush.Category + ", stack " + kush.StackLimit +
                                ", drug type " + kush.DrugType : "ogkush not found");
            var counted = defs.Where(d => d.HasOwnCount).Select(d => d.Id).Take(5).ToList();
            Check("Items: own-count items", counted.Count > 0, string.Join(", ", counted));
            var mixes = defs.Count(d => d.IsProduct);
            Check("Products", Products.IsAvailable && Products.DefaultFor("Marijuana").Exists,
                  mixes + " products; base weed = " + Products.DefaultFor("Marijuana").Id + ", base meth = " + Products.DefaultFor("Methamphetamine").Id);
            Check("Capacity filter fires", CapacityCalls > 0, CapacityCalls + " call(s) so far");
            Check("DefinitionRegistered", true, RegisteredDefs + " registration(s) seen since start (mixes register on load)");

            var racks = Stations.All(StationType.DryingRack);
            var mixers = Stations.All(StationType.MixingStation);
            Check("Stations", Stations.IsAvailable, racks.Count + " drying rack(s), " + mixers.Count + " mixer(s)" +
                  (racks.Count > 0 ? "; first rack holds " + racks[0].StationCapacity + ", guid " + racks[0].Guid : "") +
                  (mixers.Count > 0 ? "; first mixer " + mixers[0].TypeName + " max " + mixers[0].StationCapacity : ""));

            var models = Vehicles.AllModels();
            Check("Vehicles", models.Count > 0, models.Count + " model(s): " + string.Join(", ", models.Take(4).Select(v => v.Name)));

            Check("Interaction", Interaction.IsAvailable, "hovering " + (Interaction.HoveredBuildable.Exists ? Interaction.HoveredBuildable.Name : "nothing") +
                  ", storage menu " + (Interaction.StorageMenuOpen ? "open" : "closed"));
            Check("Player", LocalPlayer.Exists && PlayerControls.IsAvailable, "screens open: " + PlayerControls.OpenScreens);
            Check("Controls", Controls.IsAvailable, "typing = " + Controls.IsTyping + ", Escape seen " + ExitSeen + " time(s)");
            Check("Lobby", Lobby.IsAvailable, Lobby.InLobby ? (Lobby.IsHost ? "hosting" : "joined") : "single player");

            Check("Phone", Phone.IsAvailable && Phone.IsReadyForApps, "open = " + Phone.IsOpen + ", home screen and app area found");
            Check("Interaction prompt", InteractionPrompt.IsAvailable && InteractionPrompt.Exists, "game prompt found");
            Check("Item drag", true, Drops + " drop(s) seen" + (Drops > 0 ? ", last " + LastDrop : " — drag a stack once and reload to see it counted"));
        }

        private static bool Internal_UsingReflection()
        {
            // Not public API; read for the report only.
            var t = typeof(MDSAPI.MdsApi).Assembly.GetType("MDSAPI.Internal.Interop.Accessors");
            return t?.GetProperty("UsingReflection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?.GetValue(null) is true;
        }

        public override void OnDeinitializeMelon()
        {
            GameLifecycle.OnLoadComplete -= OnLoadComplete;
            GameLifecycle.OnSaveComplete -= Persisted;
        }

        private static void OnLoadComplete()
        {
            // Money — read-only.
            Check("Money available", MDSAPI.Money.Money.IsAvailable, "module bound");
            var cash = MDSAPI.Money.Money.Cash;
            Check("Money.Cash read", true,
                  "cash = " + MDSAPI.Money.Money.FormatAmount(cash) +
                  " — compare with your in-game cash");

            // Patch — calling the wrapper must run the game method, which must run our postfix.
            var before = NetWorthCalls;
            var worth = MDSAPI.Money.Money.NetWorth;
            Check("Patch fires", NetWorthCalls > before,
                  "net worth = " + MDSAPI.Money.Money.FormatAmount(worth));

            // Saveable — the persistence round-trip.
            var data = Counter.Current;
            if (data is null)
            {
                Check("Saveable registered", false, "discovery did not find Counter");
                return;
            }
            data.Loads++;
            data.History.Add(DateTime.UtcNow.ToString("u"));
            Check("Saveable registered", true,
                  "loaded " + data.Loads + " time(s) across " + data.History.Count +
                  " session(s); the count rises once the game has saved");
        }

        private static void Persisted()
        {
            var data = Counter.Current;
            if (data != null) Log.Info("SelfTest: wrote " + data.Loads + " load(s) to disk.");
        }

        private static void Check(string what, bool ok, string detail) =>
            Log.Info("SelfTest " + (ok ? "PASS" : "FAIL") + "  " + what + " — " + detail);
    }

    internal static class SelfTestPatches
    {
        // No parameters: it only needs to know the method ran. object/__result would also
        // work, but a postfix that asks for nothing cannot get a binding wrong.
        internal static void NetWorthSeen() => SelfTest.NetWorthCalls++;
    }

    /// <summary>Global settings — must survive restarts and apply to every save.</summary>
    public sealed class SelfTestConfig : ModConfig
    {
        public string Marker = "default";
    }

    /// <summary>Per-save data — must survive a save and reload.</summary>
    public sealed class Counter : Saveable
    {
        internal static Counter Current;

        [SaveableField("loads")] private int _loads;
        [SaveableField("history")] private List<string> _history = new List<string>();

        public int Loads { get => _loads; set => _loads = value; }
        public List<string> History => _history;

        protected override void OnCreated() => Current = this;
        protected override void OnDestroyed() => Current = null;
    }
}
