using AmongUs.GameOptions;
using DraftModeTOUM;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte       PlayerId    { get; set; }
        public int        SlotNumber  { get; set; }
        public ushort?    ChosenRoleId { get; set; }   // stored as ID, not name
        public bool       HasPicked   { get; set; }
        public bool       IsPickingNow { get; set; }
        public List<ushort> OfferedRoleIds { get; set; } = new();  // IDs only
    }

    public static class DraftManager
    {
        public static bool  IsDraftActive  { get; private set; }
        public static int   CurrentTurn    { get; private set; }
        public static float TurnTimeLeft   { get; private set; }
        public static float TurnDuration   { get; set; } = 10f;

        public static bool ShowRecap           { get; set; } = true;
        public static bool AutoStartAfterDraft { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances      { get; set; } = true;
        public static int  OfferedRolesCount   { get; set; } = 3;
        public static bool ShowRandomOption    { get; set; } = true;

        public static int MaxImpostors       { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        private static int _impostorsDrafted      = 0;
        private static int _neutralKillingsDrafted = 0;
        private static int _neutralPassivesDrafted = 0;

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int>                      TurnOrder { get; private set; } = new();
        private static Dictionary<int, PlayerDraftState>  _slotMap   = new();
        private static Dictionary<byte, int>              _pidToSlot = new();
        private static DraftRolePool                      _pool      = new();
        private static Dictionary<ushort, int>            _draftedCounts = new();

        public static int  GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState? GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;

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
                if (state.SlotNumber < currentPickerSlot) state.HasPicked = true;
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
                var state = new PlayerDraftState
                {
                    PlayerId   = playerIds[i],
                    SlotNumber = slotNumbers[i]
                };
                _slotMap[slotNumbers[i]]   = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
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

            _pool = RolePoolBuilder.BuildPool();
            if (_pool.RoleIds.Count == 0) return;

            int totalSlots    = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots)
                .OrderBy(_ => UnityEngine.Random.value).ToList();

            List<byte> syncPids  = new();
            List<int>  syncSlots = new();

            for (int i = 0; i < totalSlots; i++)
            {
                int  slot = shuffledSlots[i];
                byte pid  = players[i].PlayerId;
                _slotMap[slot]  = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            IsDraftActive = true;

            DraftNetworkHelper.BroadcastDraftStart(totalSlots, syncPids, syncSlots);
            DraftNetworkHelper.BroadcastSlotNotifications(_pidToSlot);

            DraftStatusOverlay.SetState(OverlayState.Waiting);
            OfferRolesToCurrentPicker();
        }

        public static void Reset(bool cancelledBeforeCompletion = true)
        {
            IsDraftActive    = false;
            TurnTimerRunning = false;
            CurrentTurn      = 0;
            TurnTimeLeft     = 0f;
            DraftUiManager.CloseAll();

            if (cancelledBeforeCompletion)
            {
                // Only clear pending roles if the draft was cancelled, not completed.
                // When draft completes normally, PendingRoleAssignments must survive
                // until IntroCutsceneBeginPatch applies them on game start.
                PendingRoleAssignments.Clear();
                DraftRecapOverlay.Hide();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
            }

            _slotMap.Clear();
            _pidToSlot.Clear();
            _pool = new DraftRolePool();
            _draftedCounts.Clear();
            TurnOrder.Clear();

            _impostorsDrafted       = 0;
            _neutralKillingsDrafted = 0;
            _neutralPassivesDrafted = 0;

            if (cancelledBeforeCompletion)
                UpCommandRequests.Clear();
        }

        // True once the picker's client signals their cards are fully shown
        public static bool TurnTimerRunning { get; private set; } = false;

        public static void StartTurnTimer()
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimerRunning = true;
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost || !TurnTimerRunning) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        private static List<ushort> GetAvailableIds()
        {
            return _pool.RoleIds.Where(id =>
            {
                if (GetDraftedCount(id) >= GetMaxCount(id)) return false;
                var faction = GetFaction(id);
                if (faction == RoleFaction.Impostor       && _impostorsDrafted      >= MaxImpostors)       return false;
                if (faction == RoleFaction.NeutralKilling && _neutralKillingsDrafted >= MaxNeutralKillings) return false;
                if (faction == RoleFaction.Neutral        && _neutralPassivesDrafted >= MaxNeutralPassives) return false;
                return true;
            }).ToList();
        }

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow  = true;
            TurnTimerRunning    = false;  // wait for picker's PickerReady signal

            var available = GetAvailableIds();
            int target    = OfferedRolesCount;

            if (available.Count == 0)
            {
                // Offer crewmate vanilla as fallback
                state.OfferedRoleIds = Enumerable
                    .Repeat((ushort)RoleTypes.Crewmate, target).ToList();
            }
            else
            {
                var offered = new List<ushort>();

                if (target >= 3)
                {
                    var impPool = available.Where(id => GetFaction(id) == RoleFaction.Impostor).ToList();
                    offered.AddRange(PickWeightedUnique(impPool, 1));

                    var nkPool  = available.Where(id => GetFaction(id) == RoleFaction.NeutralKilling).ToList();
                    var npPool  = available.Where(id => GetFaction(id) == RoleFaction.Neutral).ToList();
                    var neutPool = nkPool.Count > 0 ? nkPool : npPool;
                    offered.AddRange(PickWeightedUnique(
                        neutPool.Where(id => !offered.Contains(id)).ToList(), 1));
                }

                var crewPool = available
                    .Where(id => GetFaction(id) == RoleFaction.Crewmate && !offered.Contains(id))
                    .ToList();
                offered.AddRange(PickWeightedUnique(crewPool, target - offered.Count));

                if (offered.Count < target)
                {
                    var extras = PickWeightedUnique(
                        available.Where(id => !offered.Contains(id)).ToList(),
                        target - offered.Count);
                    offered.AddRange(extras);
                }

                state.OfferedRoleIds = offered.OrderBy(_ => UnityEngine.Random.value).ToList();
            }

            DraftNetworkHelper.SendTurnAnnouncement(
                state.SlotNumber, state.PlayerId, state.OfferedRoleIds, CurrentTurn);
        }

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            ushort chosenId = (choiceIndex >= state.OfferedRoleIds.Count)
                ? PickFullRandom()
                : state.OfferedRoleIds[choiceIndex];

            FinalisePickForCurrentSlot(chosenId);
            return true;
        }

        private static void AutoPickRandom()
        {
            var state = GetCurrentPickerState();
            if (!ShowRandomOption && state != null && state.OfferedRoleIds.Count > 0)
                FinalisePickForCurrentSlot(
                    state.OfferedRoleIds[UnityEngine.Random.Range(0, state.OfferedRoleIds.Count)]);
            else
                FinalisePickForCurrentSlot(PickFullRandom());
        }

        private static ushort PickFullRandom()
        {
            var available = GetAvailableIds();
            if (available.Count == 0) return (ushort)RoleTypes.Crewmate;
            return UseRoleChances ? PickWeighted(available) : available[UnityEngine.Random.Range(0, available.Count)];
        }

        private static void FinalisePickForCurrentSlot(ushort roleId)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRoleId  = roleId;
            state.HasPicked     = true;
            state.IsPickingNow  = false;
            _draftedCounts[roleId] = GetDraftedCount(roleId) + 1;

            var faction = GetFaction(roleId);
            if (faction == RoleFaction.Impostor)       _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral)   _neutralPassivesDrafted++;

            CurrentTurn++;
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                // Draft is complete — populate PendingRoleAssignments BEFORE Reset()
                ApplyAllRoles();

                IsDraftActive = false;
                DraftUiManager.CloseAll();

                DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

                var recapEntries = BuildRecapEntries();
                DraftNetworkHelper.BroadcastRecap(recapEntries, ShowRecap);

                // Reset draft state but preserve PendingRoleAssignments —
                // pass cancelledBeforeCompletion: false so Reset() does NOT clear them.
                Reset(cancelledBeforeCompletion: false);
                TriggerEndDraftSequence();
            }
            else
            {
                TurnTimeLeft = TurnDuration;
                OfferRolesToCurrentPicker();
            }
        }

        public static List<RecapEntry> BuildRecapEntries()
        {
            var entries = new List<RecapEntry>();
            foreach (var slot in TurnOrder)
            {
                var s = GetStateForSlot(slot);
                if (s == null) continue;
                // Resolve name on the host for the recap
                string roleName = "?";
                if (s.ChosenRoleId.HasValue)
                {
                    var role = RoleManager.Instance?.GetRole((RoleTypes)s.ChosenRoleId.Value);
                    roleName = role?.NiceName ?? s.ChosenRoleId.Value.ToString();
                }
                entries.Add(new RecapEntry(s.SlotNumber, roleName));
            }
            return entries;
        }

        // Pending assignments — populated when draft ends, consumed when game starts.
        // NOT cleared by Reset(cancelledBeforeCompletion: false) so they survive
        // until IntroCutsceneBeginPatch fires after BeginGame().
        public static readonly Dictionary<byte, RoleTypes> PendingRoleAssignments = new();

        private static void ApplyAllRoles()
        {
            PendingRoleAssignments.Clear();

            foreach (var state in _slotMap.Values)
            {
                if (!state.ChosenRoleId.HasValue) continue;
                if (state.PlayerId >= 200) continue;

                PendingRoleAssignments[state.PlayerId] = (RoleTypes)state.ChosenRoleId.Value;

                DraftModePlugin.Logger.LogInfo(
                    $"[DraftManager] Queued {(RoleTypes)state.ChosenRoleId.Value} " +
                    $"for player {state.PlayerId} — will apply on game start");
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] PendingRoleAssignments populated with {PendingRoleAssignments.Count} entries");
        }

        /// <summary>
        /// Called by the IntroCutscene patch once the game has loaded.
        /// Applies all pending role assignments now that the ship is live
        /// so players have no ability access during the lobby.
        /// </summary>
        public static void ApplyPendingRolesOnGameStart()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (PendingRoleAssignments.Count == 0) return;

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Applying {PendingRoleAssignments.Count} pending draft roles on game start");

            foreach (var kvp in PendingRoleAssignments)
            {
                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == kvp.Key);
                if (p == null) continue;

                try
                {
                    p.RpcSetRole(kvp.Value, false);
                    DraftModePlugin.Logger.LogInfo(
                        $"[DraftManager] Applied {kvp.Value} to {p.Data.PlayerName}");
                }
                catch (Exception ex)
                {
                    DraftModePlugin.Logger.LogWarning(
                        $"[DraftManager] RpcSetRole failed for {p.Data.PlayerName}: {ex.Message}" +
                        $" — falling back to UpCommandRequests");
                    var role = RoleManager.Instance?.GetRole(kvp.Value);
                    if (role != null)
                        UpCommandRequests.SetRequest(p.Data.PlayerName, role.NiceName);
                }
            }

            PendingRoleAssignments.Clear();
        }

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration           = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap              = opts.ShowRecap;
            AutoStartAfterDraft    = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart  = opts.LockLobbyOnDraftStart;
            UseRoleChances         = opts.UseRoleChances;
            OfferedRolesCount      = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption       = opts.ShowRandomOption;
            MaxImpostors           = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors), 0, 10);
            MaxNeutralKillings     = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings), 0, 10);
            MaxNeutralPassives     = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives), 0, 10);
        }

        private static int    GetDraftedCount(ushort id) => _draftedCounts.TryGetValue(id, out var c) ? c : 0;
        private static int    GetMaxCount(ushort id)     => _pool.MaxCounts.TryGetValue(id, out var c) ? c : 1;
        private static RoleFaction GetFaction(ushort id)
        {
            if (_pool.Factions.TryGetValue(id, out var f)) return f;
            var role = RoleManager.Instance?.GetRole((RoleTypes)id);
            return role != null ? RoleCategory.GetFactionFromRole(role) : RoleFaction.Crewmate;
        }
        private static int    GetWeight(ushort id)       => _pool.Weights.TryGetValue(id, out var w) ? Math.Max(1, w) : 1;

        private static ushort PickWeighted(List<ushort> candidates)
        {
            int total = candidates.Sum(GetWeight);
            if (total <= 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc  = 0;
            foreach (var id in candidates)
            {
                acc += GetWeight(id);
                if (roll <= acc) return id;
            }
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static List<ushort> PickWeightedUnique(List<ushort> candidates, int count)
        {
            var results = new List<ushort>();
            var temp    = new List<ushort>(candidates);
            while (results.Count < count && temp.Count > 0)
            {
                var pick = UseRoleChances
                    ? PickWeighted(temp)
                    : temp[UnityEngine.Random.Range(0, temp.Count)];
                results.Add(pick);
                temp.Remove(pick);
            }
            return results;
        }

        public static void TriggerEndDraftSequence() =>
            Coroutines.Start(CoEndDraftSequence());

        private static IEnumerator CoEndDraftSequence()
        {
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);
            try { DraftRecapOverlay.Hide(); } catch { }

            bool shouldAutoStart = AutoStartAfterDraft && AmongUsClient.Instance.AmHost;

            if (!AutoStartAfterDraft)
            {
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

            if (shouldAutoStart &&
                GameStartManager.Instance != null &&
                AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined)
            {
                SkipCountdown = true;
                int orig = GameStartManager.Instance.MinPlayers;
                GameStartManager.Instance.MinPlayers = 1;
                GameStartManager.Instance.BeginGame();
                GameStartManager.Instance.MinPlayers = orig;
                yield return null;
                SkipCountdown = false;
            }

            yield return new WaitForSeconds(0.6f);
            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}
