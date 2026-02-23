using AmongUs.GameOptions;
using DraftModeTOUM;
using DraftModeTOUM.Patches;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Options;
using TownOfUs.Utilities;

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

        // ── Recap toggle ─────────────────────────────────────────────────────
        public static bool ShowRecap { get; set; } = true;
        public static bool AutoStartAfterDraft { get; set; } = true;
        public static bool LockLobbyOnDraftStart { get; set; } = true;
        public static bool UseRoleChances { get; set; } = true;
        public static int OfferedRolesCount { get; set; } = 3;
        public static bool ShowRandomOption { get; set; } = true;

        // ── Faction caps (classic mode only) ─────────────────────────────────
        public static int MaxImpostors { get; set; } = 2;
        public static int MaxNeutralKillings { get; set; } = 2;
        public static int MaxNeutralPassives { get; set; } = 3;

        private static int _impostorsDrafted = 0;
        private static int _neutralKillingsDrafted = 0;
        private static int _neutralPassivesDrafted = 0;

        // ── Classic-mode per-role budget ─────────────────────────────────────
        private static Dictionary<string, int> _roleRemainingSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ── Role List mode: ordered slots (one per player turn) ──────────────
        private static bool _isRoleListMode = false;
        /// <summary>
        /// Per-turn slot data for Role List mode.
        /// Index corresponds to (TurnOrder index), i.e., slot at index 0 is for the 1st picker.
        /// </summary>
        private static List<DraftSlot> _turnSlots = new List<DraftSlot>();

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int> TurnOrder { get; private set; } = new List<int>();
        private static Dictionary<int, PlayerDraftState> _slotMap = new Dictionary<int, PlayerDraftState>();
        private static Dictionary<byte, int> _pidToSlot = new Dictionary<byte, int>();
        private static List<string> _lobbyRolePool = new List<string>();
        private static Dictionary<string, int> _roleMaxCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _roleWeights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleFaction> _roleFactions = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int> _draftedRoleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleBehaviour> _roleLookup = new Dictionary<string, RoleBehaviour>(StringComparer.OrdinalIgnoreCase);

        public static RoleBehaviour? FindRoleInPool(string canonicalKey) =>
            _roleLookup.TryGetValue(canonicalKey, out var role) ? role : null;

        public static int GetSlotForPlayer(byte playerId) => _pidToSlot.TryGetValue(playerId, out int slot) ? slot : -1;
        public static PlayerDraftState? GetStateForSlot(int slot) => _slotMap.TryGetValue(slot, out var s) ? s : null;

        public static PlayerDraftState? GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn = turnNumber;
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
                _slotMap[slotNumbers[i]] = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }
            TurnOrder = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn = 1;
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
            _lobbyRolePool  = pool.Roles;
            _roleMaxCounts  = pool.MaxCounts;
            _roleWeights    = pool.Weights;
            _roleFactions   = pool.Factions;
            _roleLookup     = pool.RoleLookup;
            _isRoleListMode = pool.IsRoleListMode;
            _turnSlots      = pool.Slots;

            if (_isRoleListMode)
            {
                // In Role List mode we need at least 1 slot; role pool can be empty
                if (_turnSlots.Count == 0)
                {
                    DraftModePlugin.Logger.LogWarning(
                        "[DraftManager] Role List mode active but no slots found — aborting draft");
                    return;
                }
            }
            else
            {
                if (_lobbyRolePool.Count == 0) return;
            }

            int totalSlots = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            List<byte> syncPids  = new List<byte>();
            List<int>  syncSlots = new List<int>();

            for (int i = 0; i < totalSlots; i++)
            {
                int slot = shuffledSlots[i];
                byte pid = players[i].PlayerId;
                _slotMap[slot] = new PlayerDraftState { PlayerId = pid, SlotNumber = slot };
                _pidToSlot[pid] = slot;
                syncPids.Add(pid);
                syncSlots.Add(slot);
            }

            TurnOrder = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn = 1;
            TurnTimeLeft = TurnDuration;
            IsDraftActive = true;

            // Classic-mode budget (not used in Role List mode)
            if (!_isRoleListMode)
                BuildRemainingSlots();
            else
                ComputeRoleListFactionQuotas();

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
            CurrentTurn   = 0;
            TurnTimeLeft  = 0f;
            DraftUiManager.CloseAll();

            if (cancelledBeforeCompletion)
            {
                DraftRecapOverlay.Hide();
                DraftStatusOverlay.SetState(OverlayState.Hidden);
            }

            _slotMap.Clear();
            _pidToSlot.Clear();
            _lobbyRolePool.Clear();
            _draftedRoleCounts.Clear();
            _roleMaxCounts.Clear();
            _roleWeights.Clear();
            _roleFactions.Clear();
            _roleRemainingSlots.Clear();
            _roleLookup.Clear();
            _turnSlots.Clear();
            _isRoleListMode = false;
            TurnOrder.Clear();

            _impostorsDrafted       = 0;
            _neutralKillingsDrafted = 0;
            _neutralPassivesDrafted = 0;
            _totalImpSlotsInList      = 0;
            _totalNeutKillSlotsInList = 0;
            _totalNeutSlotsInList     = 0;

            if (cancelledBeforeCompletion)
                UpCommandRequests.Clear();
        }

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Classic-mode budget helpers
        // ─────────────────────────────────────────────────────────────────────

        private static void BuildRemainingSlots()
        {
            _roleRemainingSlots.Clear();

            int impBudget = MaxImpostors;
            int nkBudget  = MaxNeutralKillings;
            int npBudget  = MaxNeutralPassives;

            foreach (var roleName in _lobbyRolePool)
            {
                int settingMax = GetMaxCount(roleName);
                var faction    = GetFaction(roleName);

                int allowed;
                switch (faction)
                {
                    case RoleFaction.Impostor:
                        allowed = Math.Min(settingMax, impBudget);
                        impBudget -= allowed;
                        break;
                    case RoleFaction.NeutralKilling:
                        allowed = Math.Min(settingMax, nkBudget);
                        nkBudget -= allowed;
                        break;
                    case RoleFaction.Neutral:
                        allowed = Math.Min(settingMax, npBudget);
                        npBudget -= allowed;
                        break;
                    default:
                        allowed = settingMax;
                        break;
                }

                if (allowed > 0)
                    _roleRemainingSlots[roleName] = allowed;
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Slot budget — Imp:{MaxImpostors - impBudget}/{MaxImpostors}  " +
                $"NK:{MaxNeutralKillings - nkBudget}/{MaxNeutralKillings}  " +
                $"NP:{MaxNeutralPassives - npBudget}/{MaxNeutralPassives}  " +
                $"Roles in pool: {_roleRemainingSlots.Count}");
        }

        private static List<string> GetAvailableRoles()
        {
            return _lobbyRolePool
                .Where(r => _roleRemainingSlots.TryGetValue(r, out int rem) && rem > 0)
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Role List mode helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the DraftSlot for the current turn (0-indexed turn index).
        /// Falls back to a "Any" slot with all pool roles if out of range.
        /// </summary>
        private static DraftSlot GetSlotForCurrentTurn()
        {
            int turnIndex = CurrentTurn - 1; // TurnOrder is 1-based
            if (turnIndex >= 0 && turnIndex < _turnSlots.Count)
                return _turnSlots[turnIndex];

            // Beyond 15 players: offer any role
            return new DraftSlot(RoleListOption.Any, _lobbyRolePool.ToList());
        }

        // ── Role List mode: total imp/neut/crew slots in the list ─────────────
        private static int _totalImpSlotsInList   = 0;
        private static int _totalNeutKillSlotsInList = 0;
        private static int _totalNeutSlotsInList  = 0;

        /// <summary>
        /// Pre-computes how many imp / neutral-killing / neutral slots exist
        /// in the full turn slot list. Called once when the draft starts.
        /// </summary>
        private static void ComputeRoleListFactionQuotas()
        {
            _totalImpSlotsInList      = 0;
            _totalNeutKillSlotsInList = 0;
            _totalNeutSlotsInList     = 0;

            foreach (var slot in _turnSlots)
            {
                bool hasImp  = slot.ValidRoles.Any(r =>
                    _roleFactions.TryGetValue(r, out var f) && f == RoleFaction.Impostor);
                bool hasNK   = slot.ValidRoles.Any(r =>
                    _roleFactions.TryGetValue(r, out var f) && f == RoleFaction.NeutralKilling);
                bool hasNeut = slot.ValidRoles.Any(r =>
                    _roleFactions.TryGetValue(r, out var f) && f == RoleFaction.Neutral);

                if (hasImp)  _totalImpSlotsInList++;
                if (hasNK)   _totalNeutKillSlotsInList++;
                if (hasNeut) _totalNeutSlotsInList++;
            }

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Role List quotas — Imp:{_totalImpSlotsInList}  " +
                $"NK:{_totalNeutKillSlotsInList}  Neut:{_totalNeutSlotsInList}");
        }

        /// <summary>
        /// Returns enabled roles for a slot that haven't been exhausted yet.
        /// In Role List mode a role faction is considered exhausted only when
        /// as many players have drafted it as there are slots for that faction
        /// in the full role list — NOT based on vanilla per-role MaxCount.
        /// </summary>
        private static List<string> GetAvailableRolesForSlot(DraftSlot slot)
        {
            return slot.ValidRoles
                .Where(r =>
                {
                    if (!_lobbyRolePool.Contains(r)) return false;

                    // Per-role hard cap (e.g. Ambusher set to 1 in settings).
                    // We still respect this so the same specific role isn't
                    // drafted more times than its setting allows.
                    if (GetDraftedCount(r) >= GetMaxCount(r)) return false;

                    // Faction-level cap: only suppress a faction's roles once
                    // as many players have already chosen that faction as there
                    // are slots for it in the role list.
                    if (!_roleFactions.TryGetValue(r, out var faction)) return true;

                    if (faction == RoleFaction.Impostor &&
                        _impostorsDrafted >= _totalImpSlotsInList)
                        return false;

                    if (faction == RoleFaction.NeutralKilling &&
                        _neutralKillingsDrafted >= _totalNeutKillSlotsInList)
                        return false;

                    if (faction == RoleFaction.Neutral &&
                        _neutralPassivesDrafted >= _totalNeutSlotsInList)
                        return false;

                    return true;
                })
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Offering roles to the current picker
        // ─────────────────────────────────────────────────────────────────────

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;

            List<string> offered;

            if (_isRoleListMode)
                offered = BuildOffersForRoleListMode();
            else
                offered = BuildOffersForClassicMode();

            state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();

            DraftNetworkHelper.SendTurnAnnouncement(state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        // Chance (0–100) that a second bucket card appears in the hand.
        private const int SecondBucketCardChance = 15;

        private static List<string> BuildOffersForRoleListMode()
        {
            int target = OfferedRolesCount;
            var slot   = GetSlotForCurrentTurn();

            // bucketRoles = roles valid for THIS slot's bucket.
            // otherRoles  = roles valid for the OTHER remaining slots in this game.
            //               Built by union-ing the valid roles of all other turns' slots
            //               so we never show a faction that has no slot in this lobby.
            var bucketRoles = GetAvailableRolesForSlot(slot);

            var otherRoles = new List<string>();
            for (int i = 0; i < _turnSlots.Count; i++)
            {
                if (i == CurrentTurn - 1) continue; // skip current slot
                foreach (var r in _turnSlots[i].ValidRoles)
                {
                    if (bucketRoles.Contains(r, StringComparer.OrdinalIgnoreCase)) continue;
                    if (GetDraftedCount(r) >= GetMaxCount(r)) continue;
                    if (!otherRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
                        otherRoles.Add(r);
                }
            }

            var offered = new List<string>();

            if (bucketRoles.Count > 0)
            {
                // 1. One guaranteed bucket card.
                offered.AddRange(PickWeightedUnique(bucketRoles, 1));

                // 2. Rare second bucket card.
                if (target >= 2 && bucketRoles.Count > 1
                    && UnityEngine.Random.Range(0, 100) < SecondBucketCardChance)
                {
                    var pool2 = bucketRoles
                        .Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (pool2.Count > 0)
                        offered.AddRange(PickWeightedUnique(pool2, 1));
                }
            }

            // 3. Fill the rest of the hand with OTHER enabled roles (crew, neut, etc.).
            //    This is the majority of the hand — the bucket card is just 1 option among many.
            int remaining = target - offered.Count;
            if (remaining > 0 && otherRoles.Count > 0)
            {
                var filler = PickWeightedUnique(
                    otherRoles.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList(),
                    remaining);
                offered.AddRange(filler);
            }

            // Only use bare Crewmate if we ended up with a completely empty hand.
            if (offered.Count == 0)
                offered.Add("Crewmate");

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Turn {CurrentTurn} bucket={slot.Bucket} " +
                $"bucket={bucketRoles.Count} other={otherRoles.Count} offered={offered.Count}");

            return offered;
        }

        private static List<string> BuildOffersForClassicMode()
        {
            int target    = OfferedRolesCount;
            var available = GetAvailableRoles();

            if (available.Count == 0)
                return Enumerable.Repeat("Crewmate", target).ToList();

            var offered = new List<string>();

            if (target >= 3)
            {
                var impPool = available.Where(r => GetFaction(r) == RoleFaction.Impostor).ToList();
                if (impPool.Count > 0)
                    offered.AddRange(PickWeightedUnique(impPool, 1));

                var nkPool = available.Where(r => GetFaction(r) == RoleFaction.NeutralKilling
                    && !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
                var npPool = available.Where(r => GetFaction(r) == RoleFaction.Neutral
                    && !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
                var neutralPool = nkPool.Count > 0 ? nkPool : npPool;
                if (neutralPool.Count > 0)
                    offered.AddRange(PickWeightedUnique(neutralPool, 1));
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

            while (offered.Count < target)
                offered.Add("Crewmate");

            return offered;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pick submission
        // ─────────────────────────────────────────────────────────────────────

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            string chosenRole = (choiceIndex >= state.OfferedRoles.Count)
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosenRole);
            return true;
        }

        private static void AutoPickRandom()
        {
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
        }

        private static string PickFullRandom()
        {
            if (_isRoleListMode)
            {
                var slot      = GetSlotForCurrentTurn();
                var available = GetAvailableRolesForSlot(slot);
                if (available.Count == 0) return "Crewmate";
                return UseRoleChances ? PickWeighted(available) : available[UnityEngine.Random.Range(0, available.Count)];
            }

            var classicAvail = GetAvailableRoles();
            if (classicAvail.Count == 0) return "Crewmate";
            return UseRoleChances ? PickWeighted(classicAvail) : classicAvail[UnityEngine.Random.Range(0, classicAvail.Count)];
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole    = roleName;
            state.HasPicked     = true;
            state.IsPickingNow  = false;
            _draftedRoleCounts[roleName] = GetDraftedCount(roleName) + 1;

            if (!_isRoleListMode)
            {
                // Classic mode: decrement pre-computed slot budget
                if (_roleRemainingSlots.TryGetValue(roleName, out int rem))
                    _roleRemainingSlots[roleName] = Math.Max(0, rem - 1);
            }
            // Role List mode: exhaustion is tracked via _draftedRoleCounts vs MaxCounts

            var faction = GetFaction(roleName);
            if (faction == RoleFaction.Impostor)       _impostorsDrafted++;
            else if (faction == RoleFaction.NeutralKilling) _neutralKillingsDrafted++;
            else if (faction == RoleFaction.Neutral)   _neutralPassivesDrafted++;

            CurrentTurn++;
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();
                DraftUiManager.CloseAll();

                DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

                var recapEntries = BuildRecapEntries();
                DraftNetworkHelper.BroadcastRecap(recapEntries, ShowRecap);

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
                string role = s.ChosenRole ?? "?";
                entries.Add(new RecapEntry(s.SlotNumber, role));
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
                    r.NiceName.Equals(roleName, StringComparison.OrdinalIgnoreCase) ||
                    r.NiceName.Replace(" ", "").Equals(roleName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                if (match != null) roleName = match.NiceName;

                UpCommandRequests.SetRequest(p.Data.PlayerName, roleName);
            }
        }

        public static void SendChatLocal(string msg)
        {
            if (HudManager.Instance?.Chat)
                HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, msg);
        }

        private static void ApplyLocalSettings()
        {
            var opts = MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration       = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap          = opts.ShowRecap;
            AutoStartAfterDraft = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances     = opts.UseRoleChances;
            OfferedRolesCount  = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption   = opts.ShowRandomOption;
            MaxImpostors       = Mathf.Clamp(Mathf.RoundToInt(opts.MaxImpostors), 0, 10);
            MaxNeutralKillings = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralKillings), 0, 10);
            MaxNeutralPassives = Mathf.Clamp(Mathf.RoundToInt(opts.MaxNeutralPassives), 0, 10);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Small helpers
        // ─────────────────────────────────────────────────────────────────────

        private static int GetDraftedCount(string roleName) => _draftedRoleCounts.TryGetValue(roleName, out var count) ? count : 0;
        private static int GetMaxCount(string roleName) => _roleMaxCounts.TryGetValue(roleName, out var count) ? count : 1;
        private static RoleFaction GetFaction(string roleName) => _roleFactions.TryGetValue(roleName, out var faction) ? faction : RoleCategory.GetFaction(roleName);
        private static int GetWeight(string roleName) => _roleWeights.TryGetValue(roleName, out var weight) ? Math.Max(1, weight) : 1;

        private static string PickWeighted(List<string> candidates)
        {
            int total = candidates.Sum(GetWeight);
            if (total <= 0) return candidates[UnityEngine.Random.Range(0, candidates.Count)];
            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc  = 0;
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
            var temp    = new List<string>(candidates);
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
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);

            try { DraftRecapOverlay.Hide(); } catch { }

            bool isHost           = AmongUsClient.Instance.AmHost;
            bool shouldAutoStart  = AutoStartAfterDraft && isHost;

            if (!AutoStartAfterDraft)
            {
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

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

            yield return new WaitForSeconds(0.6f);

            try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
        }
    }
}
