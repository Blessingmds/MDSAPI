using System;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(MDSAPILoader.Loader), "MDSAPILoader", "0.1.0", "BlessingMds")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MDSAPILoader
{
    /// <summary>
    /// Puts the right MDSAPI build in the domain before any mod asks for it.
    /// <para>
    /// This is a MelonLoader <i>plugin</i>, not a mod, because plugins load before the
    /// game's assemblies. Both MDSAPI builds ship together and this picks one, so a player
    /// gets a single download and never has to know which Steam branch they are on.
    /// </para>
    /// </summary>
    public sealed class Loader : MelonPlugin
    {
        public override void OnPreModsLoaded()
        {
            // Ask MelonLoader rather than sniffing the filesystem. IsMonoRuntime is
            // correct before any game assembly has loaded; checking for an
            // Il2CppAssemblies folder is a guess that a reinstall can make wrong.
            var mono = MelonEnvironment.IsMonoRuntime;
            // Both builds are the file MDSAPI.dll — same assembly identity, so a mod
            // compiles once and runs on either branch. They are told apart by folder.
            var runtime = mono ? "Mono" : "Il2Cpp";
            var root = Path.Combine(MelonEnvironment.ModsDirectory, "MDSAPI");

            // MDSAPI ships inside each mod's download so a player gets one file instead of
            // chasing five dependencies. That means several copies can land here, and the
            // last installer must not be able to downgrade someone. Versioned folders let
            // them coexist and the newest win.
            //
            //   Mods/MDSAPI/0.2.0/Il2Cpp/MDSAPI.dll
            //   Mods/MDSAPI/0.1.0/Il2Cpp/MDSAPI.dll   <- older, ignored
            //
            // A flat Mods/MDSAPI/<runtime>/MDSAPI.dll is still honoured, so an existing
            // install keeps working and a single-version setup needs no ceremony.
            var path = NewestAvailable(root, runtime);

            if (path is null)
            {
                MelonLogger.Error(
                    "MDSAPI: no MDSAPI.dll found under " + root + " for the " + runtime +
                    " runtime. Every mod that depends on MDSAPI will fail to load. " +
                    "Reinstall MDSAPI, keeping its folder structure intact.");
                return;
            }

            try
            {
                // Three things have to be right here, and each one failed silently
                // on its own before this shape worked.
                //
                // 1. Assembly.LoadFrom only puts the assembly in the domain. Nothing is
                //    registered, so MDSAPI's MelonMod never starts.
                // 2. LoadMelonAssembly creates the melon instances but leaves them
                //    unregistered, which looks like success and still does nothing.
                // 3. Register() is what actually starts a melon. MelonHandler.LoadFromFile
                //    used to wrap all of this and is obsolete in 0.7.x; its replacement
                //    message names a MelonBase.Load that does not exist, so this does the
                //    steps explicitly.
                //
                // OnPreModsLoaded, not OnPreInitialization: registration that early is
                // dropped, because MelonLoader is not ready for it yet. This callback fires
                // immediately before the mod phase, so MDSAPI lands just ahead of the mods
                // that depend on it. MelonLoader does not recurse into Mods subfolders, so
                // Mods\MDSAPI is never scanned on its own and nothing loads twice.
                var melonAssembly = MelonAssembly.LoadMelonAssembly(path);
                var melons = melonAssembly?.LoadedMelons;

                if (melons == null || melons.Count == 0)
                {
                    MelonLogger.Error(
                        "MDSAPI: " + path + " loaded but contained no melon to start. " +
                        "The API is inert and mods depending on it will not work. " +
                        "Please report this with your game version.");
                    return;
                }

                var started = 0;
                foreach (var melon in melons)
                {
                    melon.Register();
                    if (melon.Registered) started++;
                }

                if (started == 0)
                {
                    MelonLogger.Error(
                        "MDSAPI: " + path + " was found but would not register. " +
                        "Mods depending on it will not work. " +
                        "Please report this with your game version.");
                    return;
                }

                MelonLogger.Msg("MDSAPI loaded (" + runtime + ") from " +
                                path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) + ".");
            }
            catch (Exception ex)
            {
                MelonLogger.Error(
                    "MDSAPI: loading " + path + " failed. Mods depending on MDSAPI will not " +
                    "work this session. Please report this with your game version.", ex);
            }
        }

        /// <summary>
        /// The highest-versioned MDSAPI.dll for this runtime, or null if there is none.
        /// Version comes from the folder name where there is one, so picking does not
        /// require loading every candidate assembly first.
        /// </summary>
        private static string NewestAvailable(string root, string runtime)
        {
            if (!Directory.Exists(root)) return null;

            // Unversioned layout, kept working on purpose.
            var flat = Path.Combine(root, runtime, "MDSAPI.dll");
            string best = File.Exists(flat) ? flat : null;
            var bestVersion = new Version(0, 0, 0);

            foreach (var folder in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (!Version.TryParse(name, out var version)) continue;   // not a version folder

                var candidate = Path.Combine(folder, runtime, "MDSAPI.dll");
                if (!File.Exists(candidate)) continue;

                if (version > bestVersion) { bestVersion = version; best = candidate; }
            }

            return best;
        }
    }
}
