using AmongUs.GameOptions;
using DraftModeTOUM;
using DraftModeTOUM.Patches;
using MiraAPI.GameOptions;
using MiraAPI.Utilities;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Reactor.Utilities;
using UnityEngine;
using TownOfUs.Options;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public class PlayerDraftState
    {
        public byte PlayerId    { get; set; }
        public int  SlotNumber  { get; set; }
        public string? ChosenRole { get; set; }
        public bool HasPicked   { get; set; }
        public bool IsPickingNow { get; set; }
        public List<string> OfferedRoles { get; set; } = new();
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

        // ── Faction caps ──────────────────────────────────────────────────────
        public static int MaxImpostors       { get; private set; } = 2;
        public static int MaxNeutralKillings { get; private set; } = 1;
        public static int MaxNeutralBenign   { get; private set; } = 1;
        public static int MaxNeutralEvil     { get; private set; } = 1;
        public static int MaxNeutralOutlier  { get; private set; } = 1;

        // ── Per-seat chances: index 0 = seat 1 ───────────────────────────────
        private static float[] _impSeatChances          = Array.Empty<float>();
        private static float[] _neutKillSeatChances     = Array.Empty<float>();
        private static float[] _neutBenignSeatChances   = Array.Empty<float>();
        private static float[] _neutEvilSeatChances     = Array.Empty<float>();
        private static float[] _neutOutlierSeatChances  = Array.Empty<float>();

        // ── Crew sub-category chances (global) ────────────────────────────────
        private static float _crewInvestigativeChance = 70f;
        private static float _crewKillingChance       = 70f;
        private static float _crewPowerChance         = 70f;
        private static float _crewProtectiveChance    = 70f;
        private static float _crewSupportChance       = 70f;

        // ── Drafted faction counts ────────────────────────────────────────────
        private static int _impostorsDrafted      = 0;
        private static int _neutKillDrafted       = 0;
        private static int _neutBenignDrafted     = 0;
        private static int _neutEvilDrafted       = 0;
        private static int _neutOutlierDrafted    = 0;

        internal static bool SkipCountdown { get; private set; } = false;

        public static List<int> TurnOrder { get; private set; } = new();

        private static Dictionary<int,   PlayerDraftState> _slotMap          = new();
        private static Dictionary<byte,  int>              _pidToSlot         = new();
        private static List<string>                        _lobbyRolePool     = new();
        private static Dictionary<string, int>             _roleMaxCounts     = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int>             _roleWeights       = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleFaction>    _roleFactions      = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, int>             _draftedRoleCounts = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, RoleBehaviour>  _roleLookup        = new(StringComparer.OrdinalIgnoreCase);

        public static RoleBehaviour? FindRoleInPool(string key) =>
            _roleLookup.TryGetValue(key, out var r) ? r : null;

        public static int GetSlotForPlayer(byte playerId) =>
            _pidToSlot.TryGetValue(playerId, out int s) ? s : -1;

        public static PlayerDraftState? GetStateForSlot(int slot) =>
            _slotMap.TryGetValue(slot, out var s) ? s : null;

        public static PlayerDraftState? GetCurrentPickerState()
        {
            if (!IsDraftActive || CurrentTurn < 1 || CurrentTurn > TurnOrder.Count) return null;
            return GetStateForSlot(TurnOrder[CurrentTurn - 1]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Client-side sync (non-host players)
        // ─────────────────────────────────────────────────────────────────────

        public static void SetClientTurn(int turnNumber, int currentPickerSlot)
        {
            if (AmongUsClient.Instance.AmHost) return;
            CurrentTurn  = turnNumber;
            TurnTimeLeft = TurnDuration;
            foreach (var state in _slotMap.Values)
            {
                state.IsPickingNow = state.SlotNumber == currentPickerSlot;
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
                var state = new PlayerDraftState { PlayerId = playerIds[i], SlotNumber = slotNumbers[i] };
                _slotMap[slotNumbers[i]] = state;
                _pidToSlot[playerIds[i]] = slotNumbers[i];
            }

            TurnOrder    = _slotMap.Keys.OrderBy(s => s).ToList();
            CurrentTurn  = 1;
            TurnTimeLeft = TurnDuration;
            DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Draft start (host only)
        // ─────────────────────────────────────────────────────────────────────

        public static void StartDraft()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;

            DraftTicker.EnsureExists();
            Reset(cancelledBeforeCompletion: true);
            ApplyLocalSettings();

            var players = PlayerControl.AllPlayerControls.ToArray()
                .Where(p => p != null && !p.Data.Disconnected).ToList();

            // Build pool — also loads faction caps + seat chances
            var pool = RolePoolBuilder.BuildPool();
            _lobbyRolePool = pool.Roles;
            _roleMaxCounts = pool.MaxCounts;
            _roleWeights   = pool.Weights;
            _roleFactions  = pool.Factions;
            _roleLookup    = pool.RoleLookup;

            MaxImpostors      = pool.MaxImpostors;
            MaxNeutralKillings = pool.MaxNeutralKillings;
            MaxNeutralBenign  = pool.MaxNeutralBenign;
            MaxNeutralEvil    = pool.MaxNeutralEvil;
            MaxNeutralOutlier = pool.MaxNeutralOutlier;

            _impSeatChances         = pool.ImpSeatChances;
            _neutKillSeatChances    = pool.NeutKillSeatChances;
            _neutBenignSeatChances  = pool.NeutBenignSeatChances;
            _neutEvilSeatChances    = pool.NeutEvilSeatChances;
            _neutOutlierSeatChances = pool.NeutOutlierSeatChances;

            _crewInvestigativeChance = pool.CrewInvestigativeChance;
            _crewKillingChance       = pool.CrewKillingChance;
            _crewPowerChance         = pool.CrewPowerChance;
            _crewProtectiveChance    = pool.CrewProtectiveChance;
            _crewSupportChance       = pool.CrewSupportChance;

            if (_lobbyRolePool.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning("[DraftManager] Empty role pool — aborting draft");
                return;
            }

            int totalSlots    = players.Count;
            var shuffledSlots = Enumerable.Range(1, totalSlots).OrderBy(_ => UnityEngine.Random.value).ToList();
            var syncPids      = new List<byte>();
            var syncSlots     = new List<int>();

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

        // ─────────────────────────────────────────────────────────────────────
        // Reset
        // ─────────────────────────────────────────────────────────────────────

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
            _roleLookup.Clear();
            TurnOrder.Clear();

            _impostorsDrafted       = 0;
            _neutKillDrafted        = 0;
            _neutBenignDrafted      = 0;
            _neutEvilDrafted        = 0;
            _neutOutlierDrafted     = 0;
            _impSeatChances         = Array.Empty<float>();
            _neutKillSeatChances    = Array.Empty<float>();
            _neutBenignSeatChances  = Array.Empty<float>();
            _neutEvilSeatChances    = Array.Empty<float>();
            _neutOutlierSeatChances = Array.Empty<float>();

            if (cancelledBeforeCompletion)
                UpCommandRequests.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Tick
        // ─────────────────────────────────────────────────────────────────────

        public static void Tick(float deltaTime)
        {
            if (!IsDraftActive || !AmongUsClient.Instance.AmHost) return;
            TurnTimeLeft -= deltaTime;
            if (TurnTimeLeft <= 0f) AutoPickRandom();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Offer building
        // ─────────────────────────────────────────────────────────────────────

        private static void OfferRolesToCurrentPicker()
        {
            var state = GetCurrentPickerState();
            if (state == null) return;
            state.IsPickingNow = true;

            var offered = BuildOffersForClassicMode();
            state.OfferedRoles = offered.OrderBy(_ => UnityEngine.Random.value).ToList();

            DraftNetworkHelper.SendTurnAnnouncement(
                state.SlotNumber, state.PlayerId, state.OfferedRoles, CurrentTurn);
        }

        private static bool RollChance(float[] chances, int drafted)
        {
            int seat = drafted + 1;
            if (seat > chances.Length) return false;
            return UnityEngine.Random.Range(0f, 100f) < chances[seat - 1];
        }

        private static List<string> BuildOffersForClassicMode()
        {
            int target = OfferedRolesCount;

            // ── Faction rolls ─────────────────────────────────────────────────
            bool offerImp      = _impostorsDrafted  < MaxImpostors       && RollChance(_impSeatChances,         _impostorsDrafted);
            bool offerNK       = _neutKillDrafted   < MaxNeutralKillings  && RollChance(_neutKillSeatChances,    _neutKillDrafted);
            bool offerNBenign  = _neutBenignDrafted < MaxNeutralBenign    && RollChance(_neutBenignSeatChances,  _neutBenignDrafted);
            bool offerNEvil    = _neutEvilDrafted   < MaxNeutralEvil      && RollChance(_neutEvilSeatChances,    _neutEvilDrafted);
            bool offerNOutlier = _neutOutlierDrafted< MaxNeutralOutlier   && RollChance(_neutOutlierSeatChances, _neutOutlierDrafted);

            DraftModePlugin.Logger.LogInfo(
                $"[DraftManager] Turn {CurrentTurn}: imp={offerImp} nk={offerNK} " +
                $"benign={offerNBenign} evil={offerNEvil} outlier={offerNOutlier}");

            // ── Available role pool (not yet exhausted) ───────────────────────
            var allAvail = _lobbyRolePool
                .Where(r => GetDraftedCount(r) < GetMaxCount(r))
                .ToList();

            // ── Faction role lists ────────────────────────────────────────────
            var impRoles     = offerImp      ? allAvail.Where(r => GetFaction(r) == RoleFaction.Impostor).ToList()       : new();
            var nkRoles      = offerNK       ? allAvail.Where(r => GetFaction(r) == RoleFaction.NeutralKilling).ToList() : new();
            var nBenignRoles = offerNBenign  ? allAvail.Where(r => GetFaction(r) == RoleFaction.NeutralBenign).ToList()  : new();
            var nEvilRoles   = offerNEvil    ? allAvail.Where(r => GetFaction(r) == RoleFaction.NeutralEvil).ToList()    : new();
            var nOutRoles    = offerNOutlier ? allAvail.Where(r => GetFaction(r) == RoleFaction.NeutralOutlier).ToList() : new();

            var offered = new List<string>();
            if (impRoles.Count     > 0) offered.AddRange(PickWeightedUnique(impRoles,     1));
            if (nkRoles.Count      > 0) offered.AddRange(PickWeightedUnique(nkRoles,      1));
            if (nBenignRoles.Count > 0) offered.AddRange(PickWeightedUnique(nBenignRoles, 1));
            if (nEvilRoles.Count   > 0) offered.AddRange(PickWeightedUnique(nEvilRoles,   1));
            if (nOutRoles.Count    > 0) offered.AddRange(PickWeightedUnique(nOutRoles,    1));

            // ── Fill remainder with crew ──────────────────────────────────────
            int crewNeeded = target - offered.Count;
            if (crewNeeded > 0)
            {
                var crewPool = BuildCrewPool(allAvail, offered);
                offered.AddRange(PickWeightedUnique(crewPool, crewNeeded));
            }

            // Pad if still short
            if (offered.Count < target)
            {
                var extras = allAvail.Where(r => !offered.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();
                offered.AddRange(PickWeightedUnique(extras, target - offered.Count));
            }

            if (offered.Count == 0) offered.Add("Crewmate");
            return offered;
        }

        // Builds a crew list weighted toward sub-categories via their chance settings.
        private static List<string> BuildCrewPool(List<string> allAvail, List<string> excluded)
        {
            var crew = allAvail.Where(r =>
            {
                var f = GetFaction(r);
                return f == RoleFaction.Crewmate || f == RoleFaction.CrewInvestigative ||
                       f == RoleFaction.CrewKilling || f == RoleFaction.CrewPower ||
                       f == RoleFaction.CrewProtective || f == RoleFaction.CrewSupport;
            }).Where(r => !excluded.Any(o => o.Equals(r, StringComparison.OrdinalIgnoreCase))).ToList();

            if (crew.Count == 0) return crew;

            // Assign boosted weights to crew roles based on sub-category chances
            // (the chances act as multipliers on top of the TOU role weight).
            var weightedCrew = new List<string>();
            foreach (var r in crew)
            {
                var f = GetFaction(r);
                float chanceBoost = f switch
                {
                    RoleFaction.CrewInvestigative => _crewInvestigativeChance,
                    RoleFaction.CrewKilling       => _crewKillingChance,
                    RoleFaction.CrewPower         => _crewPowerChance,
                    RoleFaction.CrewProtective    => _crewProtectiveChance,
                    RoleFaction.CrewSupport       => _crewSupportChance,
                    _                             => 50f, // plain crewmate
                };
                // Add the role once per 10% chance (rough weighting, clamped 1-10)
                int copies = Math.Max(1, (int)(chanceBoost / 10f));
                for (int i = 0; i < copies; i++) weightedCrew.Add(r);
            }
            return weightedCrew;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pick submission
        // ─────────────────────────────────────────────────────────────────────

        public static bool SubmitPick(byte playerId, int choiceIndex)
        {
            if (!AmongUsClient.Instance.AmHost || !IsDraftActive) return false;
            var state = GetCurrentPickerState();
            if (state == null || state.PlayerId != playerId || state.HasPicked) return false;

            string chosen = choiceIndex >= state.OfferedRoles.Count
                ? PickFullRandom()
                : state.OfferedRoles[choiceIndex];

            FinalisePickForCurrentSlot(chosen);
            return true;
        }

        private static void AutoPickRandom()
        {
            var state = GetCurrentPickerState();
            if (!ShowRandomOption && state != null && state.OfferedRoles.Count > 0)
                FinalisePickForCurrentSlot(state.OfferedRoles[UnityEngine.Random.Range(0, state.OfferedRoles.Count)]);
            else
                FinalisePickForCurrentSlot(PickFullRandom());
        }

        private static string PickFullRandom()
        {
            var avail = _lobbyRolePool.Where(r => GetDraftedCount(r) < GetMaxCount(r)).ToList();
            if (avail.Count == 0) return "Crewmate";
            return UseRoleChances ? PickWeighted(avail) : avail[UnityEngine.Random.Range(0, avail.Count)];
        }

        private static void FinalisePickForCurrentSlot(string roleName)
        {
            var state = GetCurrentPickerState();
            if (state == null) return;

            state.ChosenRole   = roleName;
            state.HasPicked    = true;
            state.IsPickingNow = false;
            _draftedRoleCounts[roleName] = GetDraftedCount(roleName) + 1;

            var faction = GetFaction(roleName);
            switch (faction)
            {
                case RoleFaction.Impostor:       _impostorsDrafted++;   break;
                case RoleFaction.NeutralKilling: _neutKillDrafted++;    break;
                case RoleFaction.NeutralBenign:  _neutBenignDrafted++;  break;
                case RoleFaction.NeutralEvil:    _neutEvilDrafted++;    break;
                case RoleFaction.NeutralOutlier: _neutOutlierDrafted++; break;
            }

            CurrentTurn++;
            DraftUiManager.RefreshTurnList();

            if (CurrentTurn > TurnOrder.Count)
            {
                IsDraftActive = false;
                ApplyAllRoles();
                DraftUiManager.CloseAll();
                DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

                var recap = BuildRecapEntries();
                DraftNetworkHelper.BroadcastRecap(recap, ShowRecap);
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
                entries.Add(new RecapEntry(s.SlotNumber, s.ChosenRole ?? "?"));
            }
            return entries;
        }

        private static void ApplyAllRoles()
        {
            var allRoles = MiscUtils.AllRegisteredRoles.ToArray();
            foreach (var state in _slotMap.Values)
            {
                if (state.PlayerId >= 200 || state.ChosenRole == null) continue;
                var p = PlayerControl.AllPlayerControls.ToArray()
                    .FirstOrDefault(x => x.PlayerId == state.PlayerId);
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
            var opts = OptionGroupSingleton<DraftModeOptions>.Instance;
            TurnDuration          = Mathf.Clamp(opts.TurnDurationSeconds, 5f, 60f);
            ShowRecap             = opts.ShowRecap;
            AutoStartAfterDraft   = opts.AutoStartAfterDraft;
            LockLobbyOnDraftStart = opts.LockLobbyOnDraftStart;
            UseRoleChances        = opts.UseRoleChances;
            OfferedRolesCount     = Mathf.Clamp(Mathf.RoundToInt(opts.OfferedRolesCount), 1, 9);
            ShowRandomOption      = opts.ShowRandomOption;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Small helpers
        // ─────────────────────────────────────────────────────────────────────

        private static int GetDraftedCount(string r) =>
            _draftedRoleCounts.TryGetValue(r, out var c) ? c : 0;
        private static int GetMaxCount(string r) =>
            _roleMaxCounts.TryGetValue(r, out var c) ? c : 1;
        private static RoleFaction GetFaction(string r) =>
            _roleFactions.TryGetValue(r, out var f) ? f : RoleCategory.GetFaction(r);
        private static int GetWeight(string r) =>
            _roleWeights.TryGetValue(r, out var w) ? Math.Max(1, w) : 1;

        private static string PickWeighted(List<string> pool)
        {
            int total = pool.Sum(GetWeight);
            if (total <= 0) return pool[UnityEngine.Random.Range(0, pool.Count)];
            int roll = UnityEngine.Random.Range(1, total + 1);
            int acc  = 0;
            foreach (var r in pool)
            {
                acc += GetWeight(r);
                if (roll <= acc) return r;
            }
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private static List<string> PickWeightedUnique(List<string> pool, int count)
        {
            var results = new List<string>();
            var temp    = new List<string>(pool);
            // Deduplicate (pool may have repeated entries from crew weighting)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (results.Count < count && temp.Count > 0)
            {
                var pick = UseRoleChances ? PickWeighted(temp) : temp[UnityEngine.Random.Range(0, temp.Count)];
                if (seen.Add(pick)) results.Add(pick);
                temp.RemoveAll(r => r.Equals(pick, StringComparison.OrdinalIgnoreCase));
            }
            return results;
        }

        // ─────────────────────────────────────────────────────────────────────
        // End-of-draft sequence
        // ─────────────────────────────────────────────────────────────────────

        public static void TriggerEndDraftSequence() => Coroutines.Start(CoEndDraftSequence());

        private static IEnumerator CoEndDraftSequence()
        {
            yield return new WaitForSeconds(ShowRecap ? 5.0f : 0.5f);
            try { DraftRecapOverlay.Hide(); } catch { }

            if (!AutoStartAfterDraft)
            {
                try { DraftStatusOverlay.SetState(OverlayState.Hidden); } catch { }
                yield break;
            }

            if (AmongUsClient.Instance.AmHost && GameStartManager.Instance != null
                && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined)
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
