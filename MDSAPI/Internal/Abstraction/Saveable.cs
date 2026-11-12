using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MDSAPI.Internal.Interop;
using MDSAPI.Lifecycle;
using MDSAPI.Logging;
using Newtonsoft.Json;

namespace MDSAPI.Internal.Abstraction
{
    /// <summary>
    /// Marks a private field for persistence. The key becomes a file name and is durable
    /// save data — renaming one orphans every existing save, so choose it once.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SaveableFieldAttribute : Attribute
    {
        public string Key { get; }
        public SaveableFieldAttribute(string key) => Key = key;
    }

    /// <summary>
    /// Data that belongs to one save, stored alongside it.
    /// <para>
    /// For settings that belong to the <i>player</i> rather than a save — a keybind, a
    /// window size, a limit they chose — use <see cref="MDSAPI.Config.ModConfig"/>
    /// instead. Putting those here makes the player configure the mod again on every new
    /// playthrough.
    /// </para>
    /// <para>
    /// Subclass it, mark fields with <see cref="SaveableFieldAttribute"/>, and MDSAPI
    /// handles the rest. One instance per type, created and owned by MDSAPI — give it a
    /// parameterless constructor and do not <c>new</c> it yourself.
    /// </para>
    /// <para>
    /// Why this exists rather than a static: a save reload does not restart the process.
    /// Anything you keep in a static carries into the next session and corrupts it.
    /// Rebuild runtime state in <see cref="OnLoaded"/>.
    /// </para>
    /// </summary>
    public abstract class Saveable : Registerable
    {
        private static readonly List<Saveable> Registry = new();

        private static readonly JsonSerializerSettings Json = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented,

            // Enums persist by NAME, never by number, and this is not a style choice.
            // A game enum's numeric value is positional: EQuality.Premium is 3 at
            // 0.4.7f12, and the day TVGS inserts a value above it every stored 3 silently
            // becomes a different quality. Nothing detects that — the save still loads,
            // the number is still valid, it just means something else now, and the player
            // sees their premium product turn into something cheaper with no error.
            // A name either still resolves or is visibly missing.
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
        };

        /// <summary>Folder name under the save directory. Defaults to the type name.</summary>
        protected virtual string FolderName => GetType().Name;

        /// <summary>Runs after this object's fields have been read back from disk.</summary>
        protected virtual void OnLoaded() { }

        /// <summary>Runs after this object's fields have been written to disk.</summary>
        protected virtual void OnSaved() { }

        internal static void DiscoverAndRegister()
        {
            Registry.Clear();
            foreach (var type in Reflect.DerivedFrom<Saveable>())
            {
                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    Log.Error($"{type.FullName} derives from Saveable but has no parameterless " +
                              "constructor, so MDSAPI cannot create it. Its data will not persist.");
                    continue;
                }
                try
                {
                    var instance = (Saveable)Activator.CreateInstance(type);
                    instance.CreateInternal();
                    Registry.Add(instance);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not create {type.FullName}. Its data will not persist.", ex);
                }
            }
        }

        /// <summary>Loads every registered saveable. Returns how many were handled.</summary>
        internal static int LoadAll(string saveRoot)
        {
            var loaded = 0;
            foreach (var s in Registry)
            {
                try { s.LoadFields(saveRoot); s.OnLoaded(); loaded++; }
                catch (Exception ex) { Log.Error($"Loading {s.GetType().Name} failed. It starts empty this session.", ex); }
            }
            return loaded;
        }

        internal static void SaveAll(string saveRoot)
        {
            foreach (var s in Registry)
            {
                try { s.SaveFields(saveRoot); s.OnSaved(); }
                catch (Exception ex) { Log.Error($"Saving {s.GetType().Name} failed. Changes this session may be lost.", ex); }
            }
        }

        internal static void UnregisterAll()
        {
            foreach (var s in Registry) s.DestroyInternal();
            Registry.Clear();
        }

        // Save and load walk fields identically, including fields declared on base
        // classes. They have to: if one walks the hierarchy and the other does not, an
        // inherited field is written and never read back, which looks like data loss to
        // the player and only shows up on the second session.
        private IEnumerable<(FieldInfo Field, string Key)> Fields() =>
            Reflect.AllFields(GetType())
                   .Select(f => (f, f.GetCustomAttribute<SaveableFieldAttribute>()?.Key))
                   .Where(x => x.Key != null);

        private string Dir(string saveRoot) => Path.Combine(saveRoot, "MDSAPI", FolderName);

        private void SaveFields(string saveRoot)
        {
            var dir = Dir(saveRoot);
            Directory.CreateDirectory(dir);
            foreach (var (field, key) in Fields())
            {
                var path = Path.Combine(dir, key + ".json");
                var value = field.GetValue(this);
                if (value is null) { if (File.Exists(path)) File.Delete(path); continue; }
                File.WriteAllText(path, JsonConvert.SerializeObject(value, Json));
            }
        }

        private void LoadFields(string saveRoot)
        {
            var dir = Dir(saveRoot);
            foreach (var (field, key) in Fields())
            {
                var path = Path.Combine(dir, key + ".json");
                if (!File.Exists(path)) continue;

                // Per field, so one unreadable value does not cost the player everything
                // else this saveable holds. The usual cause is an enum name that no longer
                // exists after a game update — which is exactly the case where the right
                // answer is "this one value is missing", not "your data is gone".
                try
                {
                    var text = File.ReadAllText(path);
                    field.SetValue(this, JsonConvert.DeserializeObject(text, field.FieldType, Json));
                }
                catch (Exception ex)
                {
                    Log.Error("Could not read '" + key + "' for " + GetType().Name +
                              ". That one value starts empty; the rest of its data is intact. " +
                              "If this followed a game update, a stored name has probably " +
                              "been renamed or removed.", ex);
                }
            }
        }
    }
}
