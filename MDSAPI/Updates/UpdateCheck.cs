using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MDSAPI.Config;
using MDSAPI.Logging;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MDSAPI.Updates
{
    /// <summary>MDSAPI's own settings, in UserData/MDSAPI.json.</summary>
    public sealed class MdsApiSettings : ModConfig
    {
        /// <summary>
        /// Once a day, ask GitHub whether MDSAPI or a mod built on it has a newer release, and
        /// say so in the log and on the main menu. Nothing is downloaded or installed. Set to
        /// false to never contact GitHub.
        /// </summary>
        public bool CheckForUpdates = true;

        /// <summary>
        /// Shows a sample notice on the main menu, for checking how it looks and where it sits.
        /// For testing; leave false.
        /// </summary>
        public bool PreviewUpdateNotice = false;

        /// <summary>
        /// Writes the main menu's own panels — fonts, colours, sizes, spacing — to the log once,
        /// so MDSAPI's UI can be built from the game's real values. For development; leave false.
        /// </summary>
        public bool LogMenuStyle = false;

        /// <inheritdoc/>
        protected override string FileName => "MDSAPI.json";
    }

    /// <summary>
    /// Tells players when MDSAPI or a mod built on it has a newer release on GitHub.
    /// <para>
    /// It only ever reads the latest release's tag; it never downloads or replaces anything —
    /// a mod that swaps its own DLLs is one compromised account away from shipping malware to
    /// every player. One request per repository per day, in the background with a short
    /// timeout, so a player who is offline or behind a firewall notices nothing. Players can
    /// switch it off in UserData/MDSAPI.json.
    /// </para>
    /// </summary>
    public static class UpdateCheck
    {
        private sealed class Entry
        {
            internal string Name;
            internal Version Installed;
            internal string Repo;
            internal string Url;
            internal bool Started;
        }

        /// <summary>A newer release that was found.</summary>
        public sealed class Available
        {
            /// <summary>The mod's name.</summary>
            public string Name { get; internal set; }
            /// <summary>The version installed.</summary>
            public Version Installed { get; internal set; }
            /// <summary>The newest released version.</summary>
            public Version Latest { get; internal set; }
            /// <summary>Where to get it.</summary>
            public string Url { get; internal set; }
        }

        private const int CacheHours = 24;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        private static readonly List<Entry> Entries = new();
        private static readonly ConcurrentQueue<Available> Found = new();
        private static readonly List<Available> Shown = new();
        private static readonly object CacheLock = new();
        private static Dictionary<string, CacheRow> _cache;
        private static bool _enabled = true;
        private static bool _started;

        private sealed class CacheRow
        {
            public DateTime CheckedUtc;
            public string LatestTag;
        }

        /// <summary>Every newer release found this session, in the order found.</summary>
        public static IReadOnlyList<Available> Updates => Shown;

        /// <summary>
        /// Registers a mod for the check. Call from OnInitializeMelon.
        /// </summary>
        /// <param name="name">The mod's display name ("StackLimit Plus").</param>
        /// <param name="installedVersion">The version this build is ("2.1.1").</param>
        /// <param name="githubRepo">"Owner/Repo" on GitHub (releases tagged with the version, "v2.1.2" or "2.1.2"), or "thunderstore:Namespace/Name".</param>
        /// <param name="downloadUrl">Where players should get updates (a Nexus page); defaults to the GitHub releases page.</param>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void Register(string name, string installedVersion, string githubRepo, string downloadUrl = null)
        {
            var v = ParseVersion(installedVersion);
            if (v is null || string.IsNullOrEmpty(githubRepo) || !githubRepo.Contains("/"))
            {
                Log.Warn("UpdateCheck.Register(" + name + "): need a version like \"1.2.3\" and a repo like \"Owner/Repo\".");
                return;
            }
            // remember which mod registered, so the automatic scan does not count it twice
            lock (RegisteredAssemblies) RegisteredAssemblies.Add(System.Reflection.Assembly.GetCallingAssembly().GetName().Name);
            Add(name, v, githubRepo, downloadUrl);
        }

        private static readonly HashSet<string> RegisteredAssemblies = new();

        private static Entry Add(string name, Version installed, string repo, string downloadUrl)
        {
            repo = repo.Trim().Trim('/');
            var entry = new Entry
            {
                Name = name,
                Installed = installed,
                Repo = repo,
                Url = IsTrustedUrl(downloadUrl) ? downloadUrl
                    : repo.StartsWith(ThunderstorePrefix, StringComparison.Ordinal)
                        ? "https://thunderstore.io/package/" + repo.Substring(ThunderstorePrefix.Length) + "/"
                        : "https://github.com/" + repo + "/releases/latest",
            };
            lock (Entries)
            {
                foreach (var e in Entries)
                    if (string.Equals(e.Repo, repo, StringComparison.OrdinalIgnoreCase)) return e;
                Entries.Add(entry);
            }
            if (_started) Begin(entry);
            return entry;
        }

        /// <summary>
        /// True for https pages on the sites mods are published on. The notice opens nothing
        /// else, so neither a mod nor the catalog can send a player's browser somewhere odd.
        /// </summary>
        internal static bool IsTrustedUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https") return false;
            var host = u.Host.ToLowerInvariant();
            return host == "github.com" || host == "www.nexusmods.com" || host == "nexusmods.com" || host == "thunderstore.io";
        }

        // ---- called by MdsApi --------------------------------------------------------------

        internal static void Start(bool enabled)
        {
            _enabled = enabled;
            _started = true;
            if (!enabled) return;
            List<Entry> snapshot;
            lock (Entries) snapshot = new List<Entry>(Entries);
            foreach (var e in snapshot) Begin(e);
            CheckInstalledMods();
        }

        // ---- installed mods that did not register themselves --------------------------------

        private const string Owner = "Blessingmds";
        private const string Author = "BlessingMds";
        private const string ThunderstorePrefix = "thunderstore:";

        /// <summary>
        /// Checks every installed mod by BlessingMds and every installed mod built on MDSAPI,
        /// including ones that never call <see cref="Register"/>.
        /// <para>
        /// Where to look comes from, in order: mods.json in the MDSAPI repository (BlessingMds
        /// mods; a new mod is covered by adding a line there, no MDSAPI release needed), then
        /// the GitHub or Thunderstore link in the mod's own MelonInfo. A mod with only a Nexus
        /// link cannot be checked — Nexus's API needs a personal key — and is named once in
        /// the log so its author knows why.
        /// </para>
        /// </summary>
        private static void CheckInstalledMods()
        {
            var installed = new List<(string Name, string Version, string Link, bool Ours)>();
            try
            {
                foreach (var m in MelonLoader.MelonBase.RegisteredMelons)
                {
                    var info = m?.Info;
                    if (info == null) continue;
                    var asmName = m.MelonAssembly?.Assembly?.GetName().Name;
                    // MDSAPI and its loader are covered by MDSAPI's own entry; a mod that
                    // registered itself is already being checked under the name it chose
                    if ((asmName != null && asmName.StartsWith("MDSAPI", StringComparison.Ordinal)) || info.Name == "MDSAPI") continue; // MDSAPI, its loader, its self-test
                    lock (RegisteredAssemblies) if (asmName != null && RegisteredAssemblies.Contains(asmName)) continue;
                    bool ours = info.Author != null && info.Author.IndexOf(Author, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!ours && !UsesMdsApi(m)) continue;
                    installed.Add((info.Name, info.Version, info.DownloadLink, ours));
                }
            }
            catch { return; }
            if (installed.Count == 0) return;

            Task.Run(async () =>
            {
                Dictionary<string, (string Repo, string Url, string Name)> catalog = null;
                try { catalog = await Catalog().ConfigureAwait(false); }
                catch { }

                var unchecked_ = new List<string>();
                foreach (var (name, version, link, ours) in installed)
                {
                    var v = ParseVersion(version);
                    if (v is null) continue;
                    if (ours && catalog != null && catalog.TryGetValue(name.ToLowerInvariant(), out var row))
                    {
                        Add(row.Name ?? name, v, row.Repo, row.Url);
                        continue;
                    }
                    var source = RepoFromLink(link) ?? ThunderstoreFromLink(link);
                    if (source != null) Add(name, v, source, link);
                    else unchecked_.Add(name);
                }
                if (unchecked_.Count > 0)
                    Pending.Enqueue("Not checked for updates (no GitHub or Thunderstore link in MelonInfo, " +
                                    "and no UpdateCheck.Register): " + string.Join(", ", unchecked_) + ".");
            });
        }

        // log lines raised on a background thread, written by Drain on the main thread
        private static readonly ConcurrentQueue<string> Pending = new();

        private static bool UsesMdsApi(MelonLoader.MelonBase melon)
        {
            try
            {
                var asm = melon.MelonAssembly?.Assembly;
                if (asm == null) return false;
                foreach (var r in asm.GetReferencedAssemblies())
                    if (r.Name == "MDSAPI") return true;
            }
            catch { }
            return false;
        }

        /// <summary>"Owner/Repo" from a github.com link, any owner.</summary>
        private static string RepoFromLink(string link)
        {
            if (string.IsNullOrEmpty(link) || !Uri.TryCreate(link, UriKind.Absolute, out var u) || u.Host.ToLowerInvariant() != "github.com") return null;
            var parts = u.AbsolutePath.Trim('/').Split('/');
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0 ? parts[0] + "/" + parts[1] : null;
        }

        /// <summary>"thunderstore:Namespace/Name" from a thunderstore.io package link.</summary>
        private static string ThunderstoreFromLink(string link)
        {
            if (string.IsNullOrEmpty(link) || !Uri.TryCreate(link, UriKind.Absolute, out var u) || u.Host.ToLowerInvariant() != "thunderstore.io") return null;
            var parts = u.AbsolutePath.Trim('/').Split('/');
            // /c/<community>/p/<namespace>/<name>/  or  /package/<namespace>/<name>/
            int i = Array.IndexOf(parts, "p");
            if (i < 0) i = Array.IndexOf(parts, "package");
            return i >= 0 && i + 2 < parts.Length ? ThunderstorePrefix + parts[i + 1] + "/" + parts[i + 2] : null;
        }

        /// <summary>
        /// mods.json from the MDSAPI repository (cached a day), keyed by lower-case melon name:
        /// <c>{ "mods": [ { "melon": "Stack Limit Plus", "name": "StackLimit Plus",
        /// "repo": "Blessingmds/SCH1_StackLimitPlus", "url": "https://www.nexusmods.com/..." } ] }</c>
        /// </summary>
        private static async Task<Dictionary<string, (string Repo, string Url, string Name)>> Catalog()
        {
            const string key = "catalog:" + MdsApiRepo;
            var json = CachedTag(key);
            if (json is null)
            {
                using (var http = Client())
                {
                    var response = await http.GetAsync("https://api.github.com/repos/" + MdsApiRepo + "/contents/mods.json").ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return null;
                    var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var b64 = (body.Value<string>("content") ?? "").Replace("\n", "").Replace("\r", ""); // GitHub wraps base64 at 60 columns
                    json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                }
                StoreTag(key, json);
            }

            var result = new Dictionary<string, (string, string, string)>();
            foreach (var mod in JObject.Parse(json)["mods"] ?? new JArray())
            {
                var melon = mod.Value<string>("melon");
                var repo = mod.Value<string>("repo");
                if (string.IsNullOrEmpty(melon) || string.IsNullOrEmpty(repo)) continue;
                if (!repo.StartsWith(Owner + "/", StringComparison.OrdinalIgnoreCase)) continue; // only BlessingMds repos
                result[melon.ToLowerInvariant()] = (repo, mod.Value<string>("url"), mod.Value<string>("name"));
            }
            return result;
        }

        private static HttpClient Client()
        {
            var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MDSAPI-UpdateCheck/" + MdsApi.ApiVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>Main thread: logs anything the background checks found.</summary>
        internal static void Drain()
        {
            while (Pending.TryDequeue(out var line)) Log.Info(line);
            while (Found.TryDequeue(out var a))
            {
                Shown.Add(a);
                Log.Info("Update available: " + a.Name + " " + a.Latest.ToString(3) + " (you have " +
                         a.Installed.ToString(3) + ") — " + a.Url);
                UpdateNotice.AddUpdate(a.Name, a.Latest.ToString(3), a.Installed.ToString(3), a.Url);
            }
        }

        internal const string MdsApiRepo = "Blessingmds/MDSAPI";

        /// <summary>Every address being checked, as a player would read it, for the opt-out dialog.</summary>
        internal static List<string> Repos()
        {
            var list = new List<string>();
            lock (Entries)
                foreach (var e in Entries)
                {
                    var shown = e.Repo.StartsWith(ThunderstorePrefix, StringComparison.Ordinal)
                        ? "thunderstore.io/package/" + e.Repo.Substring(ThunderstorePrefix.Length)
                        : "github.com/" + e.Repo;
                    if (!list.Contains(shown)) list.Add(shown);
                }
            return list;
        }
        internal static string MdsApiUrl => "https://github.com/" + MdsApiRepo + "/releases/latest";

        // ---- the check ---------------------------------------------------------------------

        private static void Begin(Entry e)
        {
            if (!_enabled || e.Started) return;
            e.Started = true;

            var cached = CachedTag(e.Repo);
            if (cached != null) { Compare(e, cached); return; }

            Task.Run(async () =>
            {
                try
                {
                    var tag = await LatestTag(e.Repo).ConfigureAwait(false);
                    if (tag is null) return;
                    StoreTag(e.Repo, tag);
                    Compare(e, tag);
                }
                catch { /* offline, blocked, rate-limited: not the player's problem */ }
            });
        }

        private static void Compare(Entry e, string tag)
        {
            var latest = ParseVersion(tag);
            if (latest != null && latest > e.Installed)
                Found.Enqueue(new Available { Name = e.Name, Installed = e.Installed, Latest = latest, Url = e.Url });
        }

        private static async Task<string> LatestTag(string repo)
        {
            if (repo.StartsWith(ThunderstorePrefix, StringComparison.Ordinal))
            {
                using (var http = Client())
                {
                    var response = await http.GetAsync("https://thunderstore.io/api/experimental/package/" +
                                                       repo.Substring(ThunderstorePrefix.Length) + "/").ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return null;
                    var body = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    return body["latest"]?.Value<string>("version_number");
                }
            }

            using (var http = Client())
            {
                var response = await http.GetAsync("https://api.github.com/repos/" + repo + "/releases/latest").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null; // 404: no release yet
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JObject.Parse(body).Value<string>("tag_name");
            }
        }

        /// <summary>
        /// The first dotted number in a tag: "v0.3.0", "0.3.0" and "MDSAPI-0.3.0" all give 0.3.0.
        /// Missing parts count as zero, so "2.1" equals "2.1.0".
        /// </summary>
        internal static Version ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, @"\d+(\.\d+){0,3}");
            if (!m.Success) return null;
            var parts = new List<int>();
            foreach (var p in m.Value.Split('.')) parts.Add(int.Parse(p));
            while (parts.Count < 3) parts.Add(0);
            if (parts.Count == 4 && parts[3] == 0) parts.RemoveAt(3); // 2.1.1.0 is 2.1.1, not newer than it
            return parts.Count == 3 ? new Version(parts[0], parts[1], parts[2]) : new Version(parts[0], parts[1], parts[2], parts[3]);
        }

        // ---- daily cache: UserData/MDSAPI/update-check.json ----------------------------------

        private static string CachePath => Path.Combine(MelonEnvironment.UserDataDirectory, "MDSAPI", "update-check.json");

        private static Dictionary<string, CacheRow> Cache()
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = File.Exists(CachePath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, CacheRow>>(File.ReadAllText(CachePath))
                    : null;
            }
            catch { _cache = null; }
            return _cache ??= new Dictionary<string, CacheRow>();
        }

        private static string CachedTag(string repo)
        {
            lock (CacheLock)
            {
                return Cache().TryGetValue(repo, out var row) && row != null &&
                       (DateTime.UtcNow - row.CheckedUtc).TotalHours < CacheHours
                    ? row.LatestTag
                    : null;
            }
        }

        private static void StoreTag(string repo, string tag)
        {
            lock (CacheLock)
            {
                Cache()[repo] = new CacheRow { CheckedUtc = DateTime.UtcNow, LatestTag = tag };
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                    File.WriteAllText(CachePath, JsonConvert.SerializeObject(_cache, Formatting.Indented));
                }
                catch { }
            }
        }
    }
}
