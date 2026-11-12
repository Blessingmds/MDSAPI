using System;
using System.IO;
using MDSAPI.Logging;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace MDSAPI.Config
{
    /// <summary>
    /// Settings that belong to the player, not to a save.
    /// <para>
    /// This is the counterpart to <c>Saveable</c>, and the distinction matters. A keybind,
    /// a window size, a stack limit the player chose — those apply to every save and should
    /// survive starting a new one. Progress, grudges, employee skills belong to one save and
    /// go in a <c>Saveable</c>. Putting the first kind in a save means the player sets it up
    /// again every playthrough; putting the second kind here means one save leaks into
    /// another.
    /// </para>
    /// <para>
    /// Available from <c>OnInitializeMelon</c>, before any save exists — which is the other
    /// reason it cannot be a Saveable. Written as readable JSON in UserData so a player can
    /// edit it by hand, because they will.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// public sealed class MySettings : ModConfig
    /// {
    ///     public int StackLimit = 20;                                // the default
    ///     public Dictionary&lt;string, int&gt; PerPlace = new();
    /// }
    ///
    /// // Once, at startup. Read it — never write here, or every launch overwrites
    /// // whatever the player chose with your default.
    /// Settings = ModConfig.Load&lt;MySettings&gt;();
    ///
    /// // Later, when the PLAYER changes something.
    /// Settings.StackLimit = chosenInMenu;
    /// Settings.Save();
    /// </code>
    /// </example>
    public abstract class ModConfig
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,

            // By name, never by number — same reason as Saveable. An enum's value is
            // positional and a game update can shift it under a stored config.
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        };

        /// <summary>
        /// File name under UserData. Defaults to the type name. Override it to give players
        /// something recognisable, since they will find this file and open it.
        /// </summary>
        protected virtual string FileName => GetType().Name + ".json";

        private string Path_ => Path.Combine(MelonEnvironment.UserDataDirectory, FileName);

        /// <summary>Runs after the file has been read, before the caller sees the object.</summary>
        protected virtual void OnLoaded() { }

        /// <summary>
        /// Reads the config, or returns defaults if there is no file yet. Never throws —
        /// a mod should not fail to start because a config file is malformed.
        /// </summary>
        public static T Load<T>() where T : ModConfig, new()
        {
            var fresh = new T();
            var path = fresh.Path_;

            if (!File.Exists(path))
            {
                fresh.Save();          // so the player has something to edit
                fresh.OnLoaded();
                return fresh;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<T>(File.ReadAllText(path), Json) ?? fresh;
                loaded.OnLoaded();
                return loaded;
            }
            catch (Exception ex)
            {
                // Keep what the player wrote. Overwriting a hand-edited config with
                // defaults because one brace is missing turns a typo into lost work, and
                // they have no way to get it back.
                var backup = path + ".broken";
                try { File.Copy(path, backup, true); } catch { /* best effort */ }

                Log.Error("Could not read " + fresh.FileName + ", so defaults are in use " +
                          "this session. Your file has been kept as " +
                          System.IO.Path.GetFileName(backup) + " — fix it and restart, or " +
                          "delete it to start fresh.", ex);

                fresh.OnLoaded();
                return fresh;
            }
        }

        /// <summary>Writes the config. Returns false, having logged, if it could not.</summary>
        public bool Save()
        {
            try
            {
                var path = Path_;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

                // Write beside, then move into place. A crash mid-write otherwise leaves a
                // truncated file, which reads as corruption on the next launch.
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Json));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not write " + FileName + ". Settings changed this session " +
                          "will not be remembered.", ex);
                return false;
            }
        }
    }
}
