using System;
using System.Collections.Generic;
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
    /// URL: https://mckelanor.xyz/au/draft/admin/api/heartbeat.php
    /// </summary>
    public class DraftDashboardReporter : MonoBehaviour
    {
        private const string HeartbeatUrl    = "https://mckelanor.xyz/au/draft/admin/api/heartbeat.php";
        private const float  HeartbeatInterval = 10f;

        private static DraftDashboardReporter _instance;
        private static readonly HttpClient    _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };

        private float   _nextHeartbeat  = 5f; // small delay before first beat
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
            // Apply any forced role that arrived from the server
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

                string name      = me.Data.PlayerName ?? me.name;
                string userId    = BuildUserId(me);
                string lobbyCode = BuildLobbyCode();
                bool   isHost    = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

                // Capture everything before going off-thread
                Task.Run(async () =>
                {
                    await PostHeartbeat(userId, name, lobbyCode, "lobby", isHost);
                });
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DashboardReporter] TrySendHeartbeat setup failed: {ex.Message}");
            }
        }

        private static async Task PostHeartbeat(
            string userId, string name, string lobbyCode, string gameState, bool isHost)
        {
            try
            {
                // Build JSON manually to avoid any serializer issues
                string json = "{\"player\":{" +
                    "\"userId\":\""    + EscapeJson(userId)    + "\"," +
                    "\"name\":\""      + EscapeJson(name)      + "\"," +
                    "\"lobbyCode\":\"" + EscapeJson(lobbyCode) + "\"," +
                    "\"gameState\":\"" + EscapeJson(gameState) + "\"," +
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

        // ── Response parsing ──────────────────────────────────────────────────────

        private static void ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Forced role
                if (root.TryGetProperty("forcedRole", out var fr) &&
                    fr.ValueKind == JsonValueKind.String)
                {
                    string role = fr.GetString();
                    if (!string.IsNullOrWhiteSpace(role))
                    {
                        DraftModePlugin.Logger.LogInfo($"[DashboardReporter] Forced role: {role}");
                        _pendingForcedRole = role; // picked up on main thread in Update()
                    }
                }

                // Commands
                if (root.TryGetProperty("commands", out var cmds) &&
                    cmds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in cmds.EnumerateArray())
                    {
                        string cmd = el.GetString();
                        if (!string.IsNullOrWhiteSpace(cmd))
                            _pendingForcedRole = null; // commands handled separately below
                        // queue for main thread — reuse same mechanism
                        // (extend if more command types needed)
                    }
                }
            }
            catch { /* malformed JSON — ignore */ }
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

        /// <summary>
        /// Build a stable user ID. Tries EOS ProductUserId first, falls back to a
        /// deterministic string from the player name so it's at least consistent
        /// within a session.
        /// </summary>
        private static string BuildUserId(PlayerControl me)
        {
            // Try EOS product user ID — the most stable identifier
            try
            {
                if (EOSManager.Instance != null)
                {
                    var puid = EOSManager.Instance.GetProductUserId();
                    if (puid != null)
                    {
                        string s = puid.ToString();
                        if (!string.IsNullOrWhiteSpace(s) && s != "0") return s;
                    }
                }
            }
            catch { }

            // Fallback: client ID + name (stable per session)
            int clientId = AmongUsClient.Instance != null ? AmongUsClient.Instance.ClientId : 0;
            string pname = me.Data?.PlayerName ?? me.name;
            return "net_" + clientId + "_" + pname;
        }

        /// <summary>
        /// Convert the integer GameId to a human-readable room code (e.g. "ABCDEF").
        /// </summary>
        private static string BuildLobbyCode()
        {
            try
            {
                if (AmongUsClient.Instance == null) return "";
                int gameId = AmongUsClient.Instance.GameId;
                if (gameId == 0 || gameId == 32) return ""; // 32 = local/offline
                return GameCode.IntToGameName(gameId) ?? gameId.ToString();
            }
            catch { return ""; }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}
