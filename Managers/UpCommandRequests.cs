using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    /// <summary>
    /// Lightweight stub that mirrors the UpCommandRequests API from MiraCommands.
    /// Stores pending role overrides that the DashboardReporter should send
    /// as forced-role requests, falling back to a local dictionary if the
    /// reporter is unavailable.
    /// </summary>
    public static class UpCommandRequests
    {
        // Key = player name, Value = role name
        private static readonly Dictionary<string, string> _pending = new();

        public static void SetRequest(string playerName, string roleName)
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(roleName))
                return;

            _pending[playerName] = roleName;
            DraftModePlugin.Logger.LogInfo(
                $"[UpCommandRequests] Queued fallback role '{roleName}' for '{playerName}'");
        }

        /// <summary>Drain all pending entries. Called by DashboardReporter.</summary>
        public static IEnumerable<KeyValuePair<string, string>> DrainAll()
        {
            var copy = new List<KeyValuePair<string, string>>(_pending);
            _pending.Clear();
            return copy;
        }

        public static void Clear() => _pending.Clear();
    }
}
