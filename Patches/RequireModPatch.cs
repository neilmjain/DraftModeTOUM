using DraftModeTOUM.Managers;
using HarmonyLib;
using InnerNet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    public static class RequireModPatch
    {
        public static bool RequireDraftMod { get; set; } = true;

        private const string MOD_NAME = "DraftModeTOUM";

        private const float FALLBACK_KICK_DELAY = 8f;

        private static string RequiredEntry =>
            $"{MOD_NAME}: {PluginInfo.PLUGIN_VERSION}";

        private static readonly HashSet<int> _verifiedClients = new HashSet<int>();

        // Kicked for joining mid-draft — re-kick immediately on rejoin
        private static readonly HashSet<string> _draftKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Confirmed missing/wrong mod — re-kick immediately on rejoin
        private static readonly HashSet<string> _modKickedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Pending fallback kicks: clientId -> (playerName, deadline)
        private static readonly Dictionary<int, (string playerName, float deadline)> _pendingFallbacks
            = new Dictionary<int, (string, float)>();

        public static void Apply(Harmony harmony)
        {
            try
            {
                // Patch Handle() on the RPC class — this is what TOU actually calls,
                // and it reliably fires because it's a virtual method on a registered RPC type.
                var handleTarget = AccessTools.Method(
                    "TownOfUs.Networking.SendClientModInfoRpc:Handle");

                if (handleTarget == null)
                {
                    DraftModePlugin.Logger.LogError(
                        "[RequireModPatch] Could not find SendClientModInfoRpc.Handle — trying ReceiveClientModInfo fallback.");

                    // Fallback: try patching ReceiveClientModInfo directly
                    var receiveTarget = AccessTools.Method(
                        "TownOfUs.Networking.SendClientModInfoRpc:ReceiveClientModInfo");

                    if (receiveTarget == null)
                    {
                        DraftModePlugin.Logger.LogError(
                            "[RequireModPatch] Could not find ReceiveClientModInfo either — mod check patch skipped.");
                    }
                    else
                    {
                        harmony.Patch(receiveTarget,
                            postfix: new HarmonyMethod(typeof(RequireModPatch), nameof(ModInfoPostfix)));
                        DraftModePlugin.Logger.LogInfo(
                            $"[RequireModPatch] Patched ReceiveClientModInfo (fallback). Requiring: {RequiredEntry}");
                    }
                }
                else
                {
                    harmony.Patch(handleTarget,
                        postfix: new HarmonyMethod(typeof(RequireModPatch), nameof(HandlePostfix)));
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Patched SendClientModInfoRpc.Handle. Requiring: {RequiredEntry}");
                }

                var joinTarget = AccessTools.Method(
                    typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined));

                if (joinTarget == null)
                {
                    DraftModePlugin.Logger.LogError(
                        "[RequireModPatch] Could not find OnPlayerJoined — rejoin patch skipped.");
                }
                else
                {
                    harmony.Patch(joinTarget,
                        postfix: new HarmonyMethod(typeof(RequireModPatch), nameof(OnPlayerJoinedPostfix)));
                    DraftModePlugin.Logger.LogInfo("[RequireModPatch] Patched OnPlayerJoined.");
                }
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogError($"[RequireModPatch] Failed to apply patch: {ex}");
            }
        }

        // Postfix for Handle(PlayerControl innerNetObject, Dictionary<byte,string> data)
        public static void HandlePostfix(PlayerControl innerNetObject, Dictionary<byte, string>? data)
        {
            if (data == null || data.Count == 0) return;
            ModInfoPostfix(innerNetObject, data);
        }

        public static void OnPlayerJoinedPostfix(ClientData data)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (data.Id == AmongUsClient.Instance.ClientId) return;

            string playerName = data.PlayerName ?? string.Empty;

            // Re-kick immediately if confirmed missing/wrong mod
            if (_modKickedPlayers.Contains(playerName))
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Mod-kicked player '{playerName}' rejoined — kicking again.");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION} is required to join.</color>");
                AmongUsClient.Instance.KickPlayer(data.Id, false);
                return;
            }

            // Re-kick immediately if they joined during an active draft
            if (_draftKickedPlayers.Contains(playerName))
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Draft-kicked player '{playerName}' rejoined — kicking again.");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — draft has already started.</color>");
                AmongUsClient.Instance.KickPlayer(data.Id, false);
                return;
            }

            if (DraftManager.IsDraftActive && DraftManager.LockLobbyOnDraftStart)
            {
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Draft active — kicking client {data.Id} ({playerName}).");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — draft has already started.</color>");
                _draftKickedPlayers.Add(playerName);
                AmongUsClient.Instance.KickPlayer(data.Id, false);
                return;
            }

            // Register fallback in case Handle never fires (e.g. Reactor RPC crash)
            if (RequireDraftMod)
            {
                _pendingFallbacks[data.Id] = (playerName, Time.realtimeSinceStartup + FALLBACK_KICK_DELAY);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] Registered fallback kick for {playerName} (clientId={data.Id}) in {FALLBACK_KICK_DELAY}s.");
            }
        }

        public static void ModInfoPostfix(PlayerControl client, Dictionary<byte, string> list)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (!RequireDraftMod) return;
            if (client.AmOwner) return;

            var playerInfo = GameData.Instance.GetPlayerById(client.PlayerId);
            if (playerInfo == null) return;

            string playerName = client.Data.PlayerName ?? string.Empty;

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Checking mods for {playerName} (clientId={playerInfo.ClientId})...");

            if (_verifiedClients.Contains(playerInfo.ClientId))
            {
                _pendingFallbacks.Remove(playerInfo.ClientId);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] {playerName} already verified — skipping.");
                return;
            }

            bool hasMod = list.Values.Any(v =>
                v.Contains(MOD_NAME, StringComparison.OrdinalIgnoreCase));

            bool hasCorrectVersion = list.Values.Any(v =>
                v.Contains(RequiredEntry, StringComparison.OrdinalIgnoreCase));

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] {playerName} — hasMod={hasMod}, hasCorrectVersion={hasCorrectVersion}, looking for: '{RequiredEntry}'");

            if (hasCorrectVersion)
            {
                _verifiedClients.Add(playerInfo.ClientId);
                _pendingFallbacks.Remove(playerInfo.ClientId);
                _modKickedPlayers.Remove(playerName);
                DraftModePlugin.Logger.LogInfo(
                    $"[RequireModPatch] {playerName} verified with {RequiredEntry}.");
                return;
            }

            string reason = hasMod
                ? $"wrong version of <b>{MOD_NAME}</b> — host has v{PluginInfo.PLUGIN_VERSION}"
                : $"missing <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}";

            DraftManager.SendChatLocal(
                $"<color=#FF4444>{playerName} was kicked — {reason}.</color>");

            _pendingFallbacks.Remove(playerInfo.ClientId);
            _modKickedPlayers.Add(playerName);
            AmongUsClient.Instance.KickPlayer(playerInfo.ClientId, false);

            DraftModePlugin.Logger.LogInfo(
                $"[RequireModPatch] Kicked {playerName} ({playerInfo.ClientId}) — {reason}.");
        }

        /// <summary>
        /// Called every frame from FallbackTickPatch (LobbyBehaviour.Update).
        /// Kicks players who joined but never had Handle/ReceiveClientModInfo fire for them.
        /// </summary>
        public static void FallbackTick()
        {
            if (_pendingFallbacks.Count == 0) return;
            if (!AmongUsClient.Instance.AmHost || !RequireDraftMod)
            {
                _pendingFallbacks.Clear();
                return;
            }

            float now = Time.realtimeSinceStartup;
            var expired = new List<int>();

            foreach (var kvp in _pendingFallbacks)
            {
                if (now >= kvp.Value.deadline)
                    expired.Add(kvp.Key);
            }

            foreach (int clientId in expired)
            {
                var (playerName, _) = _pendingFallbacks[clientId];
                _pendingFallbacks.Remove(clientId);

                if (_verifiedClients.Contains(clientId))
                {
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Fallback: {playerName} already verified — no kick.");
                    continue;
                }

                var client = AmongUsClient.Instance.GetClient(clientId);
                if (client == null)
                {
                    DraftModePlugin.Logger.LogInfo(
                        $"[RequireModPatch] Fallback: {playerName} already left — no kick.");
                    continue;
                }

                DraftModePlugin.Logger.LogWarning(
                    $"[RequireModPatch] Fallback kick: {playerName} (clientId={clientId}) — Handle never fired. Kicking.");
                DraftManager.SendChatLocal(
                    $"<color=#FF4444>{playerName} was kicked — could not verify <b>{MOD_NAME}</b> v{PluginInfo.PLUGIN_VERSION}.</color>");
                _modKickedPlayers.Add(playerName);
                AmongUsClient.Instance.KickPlayer(clientId, false);
            }
        }

        public static void ClearSession()
        {
            _pendingFallbacks.Clear();
            _verifiedClients.Clear();
            _draftKickedPlayers.Clear();
            _modKickedPlayers.Clear();
        }
    }
}
