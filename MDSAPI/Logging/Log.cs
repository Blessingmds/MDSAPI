using System;
using MelonLoader;

namespace MDSAPI.Logging
{
    /// <summary>
    /// Logging for mods built on MDSAPI.
    /// <para>
    /// The console belongs to the player and to every other mod. One line at startup, then
    /// only what someone would act on: a failed hook, a missing asset, a setting out of
    /// range. Nothing from a per-frame callback, ever — a line at 60fps makes every other
    /// mod's output unreadable and hides the error the player actually needs to see.
    /// </para>
    /// </summary>
    public static class Log
    {
        private static MelonLogger.Instance _logger;

        internal static void Bind(MelonLogger.Instance logger) => _logger = logger;

        /// <summary>Something happened that a player or bug reporter would want to know.</summary>
        public static void Info(string message)
        {
            if (_logger != null) _logger.Msg(message); else MelonLogger.Msg(message);
        }

        /// <summary>Something is off but the mod carries on in a reduced state.</summary>
        public static void Warn(string message)
        {
            if (_logger != null) _logger.Warning(message); else MelonLogger.Warning(message);
        }

        /// <summary>
        /// Something failed. Say what failed, what it means for the player, and what to do:
        /// "Dealer restock hook failed — restocking is off this session. Likely a game
        /// update; please report with your game version."
        /// </summary>
        public static void Error(string message)
        {
            if (_logger != null) _logger.Error(message); else MelonLogger.Error(message);
        }

        /// <inheritdoc cref="Error(string)"/>
        public static void Error(string message, Exception ex)
        {
            if (_logger != null) _logger.Error(message, ex); else MelonLogger.Error(message, ex);
        }
    }
}
