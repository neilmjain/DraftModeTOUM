using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DraftModeTOUM.Managers;
using UnityEngine;

namespace DraftModeTOUM
{
    /// <summary>
    /// Sends heartbeats to the DraftMode PHP dashboard every 10s.
    /// User ID is generated once and stored in BepInEx/config/DraftModeTOUM.userid
    /// so it's stable across sessions without touching any game API.
    /// </summary>
    public class DraftDashboardReporter : MonoBehaviour
    {
        private const string HeartbeatUrl      = "https://mckelanor.xyz/au/draft/admin/api/heartbeat.php";
        private const float  HeartbeatInterval = 10f;

        private static DraftDashboardReporter _instance;
        private static readonly HttpClient    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        // Loaded once on first heartbeat, then cached
        private static string _userId = null;

        private float  _nextHeartbeat     = 5f;
        private static string _pendingForcedRole = null;

        // ── Singleton ────────────────────────────────────────────────────────────

        public static void EnsureExists()
        {
            if (_instance != null) return;
            var go = new GameObject("DraftDashboardReporter");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DraftDashboardReporter>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ── Unity loop ───────────────────────────────────────────────────────────

        private void Update()
        {
            // Apply forced role on main thread
            if (_pendingForcedRole != null)
            {
                string role = _pendingForcedRole;
                _pendingForcedRole = null;
                ApplyForcedRole(role);
            }

            if (!CanTick()) return;

            if (Time.time >= _nextHeartbeat)
            {
                _nextHeartbeat = Time.time + HeartbeatInterval;
                TrySendHeartbeat();
            }
        }

        // ── Heartbeat ─────────────────────────────────────────────────────────────

        private static void TrySendHeartbeat()
        {
            try
            {
                var me = PlayerControl.LocalPlayer;
                if (me == null || me.Data == null) return;

                string userId    = GetOrCreateUserId();
                string name      = me.Data.PlayerName ?? me.name;
                string lobbyCode = GetLobbyCode();
                bool   isHost    = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

                Task.Run(async () => await PostHeartbeat(userId, name, lobbyCode, isHost));
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DashboardReporter] Send setup failed: {ex.Message}");
            }
        }

        private static async Task PostHeartbeat(string userId, string name, string lobbyCode, bool isHost)
        {
            try
            {
                string json =
                    "{\"player\":{" +
                    "\"userId\":\""    + Esc(userId)    + "\"," +
                    "\"name\":\""      + Esc(name)      + "\"," +
                    "\"lobbyCode\":\"" + Esc(lobbyCode) + "\"," +
                    "\"gameState\":\"lobby\"," +
                    "\"isHost\":"      + (isHost ? "true" : "false") +
                    "}}";

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp    = await _http.PostAsync(HeartbeatUrl, content);
                string body = await resp.Content.ReadAsStringAsync();

                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Heartbeat {resp.StatusCode}");
                ParseResponse(body);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DashboardReporter] Heartbeat failed: {ex.Message}");
            }
        }

        // ── Response ──────────────────────────────────────────────────────────────

        private static void ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("forcedRole", out var fr) &&
                    fr.ValueKind == JsonValueKind.String)
                {
                    string role = fr.GetString();
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Forced role queued: {role}");
                        _pendingForcedRole = role;
                    }
                }
            }
            catch { }
        }

        // ── Forced role ───────────────────────────────────────────────────────────

        private static void ApplyForcedRole(string roleName)
        {
            try
            {
                var me = PlayerControl.LocalPlayer;
                if (me == null) return;
                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Applying forced role: {roleName}");
                RoleAssigner.AssignRole(me, roleName);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[DashboardReporter] ApplyForcedRole failed: {ex.Message}");
            }
        }

        // ── User ID (file-based) ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the persistent user ID, creating it if it doesn't exist yet.
        /// Stored at: BepInEx/config/DraftModeTOUM.userid
        /// Format:    DRAFT-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx  (DRAFT- + 32 hex chars)
        /// </summary>
        private static string GetOrCreateUserId()
        {
            if (_userId != null) return _userId;

            try
            {
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "DraftModeTOUM.userid");

                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path).Trim();
                    if (existing.StartsWith("DRAFT-") && existing.Length > 6)
                    {
                        _userId = existing;
                        DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Loaded user ID: {_userId}");
                        return _userId;
                    }
                }

                // Generate a new one
                string newId = "DRAFT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                File.WriteAllText(path, newId);
                _userId = newId;
                DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Created new user ID: {_userId}");
            }
            catch (Exception ex)
            {
                // If file IO fails for any reason, use a session-only fallback
                _userId = "DRAFT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
                DraftModePlugin.Logger.LogWarning($"[DashboardReporter] Could not read/write userid file: {ex.Message}. Using session ID: {_userId}");
            }

            return _userId;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool CanTick()
        {
            try
            {
                if (AmongUsClient.Instance == null) return false;
                var state = AmongUsClient.Instance.GameState;
                return state == InnerNet.InnerNetClient.GameStates.Joined ||
                       state == InnerNet.InnerNetClient.GameStates.Started;
            }
            catch { return false; }
        }

        private static string GetLobbyCode()
        {
            try
            {
                if (AmongUsClient.Instance == null) return "";
                int id = AmongUsClient.Instance.GameId;
                if (id == 0) return "";
                return id.ToString();
            }
            catch { return ""; }
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }
    }
}
