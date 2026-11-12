using System;
using System.IO;
using System.Reflection;
using MelonLoader;
using MDSAPI.Internal.Abstraction;
using MDSAPI.Internal.Interop;
using MDSAPI.Lifecycle;
using MDSAPI.Logging;

[assembly: MelonInfo(typeof(MDSAPI.MdsApi), "MDSAPI", "0.2.1", "BlessingMds")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MDSAPI
{
    /// <summary>
    /// MDSAPI's own entry point. It owns the lifecycle hooks and the save plumbing that
    /// every mod built on it depends on, and does nothing game-specific itself.
    /// </summary>
    public sealed class MdsApi : MelonMod
    {
        /// <summary>The MDSAPI version mods can check against.</summary>
        public static readonly Version ApiVersion = new(0, 2, 1);

        /// <summary>The game version this build was developed and tested against.</summary>
        public const string TargetedGameVersion = "0.4.7f12";

        private string _saveRoot;

        public override void OnInitializeMelon()
        {
            Log.Bind(LoggerInstance);

            // Report the game we are actually running on, not the one we assumed. When a
            // player reports a break, this line is the first thing worth seeing.
            var running = SafeGameVersion();
            Log.Info("MDSAPI " + ApiVersion + " — built against Schedule I " +
                     TargetedGameVersion + ", running on " + running + ".");

            // The Mono branch reports its version with a suffix ("0.4.7f12 Alternate"), so
            // comparing the whole string flags a mismatch on a game that matches perfectly.
            // Compare the version token only.
            var runningVersion = running.Split(' ')[0];
            if (runningVersion != "unknown" && runningVersion != TargetedGameVersion)
                Log.Warn("Game version differs from the one MDSAPI was built against (" +
                         TargetedGameVersion + "). Things may have moved; report anything " +
                         "odd with this line.");

            _settings = MDSAPI.Config.ModConfig.Load<MDSAPI.Updates.MdsApiSettings>();
            MDSAPI.Updates.UpdateCheck.Register("MDSAPI", ApiVersion.ToString(3), MDSAPI.Updates.UpdateCheck.MdsApiRepo);

            GameLifecycle.OnPreLoad += OnPreLoad;
            GameLifecycle.OnLoadComplete += OnLoadComplete;
            GameLifecycle.OnSaveStart += OnSaveStart;

            RegisterModules();
        }

        private MDSAPI.Updates.MdsApiSettings _settings;
        private static bool _inWorld;

        public override void OnLateInitializeMelon()
        {
            GameLifecycle.Bind();
            // Mods have registered by now; checks run in the background from here.
            MDSAPI.Updates.UpdateNotice.Settings = _settings;
            if (_settings != null && _settings.PreviewUpdateNotice)
                MDSAPI.Updates.UpdateNotice.AddUpdate("Example Mod (preview)", "9.9.9", "1.0.0", MDSAPI.Updates.UpdateCheck.MdsApiUrl);
            MDSAPI.Updates.UpdateCheck.Start(_settings?.CheckForUpdates ?? true);
        }

        public override void OnUpdate()
        {
            MDSAPI.Updates.UpdateCheck.Drain();
            MDSAPI.Updates.UpdateNotice.Tick(_inWorld);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName) => _inWorld = sceneName == "Main";

        /// <summary>
        /// Call first thing in a mod's OnInitializeMelon. If the installed MDSAPI is older than
        /// <paramref name="minimumVersion"/>, logs an error naming the mod and the version it
        /// needs, puts the same line on the main menu, and returns false — the mod should then
        /// switch itself off rather than fail halfway through with a missing-method error.
        /// </summary>
        public static bool Requires(string minimumVersion, string modName)
        {
            var need = MDSAPI.Updates.UpdateCheck.ParseVersion(minimumVersion);
            if (need is null || ApiVersion >= need) return true;
            var text = modName + " needs MDSAPI " + need.ToString(3) + " or newer — you have " + ApiVersion.ToString(3);
            Log.Error(text + ". " + modName + " is off until MDSAPI is updated: " + MDSAPI.Updates.UpdateCheck.MdsApiUrl);
            MDSAPI.Updates.UpdateNotice.AddRequirement(modName, need.ToString(3), ApiVersion.ToString(3), MDSAPI.Updates.UpdateCheck.MdsApiUrl);
            return false;
        }

        /// <summary>
        /// Every module MDSAPI ships, registered up front.
        /// <para>
        /// A module registers in its static constructor, which runs only when something
        /// first touches it. Left to that, the startup report would list only the modules
        /// some mod happened to use before the report ran — and would say nothing about one
        /// that broke but nobody had touched yet, which is the case it exists for.
        /// </para>
        /// <para>Add each new module here.</para>
        /// </summary>
        private static void RegisterModules()
        {
            foreach (var module in new[]
            {
                typeof(MDSAPI.Money.Money),
                typeof(MDSAPI.Items.Items),
                typeof(MDSAPI.Items.ItemDrag),
                typeof(MDSAPI.Items.Products),
                typeof(MDSAPI.World.Stations),
                typeof(MDSAPI.World.Interaction),
                typeof(MDSAPI.Players.PlayerControls),
                typeof(MDSAPI.Players.Controls),
                typeof(MDSAPI.Multiplayer.Lobby),
                typeof(MDSAPI.UI.Phone),
                typeof(MDSAPI.UI.InteractionPrompt),
                typeof(MDSAPI.UI.HudQuest),
            })
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(module.TypeHandle);
            }
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // Per-scene singletons die with the scene. Holding one across a load is the
            // bug that presents as "works until I load a save". The event bindings go the
            // same way, or destroyed UnityEvents stay alive for the rest of the session.
            Singletons.Clear();
            EventBridge.Clear();
            Modules.Forget();
            MDSAPI.Players.PlayerControls.Reset();
        }

        private void OnPreLoad()
        {
            Singletons.Clear();
            Saveable.DiscoverAndRegister();
        }

        private void OnLoadComplete()
        {
            _saveRoot = ResolveSaveRoot();
            if (_saveRoot is null)
            {
                Log.Error("Could not find the loaded save folder, so mod data will not persist " +
                          "this session. Please report with your game version.");
                return;
            }
            var count = Saveable.LoadAll(_saveRoot);

            // One line per save load, not per frame. Without it a working session and one
            // where OnLoadComplete never fired look identical in the log, which is the
            // first thing anyone needs to tell apart in a bug report.
            Modules.Report();
            MDSAPI.Patching.Patch.Report();
            Log.Info("MDSAPI ready — " + count + " saveable(s) registered.");
        }

        private void OnSaveStart()
        {
            if (_saveRoot != null) Saveable.SaveAll(_saveRoot);
        }

        public override void OnDeinitializeMelon()
        {
            GameLifecycle.OnPreLoad -= OnPreLoad;
            GameLifecycle.OnLoadComplete -= OnLoadComplete;
            GameLifecycle.OnSaveStart -= OnSaveStart;
            Saveable.UnregisterAll();
        }

        /// <summary>
        /// The folder of the save currently loaded. Mod data lives beside the game's own,
        /// so a player who copies or deletes a save takes the mod data with it.
        /// </summary>
        private static string ResolveSaveRoot()
        {
            var load = Singletons.Resolve("Persistence.LoadManager");
            if (load is null) return null;
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var path = load.GetType().GetProperty("LoadedGameFolderPath", F)?.GetValue(load) as string
                    ?? load.GetType().GetField("LoadedGameFolderPath", F)?.GetValue(load) as string;
            return string.IsNullOrEmpty(path) || !Directory.Exists(path) ? null : path;
        }

        private static string SafeGameVersion()
        {
            // MelonUtils.GameVersion is obsolete-as-error in ML 0.7.x and points at
            // InternalUtils.UnityInformationHandler — which is not public API, as the
            // namespace says. Compiling against an internal type that MelonLoader is free
            // to move is a bad trade for one log line, so read it reflectively and fall
            // back. A missing version string is cosmetic; a build that breaks on the next
            // MelonLoader release is not.
            try
            {
                var type = Type.GetType("MelonLoader.InternalUtils.UnityInformationHandler, MelonLoader");
                var value = type?.GetProperty("GameVersion",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null) as string;
                return string.IsNullOrEmpty(value) ? "unknown" : value;
            }
            catch { return "unknown"; }
        }
    }
}
