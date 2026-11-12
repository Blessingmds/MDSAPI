using MDSAPI.Internal.Interop;

namespace MDSAPI.Multiplayer
{
    /// <summary>
    /// The Steam lobby. Lobby data is a small shared key/value store the host writes and
    /// every player reads — the simple way to send a mod's settings from host to clients
    /// without networking code. Values are text and Steam caps their size (about 8 KB).
    /// </summary>
    public static class Lobby
    {
        private const string Type = "Networking.Lobby";
        private static readonly Access<bool> InLobbyAccess = new(Type, "IsInLobby");
        private static readonly Access<bool> HostAccess = new(Type, "IsHost");
        private static readonly GameMethod SetLobbyData = new(Type, null, "SetLobbyData");
        private static readonly Access<object> Service = new(Type, "_lobbyService");
        private static readonly GameMethod GetLobbyData = new("Networking.ILobbyService", null, "GetLobbyData");

        static Lobby() => Modules.Register("Lobby", () =>
            InLobbyAccess.IsAvailable && HostAccess.IsAvailable && SetLobbyData.IsAvailable &&
            Service.IsAvailable && GetLobbyData.IsAvailable);

        /// <summary>True when these members were found on this game version.</summary>
        public static bool IsAvailable => Modules.IsAvailable("Lobby");

        private static object Instance => Singletons.TryResolve(Type);

        /// <summary>True while in a multiplayer lobby (hosting or joined).</summary>
        public static bool InLobby
        {
            get
            {
                var l = Instance;
                return l != null && InLobbyAccess.Get(l);
            }
        }

        /// <summary>True when in a lobby and hosting it.</summary>
        public static bool IsHost
        {
            get
            {
                var l = Instance;
                return l != null && InLobbyAccess.Get(l) && HostAccess.Get(l);
            }
        }

        /// <summary>Publishes a value to every player. Only the host's writes take effect.</summary>
        public static bool SetData(string key, string value)
        {
            var l = Instance;
            return l != null && SetLobbyData.TryInvoke(l, key, value);
        }

        /// <summary>Reads a value the host published; null or empty when there is none.</summary>
        public static string GetData(string key)
        {
            var l = Instance;
            var service = l is null ? null : Service.Get(l);
            return service is null ? null : GetLobbyData.Call<string>(service, null, key);
        }
    }
}
