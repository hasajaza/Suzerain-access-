using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace SuzerainAccess.Core
{
    /// <summary>
    /// Central logging. Everything goes to the BepInEx log (BepInEx\LogOutput.log).
    /// Repeated warnings/errors are throttled so an unknown UI element can never flood the log.
    /// (Named ModLog because the game itself declares a global class called "Log".)
    /// </summary>
    internal static class ModLog
    {
        private static ManualLogSource _source;
        private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();

        public static bool DebugEnabled { get; set; }

        public static void Init(ManualLogSource source) => _source = source;

        public static void Info(string message) => _source?.LogInfo(message);
        public static void Warn(string message) => _source?.LogWarning(message);
        public static void Error(string message) => _source?.LogError(message);

        public static void Debug(string message)
        {
            if (DebugEnabled) _source?.LogInfo("[debug] " + message);
        }

        /// <summary>Logs a warning only the first time a given key is seen.</summary>
        public static void WarnOnce(string key, string message)
        {
            if (Bump(key) == 1) Warn(message);
        }

        /// <summary>Logs an info line only the first time a given key is seen.</summary>
        public static void InfoOnce(string key, string message)
        {
            if (Bump(key) == 1) Info(message);
        }

        /// <summary>
        /// Logs an exception from a per-frame subsystem: the first 3 occurrences in full,
        /// then one summary line every 500 occurrences. Never rethrows.
        /// </summary>
        public static void Exception(string key, Exception ex)
        {
            int n = Bump("ex:" + key);
            if (n <= 3)
                Error($"[{key}] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            else if (n % 500 == 0)
                Error($"[{key}] still failing ({n} times). Last: {ex.GetType().Name}: {ex.Message}");
        }

        private static int Bump(string key)
        {
            Counters.TryGetValue(key, out int n);
            n++;
            Counters[key] = n;
            return n;
        }
    }
}
