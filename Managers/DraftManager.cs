<<<<<<< HEAD
using AmongUs.GameOptions;
=======
﻿using AmongUs.GameOptions;
using DraftModeTOUM;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
<<<<<<< HEAD
=======
using Reactor.Utilities;
using UnityEngine;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte PlayerId { get; set; }
        public int SlotNumber { get; set; }
        public string? ChosenRole { get; set; }
        public bool HasPicked { get; set; }
        public bool IsPickingNow { get; set; }
        public List<string> OfferedRoles { get; set; } = new List<string>();
    }

    public static class DraftManager
    {
        public static bool IsDraftActive { get; private set; }
        public static int CurrentTurn { get; private set; }
        public static float TurnTimeLeft { get; private set; }
        public static float TurnDuration { get; set; } = 10f;

        public static bool ShowRecap { get; set; } = true;
        public static bool AutoStartAfterDraft { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances { get; set; } = true;
        public static int OfferedRolesCount { get; set; } = 3;
        public static bool ShowRandomOption { get; set; } = true;

        public static int MaxImpostors { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        private static int _impostorsDrafted = 0;
<<<<<<< HEAD
        private static int _neutralsDrafted = 0;
        private static bool _soloTestMode = false;

        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap   = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int>             _pidToSlot = new Dictionary<byte, int>();
        private static List<string>                      _lobbyRolePool = new List<string>();
        private static HashSet<string>                   _draftedRoles  =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Constant sender name matching TOUM's system style ─────────────────
        private const string SystemName = "<color=#8BFDFD>System</color>";

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;

        public static PlayerDraftState GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;
=======
        private static int _neutralKillingsDrafted = 0;
        private static int _neutralPassivesDrafted = 0;

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int> _pidToSlot = new Dictionary<byte, int>();
        private static List<string> _lobbyRolePool = new List<string>();
        private static Dictionary<string, int> _roleMaxCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _roleWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleFaction> _roleFactions = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _draftedRoleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static int GetSlotForPlayer(byte playerId) => _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState? GetStateForSlot(int slot) => _slotMap.TryGetValue(slot, out var s) ? s : null;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c

        public static PlayerDraftState? GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn  = turnNumber;
            TurnTimeLeft = TurnDuration;
            foreach (var state in _slotMap.Values)
            {
                state.IsPickingNow = (state.SlotNumber == currentPickerSlot);
                if (state.SlotNumber < currentPickerSlot)
                    state.HasPicked = true;
            }
        }

        public static void SetDraftStateFromHost(int totalSlots, List<byte> playerIds, List<int> slotNumbers)
        {
            ApplyLocalSettings();

            _slotMap.Clear();
            _pidToSlot.Clear();
            TurnOrder.Clear();

            IsDraftActive = true;
            for (int i = 0; i < playerIds.Count; i++)
            {
                var state = new PlayerDraftState { PlayerId = playerIds[i], SlotNumber = slotNumbers[i] };
                _slotMap[slotNumbers[i]]   = state;
                _pidToSlot[playerIds[i]]   = slotNumbers[i];
            }
            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;

            DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;
            DraftTicker.EnsureExists();
            Reset(cancelledBeforeCompletion: true);
            ApplyLocalSettings();

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();
            var pool = RolePoolBuilder.BuildPool();
            _lobbyRolePool = pool.Roles;
            _roleMaxCounts = pool.MaxCounts;
            _roleWeights = pool.Weights;
            _roleFactions = pool.Factions;
            if (_lobbyRolePool.Count == 0) return;

<<<<<<< HEAD
            int totalSlots    = _soloTestMode ? 4 : players.Count;
=======
            int totalSlots = players.Count;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            var syncPids  = new List<byte>();
            var syncSlots = new List<int>();

            for (int i = 0; i < totalSlots; i++)
            {
<<<<<<< HEAD
                int  slot = shuffledSlots[i];
                byte pid  = _soloTestMode
                    ? (i == 0 ? PlayerControl.LocalPlayer.PlayerId : (byte)(200 + i))
                    : players[i].PlayerId;
                _slotMap[slot]  = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
=======
                int slot = shuffledSlots[i];
                byte pid = players[i].PlayerId;
                _slotMap[slot] = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            IsDraftActive = true;

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            NotifyPlayersOfSlots();

            DraftStatusOverlay.SetState(OverlayState.Waiting);

            OfferRolesToCurrentPicker();
        }

        private static void NotifyPlayersOfSlots()
        {
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);
        }

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive = false;
<<<<<<< HEAD
            CurrentTurn   = 0;
            TurnTimeLeft  = 0f;
=======
            CurrentTurn = 0;
            TurnTimeLeft = 0f;
            DraftUiManager.CloseAll();

            if (cancelledBeforeCompletion)
            {
                DraftRecapOverlay.Hide();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
            }

>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoleCounts.Clear();
            _roleMaxCounts.Clear();
            _roleWeights.Clear();
            _roleFactions.Clear();
            TurnOrder.Clear();
<<<<<<< HEAD
            _soloTestMode      = false;
            _impostorsDrafted  = 0;
            _neutralsDrafted   = 0;
            UpCommandRequests.Clear();
=======

            _impostorsDrafted = 0;
            _neutralKillingsDrafted = 0;
            _neutralPassivesDrafted = 0;

            if (cancelledBeforeCompletion)
            {
                UpCommandRequests.Clear();
            }
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        private static List<string> GetAvailableRoles()
        {
            return _lobbyRolePool.Where(r =>
            {
<<<<<<< HEAD
                if (_draftedRoles.Contains(r)) return false;
                var faction = RoleCategory.GetFaction(r);
                if (faction == RoleFaction.Impostor && _impostorsDrafted >= MaxImpostors) return false;
                if (faction == RoleFaction.Neutral   && _neutralsDrafted  >= MaxNeutrals)  return false;
=======
                if (GetDraftedCount(r) >= GetMaxCount(r)) return false;
                var faction = GetFaction(r);
                if (faction == RoleFaction.Impostor && _impostorsDrafted >= MaxImpostors) return false;
                if (faction == RoleFaction.NeutralKilling && _neutralKillingsDrafted >= MaxNeutralKillings) return false;
                if (faction == RoleFaction.Neutral && _neutralPassivesDrafted >= MaxNeutralPassives) return false;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                return true;
            }).ToList();
        }

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.IsPickingNow = true;
            var available = GetAvailableRoles();
<<<<<<< HEAD
            if (available.Count == 0) { AutoPickRandom(); return; }
=======
            int target = OfferedRolesCount;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c

            // Try to guarantee at least one impostor and one neutral in the offer
            // when the caps allow it
            var impostorOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Impostor)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(_impostorsDrafted < MaxImpostors ? 1 : 0)
                .ToList();

            var neutralOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Neutral)
                .OrderBy(_ => UnityEngine.Random.value)
                .Take(_neutralsDrafted < MaxNeutrals ? 1 : 0)
                .ToList();

            var crewOffer = available
                .Where(r => RoleCategory.GetFaction(r) == RoleFaction.Crewmate)
                .OrderBy(_ => UnityEngine.Random.value)
                .ToList();

            var offered = new List<string>();
            offered.AddRange(impostorOffer);
            offered.AddRange(neutralOffer);

            int needed = 3 - offered.Count;
            offered.AddRange(crewOffer.Take(needed));

            if (offered.Count < 3)
            {
<<<<<<< HEAD
                var extras = available
                    .Where(r => !offered.Contains(r))
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(3 - offered.Count);
                offered.AddRange(extras);
            }

            state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
=======
                state.OfferedRoles = Enumerable.Repeat("Crewmate", target).ToList();
            }
            else
            {
                var offered = new List<string>();

                if (target >= 3)
                {
                    var impPool = available.Where(r => GetFaction(r) == RoleFaction.Impostor).ToList();
                    offered.AddRange(PickWeightedUnique(impPool, 1));

                    var nkPool = available.Where(r => GetFaction(r) == RoleFaction.NeutralKilling).ToList();
                    var npPool = available.Where(r => GetFaction(r) == RoleFaction.Neutral).ToList();
                    var neutralPool = nkPool.Count > 0 ? nkPool : npPool;
                    offered.AddRange(PickWeightedUnique(
                        neutralPool.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList(), 1));
                }

                var crewPool = available.Where(r => GetFaction(r) == RoleFaction.Crewmate
                    && !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
                offered.AddRange(PickWeightedUnique(crewPool, target - offered.Count));

                if (offered.Count < target)
                {
                    var extras = PickWeightedUnique(
                        available.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList(),
                        target - offered.Count);
                    offered.AddRange(extras);
                }

                state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
            }
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c

            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

<<<<<<< HEAD
            string chosenRole = (choiceIndex == 3 || choiceIndex >= state.OfferedRoles.Count)
=======
            string chosenRole = (choiceIndex >= state.OfferedRoles.Count)
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosenRole);
            return true;
        }

        private static void AutoPickRandom()
        {
<<<<<<< HEAD
            DraftScreenController.Hide();   // close UI if timer fired before player clicked
            FinalisePickForCurrentSlot(PickFullRandom());
=======
            var state = GetCurrentPickerState();
            if (!ShowRandomOption && state != null && state.OfferedRoles.Count > 0)
            {
                var pick = state.OfferedRoles[UnityEngine.Random.Range(0, state.OfferedRoles.Count)];
                FinalisePickForCurrentSlot(pick);
            }
            else
            {
                FinalisePickForCurrentSlot(PickFullRandom());
            }
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
        }

        private static string PickFullRandom()
        {
            var available = GetAvailableRoles();
            if (available.Count == 0) return "Crewmate";
            return UseRoleChances ? PickWeighted(available) : available[UnityEngine.Random.Range(0, available.Count)];
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole   = roleName;
            state.HasPicked    = true;
            state.IsPickingNow = false;
            _draftedRoleCounts[roleName] = GetDraftedCount(roleName) + 1;

<<<<<<< HEAD
            var faction = RoleCategory.GetFaction(roleName);
=======
            var faction = GetFaction(roleName);
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            if (faction == RoleFaction.Impostor) _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral) _neutralPassivesDrafted++;

            CurrentTurn++;
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();
                DraftUiManager.CloseAll();

<<<<<<< HEAD
                if (ShowRecap)
                    DraftNetworkHelper.BroadcastRecap(BuildRecapMessage());
                else
                    DraftNetworkHelper.BroadcastRecap("── DRAFT COMPLETE ──");
=======
                // Keep the black background up during recap
                DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

                var recapEntries = BuildRecapEntries();
                DraftNetworkHelper.BroadcastRecap(recapEntries, ShowRecap);

                Reset(cancelledBeforeCompletion: false);

                TriggerEndDraftSequence();
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            }
            else
            {
                TurnTimeLeft = TurnDuration;
                OfferRolesToCurrentPicker();
            }
        }

        public static List<RecapEntry> BuildRecapEntries()
        {
<<<<<<< HEAD
            var sb = new StringBuilder();
            sb.AppendLine("── DRAFT RECAP ──");
            foreach (var slot in TurnOrder)
            {
                var s     = GetStateForSlot(slot);
                
                sb.AppendLine($"Player {s.SlotNumber}: #{s.ChosenRole}");
=======
            var entries = new List<RecapEntry>();
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                string role = s.ChosenRole ?? "?";
                entries.Add(new RecapEntry(s.SlotNumber, role));
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            }
            return entries;
        }

        private static void ApplyAllRoles()
        {
            var allRoles = MiscUtils.AllRegisteredRoles.ToArray();
            foreach (var state in _slotMap.Values)
            {
                if (state.PlayerId >= 200 || state.ChosenRole == null) continue;
                var p = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(x => x.PlayerId == state.PlayerId);
                if (p == null) continue;

                string roleName = state.ChosenRole;
                var match = allRoles.FirstOrDefault(r =>
    Normalize(r.GetType().Name.Replace("Role", "")) == Normalize(roleName) ||
    r.NiceName.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                if (match != null) roleName = match.NiceName;

                UpCommandRequests.SetRequest(p.Data.PlayerName, roleName);
            }
        }

        // ── Messaging helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Shows a system-style chat message attributed to "Draft Mode" (cyan),
        /// matching how TOUM displays its own command feedback.
        /// </summary>
        public static void SendSystemMessage(string message, bool showHeadsup = false)
        {
            try
            {
                MiscUtils.AddFakeChat(
                    PlayerControl.LocalPlayer.Data,
                    SystemName,
                    message,
                    showHeadsup);
            }
            catch (Exception ex)
            {
                // Fallback in case MiscUtils is unavailable
                DraftModePlugin.Logger.LogWarning(
                    $"[DraftManager] AddFakeChat failed ({ex.Message}), falling back to raw chat.");
                SendChatLocal($"[Draft Mode] {message}");
            }
        }

        /// <summary>
        /// Low-level raw chat injection — only used internally as a fallback.
        /// Prefer SendSystemMessage for any user-facing messages.
        /// </summary>
        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat != null)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap = opts.ShowRecap;
            AutoStartAfterDraft = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances = opts.UseRoleChances;
            OfferedRolesCount = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption = opts.ShowRandomOption;
            MaxImpostors = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors), 0, 10);
            MaxNeutralKillings = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings), 0, 10);
            MaxNeutralPassives = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives), 0, 10);
        }

        private static int GetDraftedCount(string roleName) => _draftedRoleCounts.TryGetValue(roleName, out var count) ? count : 0;
        private static int GetMaxCount(string roleName) => _roleMaxCounts.TryGetValue(roleName, out var count) ? count : 1;
        private static RoleFaction GetFaction(string roleName) => _roleFactions.TryGetValue(roleName, out var faction) ? faction : RoleCategory.GetFaction(roleName);
        private static int GetWeight(string roleName) => _roleWeights.TryGetValue(roleName, out var weight) ? Math.Max(1, weight) : 1;

        private static string PickWeighted(List<string> candidates)
        {
            int total = candidates.Sum(GetWeight);
            if (total <= 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc = 0;
            foreach (var r in candidates)
            {
                acc += GetWeight(r);
                if (roll <= acc) return r;
            }
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static List<string> PickWeightedUnique(List<string> candidates, int count)
        {
            var results = new List<string>();
            var temp = new List<string>(candidates);
            while (results.Count < count && temp.Count > 0)
            {
                var pick = UseRoleChances ? PickWeighted(temp) : temp[UnityEngine.Random.Range(0, temp.Count)];
                results.Add(pick);
                temp.Remove(pick);
            }
            return results;
        }

        public static void TriggerEndDraftSequence()
        {
            Coroutines.Start(CoEndDraftSequence());
        }

        private static IEnumerator CoEndDraftSequence()
        {
            // 1. Wait for Recap Phase (5 seconds)
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);

            // 2. Hide Recap Text (but keep black screen for now)
            try { DraftRecapOverlay.Hide(); } catch { }

            bool isHost = AmongUsClient.Instance.AmHost;
            bool shouldAutoStart = AutoStartAfterDraft && isHost;

            // If NOT auto-starting, remove the black screen immediately
            if (!AutoStartAfterDraft)
            {
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

            // 3. START GAME (Host only)
            if (shouldAutoStart)
            {
                if (GameStartManager.Instance != null && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined)
                {
                    SkipCountdown = true;
                    int originalMinPlayers = GameStartManager.Instance.MinPlayers;
                    GameStartManager.Instance.MinPlayers = 1;
                    GameStartManager.Instance.BeginGame();
                    GameStartManager.Instance.MinPlayers = originalMinPlayers;
                    yield return null;
                    SkipCountdown = false;
                }
            }

            // 4. WAIT a small buffer (0.6s) to cover the lobby flash.
            // Since game start was triggered BEFORE this wait, the game is already loading.
            yield return new WaitForSeconds(0.6f);

            // 5. Force hide the screen. 
            // This ensures you are never stuck, even if the transition patches fail.
            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}
