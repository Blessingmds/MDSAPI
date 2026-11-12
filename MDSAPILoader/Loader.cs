using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(MDSAPILoader.Loader), "MDSAPILoader", "0.2.0", "BlessingMds")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MDSAPILoader
{
    /// <summary>
    /// Loads the build that matches the running game: MDSAPI first, then every mod that
    /// ships both runtimes side by side.
    /// <para>
    /// This is a MelonLoader <i>plugin</i>, not a mod, because plugins load before the
    /// game's assemblies. Mods ship their IL2CPP and Mono builds together and this picks
    /// one at every launch, so a player downloads one file, never has to know which Steam
    /// branch they are on, and can switch branches without reinstalling anything.
    /// </para>
    /// <code>
    ///   Mods/MDSAPI/Il2Cpp/MDSAPI.dll                     the API
    ///   Mods/MDSAPI/Mono/MDSAPI.dll
    ///   Mods/StackLimitPlus/Il2Cpp/SCH1_StackLimitPlus.dll   any mod, same shape
    ///   Mods/StackLimitPlus/Mono/SCH1_StackLimitPlus.dll
    /// </code>
    /// </summary>
    public sealed class Loader : MelonPlugin
    {
        private const string ApiFolder = "MDSAPI";

        public override void OnPreModsLoaded()
        {
            // Ask MelonLoader rather than sniffing the filesystem. IsMonoRuntime is
            // correct before any game assembly has loaded; checking for an
            // Il2CppAssemblies folder is a guess that a reinstall can make wrong.
            var runtime = MelonEnvironment.IsMonoRuntime ? "Mono" : "Il2Cpp";
            var mods = MelonEnvironment.ModsDirectory;

            // OnPreModsLoaded, not OnPreInitialization: registration that early is
            // dropped, because MelonLoader is not ready for it yet. This callback fires
            // immediately before the mod phase. MelonLoader does not recurse into Mods
            // subfolders, so nothing below is ever scanned by it as well.
            var apiRoot = Path.Combine(mods, ApiFolder);
            var api = NewestApi(apiRoot, runtime);
            if (api is null)
            {
                MelonLogger.Error(
                    "MDSAPI: no MDSAPI.dll found under " + apiRoot + " for the " + runtime +
                    " runtime. Every mod that depends on MDSAPI will fail to load. " +
                    "Reinstall MDSAPI, keeping its folder structure intact.");
            }
            else if (Start(new[] { api }, mods, "MDSAPI"))
            {
                MelonLogger.Msg("MDSAPI loaded (" + runtime + ") from " + Relative(api, apiRoot) + ".");
            }

            // Then every other folder that carries runtime builds. MDSAPI goes first so a
            // mod's OnInitializeMelon can already use it.
            if (!Directory.Exists(mods)) return;
            var folders = Directory.GetDirectories(mods);
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                if (string.Equals(name, ApiFolder, StringComparison.OrdinalIgnoreCase)) continue;

                var mine = Path.Combine(folder, runtime);
                var other = Path.Combine(folder, runtime == "Mono" ? "Il2Cpp" : "Mono");
                if (!Directory.Exists(mine))
                {
                    // A folder with only the other runtime's build: the player switched
                    // branch and this mod doesn't support the one they're on. Anything
                    // else (a mod's data folder) is none of our business.
                    if (Directory.Exists(other))
                        MelonLogger.Warning(name + " has no " + runtime + " build, so it is not loaded on this game branch.");
                    continue;
                }

                var dlls = Directory.GetFiles(mine, "*.dll");
                if (dlls.Length == 0) continue;
                Array.Sort(dlls, StringComparer.OrdinalIgnoreCase);
                if (Start(dlls, mods, name))
                    MelonLogger.Msg(name + " loaded (" + runtime + ").");
            }
        }

        /// <summary>
        /// Loads every assembly in <paramref name="paths"/>, then registers the melons among
        /// them. True when at least one melon started.
        /// </summary>
        private static bool Start(string[] paths, string modsDir, string label)
        {
            try
            {
                // Everything first, so a mod's own helper DLLs sitting next to it are in
                // the domain before the mod's code asks for them.
                var loaded = new List<KeyValuePair<string, Assembly>>();
                foreach (var path in paths)
                {
                    var asm = Assembly.LoadFrom(path);
                    if (asm.GetCustomAttributes(typeof(MelonInfoAttribute), false).Length > 0)
                        loaded.Add(new KeyValuePair<string, Assembly>(path, asm));
                }

                if (loaded.Count == 0)
                {
                    MelonLogger.Error(label + ": none of its DLLs contains a melon, so nothing was started.");
                    return false;
                }

                var started = 0;
                foreach (var pair in loaded)
                {
                    // An older install put the DLL straight in Mods. MelonLoader would load
                    // that copy too; it refuses a second melon with the same name and
                    // author, so this build wins, but the player should know why the log
                    // shows that refusal.
                    var stray = Path.Combine(modsDir, Path.GetFileName(pair.Key));
                    if (File.Exists(stray))
                        MelonLogger.Warning(label + ": an old copy at Mods\\" + Path.GetFileName(pair.Key) +
                                            " will be skipped. Delete it to clear the duplicate warning below.");

                    // Three things have to be right here, and each one failed silently on
                    // its own before this shape worked:
                    // 1. Loading the assembly only puts it in the domain; nothing starts.
                    // 2. LoadMelonAssembly creates the melons but leaves them unregistered,
                    //    which looks like success and still does nothing.
                    // 3. Register() is what actually starts a melon. MelonHandler.LoadFromFile
                    //    used to wrap all of this and is obsolete in 0.7.x.
                    var melonAssembly = MelonAssembly.LoadMelonAssembly(pair.Key, pair.Value, true);
                    var melons = melonAssembly?.LoadedMelons;
                    if (melons == null) continue;
                    foreach (var melon in melons)
                    {
                        melon.Register();
                        if (melon.Registered) started++;
                    }
                }

                if (started == 0)
                {
                    MelonLogger.Error(label + " was found but would not start. " +
                                      "Please report this with your game version.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Loading " + label + " failed. It will not work this session. " +
                                  "Please report this with your game version.", ex);
                return false;
            }
        }

        /// <summary>
        /// The highest-versioned MDSAPI.dll for this runtime, or null if there is none.
        /// MDSAPI ships inside every mod's download, so several copies can land here and
        /// the last installer must not be able to downgrade someone. Versioned folders let
        /// them coexist and the newest wins; a flat Mods/MDSAPI/&lt;runtime&gt; still works.
        /// </summary>
        private static string NewestApi(string root, string runtime)
        {
            if (!Directory.Exists(root)) return null;

            var flat = Path.Combine(root, runtime, "MDSAPI.dll");
            string best = File.Exists(flat) ? flat : null;
            var bestVersion = new Version(0, 0, 0);

            foreach (var folder in Directory.GetDirectories(root))
            {
                if (!Version.TryParse(Path.GetFileName(folder), out var version)) continue;
                var candidate = Path.Combine(folder, runtime, "MDSAPI.dll");
                if (File.Exists(candidate) && version > bestVersion) { bestVersion = version; best = candidate; }
            }
            return best;
        }

        private static string Relative(string path, string root) =>
            path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
