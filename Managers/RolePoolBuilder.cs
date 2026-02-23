using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using MiraAPI.GameOptions;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Options;
using TownOfUs.Roles;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    // DraftSlot kept as a stub so any remaining DraftManager references compile.
    public sealed class DraftSlot
    {
        public RoleListOption Bucket    { get; }
        public List<string>   ValidRoles { get; }
        public DraftSlot(RoleListOption bucket, List<string> validRoles)
        {
            Bucket     = bucket;
            ValidRoles = validRoles;
        }
    }

    public sealed class DraftRolePool
    {
        // ── Flat role data ────────────────────────────────────────────────────
        public List<string>                      Roles      { get; } = new();
        public Dictionary<string, int>           MaxCounts  { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int>           Weights    { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleFaction>   Factions   { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleBehaviour> RoleLookup { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool            IsRoleListMode { get; set; } = false;
        public List<DraftSlot> Slots          { get; } = new();

        // ── Faction caps ──────────────────────────────────────────────────────
        public int MaxImpostors       { get; set; } = 2;
        public int MaxNeutralKillings { get; set; } = 1;
        public int MaxNeutralBenign   { get; set; } = 1;
        public int MaxNeutralEvil     { get; set; } = 1;
        public int MaxNeutralOutlier  { get; set; } = 1;

        // ── Per-seat chances ──────────────────────────────────────────────────
        public float[] ImpSeatChances         { get; set; } = Array.Empty<float>();
        public float[] NeutKillSeatChances    { get; set; } = Array.Empty<float>();
        public float[] NeutBenignSeatChances  { get; set; } = Array.Empty<float>();
        public float[] NeutEvilSeatChances    { get; set; } = Array.Empty<float>();
        public float[] NeutOutlierSeatChances { get; set; } = Array.Empty<float>();

        // ── Crew sub-category chances (global, not per-seat) ──────────────────
        public float CrewInvestigativeChance { get; set; } = 70f;
        public float CrewKillingChance       { get; set; } = 70f;
        public float CrewPowerChance         { get; set; } = 70f;
        public float CrewProtectiveChance    { get; set; } = 70f;
        public float CrewSupportChance       { get; set; } = 70f;

        // Legacy shim so DraftManager compiles without changes
        [Obsolete("Use MaxNeutralBenign/Evil/Outlier instead")]
        public int MaxNeutralOther => MaxNeutralBenign + MaxNeutralEvil + MaxNeutralOutlier;
        [Obsolete("Use NeutBenign/Evil/OutlierSeatChances instead")]
        public float[] NeutOtherSeatChances => NeutBenignSeatChances;
    }

    public static class RolePoolBuilder
    {
        public static DraftRolePool BuildPool()
        {
            var pool = new DraftRolePool();

            try
            {
                BuildFromRoleOptions(pool);
                LoadFactionSettings(pool);

                DraftModePlugin.Logger.LogInfo(
                    $"[RolePoolBuilder] {pool.Roles.Count} roles | " +
                    $"Imp:{pool.MaxImpostors} NK:{pool.MaxNeutralKillings} " +
                    $"NB:{pool.MaxNeutralBenign} NE:{pool.MaxNeutralEvil} NO:{pool.MaxNeutralOutlier}");
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[RolePoolBuilder] Build failed: {ex.Message}");
            }

            if (pool.Roles.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning("[RolePoolBuilder] No roles found — using fallback");
                foreach (var r in FallbackRoles())
                    AddRole(pool, r, null!, 1, 100, RoleCategory.GetFaction(r));
            }

            return pool;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Load faction caps + per-seat chances from DraftRoleListOptions
        // ─────────────────────────────────────────────────────────────────────
        private static void LoadFactionSettings(DraftRolePool pool)
        {
            var imp     = OptionGroupSingleton<DraftImpOptions>.Instance;
            var nk      = OptionGroupSingleton<DraftNeutKillOptions>.Instance;
            var benign  = OptionGroupSingleton<DraftNeutBenignOptions>.Instance;
            var evil    = OptionGroupSingleton<DraftNeutEvilOptions>.Instance;
            var outlier = OptionGroupSingleton<DraftNeutOutlierOptions>.Instance;
            var crew    = OptionGroupSingleton<DraftCrewOptions>.Instance;

            if (imp == null || nk == null || benign == null || evil == null || outlier == null || crew == null)
            {
                DraftModePlugin.Logger.LogWarning("[RolePoolBuilder] One or more option groups not ready — using defaults");
                return;
            }

            pool.MaxImpostors       = Mathf.RoundToInt(imp.MaxImpostors);
            pool.MaxNeutralKillings = Mathf.RoundToInt(nk.MaxNeutralKillings);
            pool.MaxNeutralBenign   = Mathf.RoundToInt(benign.MaxNeutralBenign);
            pool.MaxNeutralEvil     = Mathf.RoundToInt(evil.MaxNeutralEvil);
            pool.MaxNeutralOutlier  = Mathf.RoundToInt(outlier.MaxNeutralOutlier);

            pool.ImpSeatChances = new float[pool.MaxImpostors];
            for (int i = 0; i < pool.MaxImpostors; i++)
                pool.ImpSeatChances[i] = imp.GetChance(i + 1);

            pool.NeutKillSeatChances = new float[pool.MaxNeutralKillings];
            for (int i = 0; i < pool.MaxNeutralKillings; i++)
                pool.NeutKillSeatChances[i] = nk.GetChance(i + 1);

            pool.NeutBenignSeatChances = new float[pool.MaxNeutralBenign];
            for (int i = 0; i < pool.MaxNeutralBenign; i++)
                pool.NeutBenignSeatChances[i] = benign.GetChance(i + 1);

            pool.NeutEvilSeatChances = new float[pool.MaxNeutralEvil];
            for (int i = 0; i < pool.MaxNeutralEvil; i++)
                pool.NeutEvilSeatChances[i] = evil.GetChance(i + 1);

            pool.NeutOutlierSeatChances = new float[pool.MaxNeutralOutlier];
            for (int i = 0; i < pool.MaxNeutralOutlier; i++)
                pool.NeutOutlierSeatChances[i] = outlier.GetChance(i + 1);

            pool.CrewInvestigativeChance = crew.CrewInvestigativeChance.Value;
            pool.CrewKillingChance       = crew.CrewKillingChance.Value;
            pool.CrewPowerChance         = crew.CrewPowerChance.Value;
            pool.CrewProtectiveChance    = crew.CrewProtectiveChance.Value;
            pool.CrewSupportChance       = crew.CrewSupportChance.Value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Collect all enabled roles from vanilla + TOU role settings
        // ─────────────────────────────────────────────────────────────────────
        private static void BuildFromRoleOptions(DraftRolePool pool)
        {
            var roleOpts = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
            if (roleOpts == null) return;

            foreach (var role in MiscUtils.AllRegisteredRoles.ToArray())
            {
                if (role == null) continue;
                if (!CustomRoleUtils.CanSpawnOnCurrentMode(role)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;
                if (role is ICustomRole cr && (cr.Configuration.HideSettings || !cr.VisibleInSettings())) continue;

                var canonical = GetCanonicalName(role);
                if (IsBannedRole(canonical)) continue;

                int count  = roleOpts.GetNumPerGame(role.Role);
                int chance = roleOpts.GetChancePerGame(role.Role);
                if (count <= 0 || chance <= 0) continue;

                AddRole(pool, canonical, role, count, chance, GetFactionFromRole(role));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        public static string GetCanonicalName(RoleBehaviour role)
        {
            if (role is ITownOfUsRole touRole) return touRole.LocaleKey;
            var t = role.GetType().Name;
            if (t.EndsWith("Role", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
            return t;
        }

        private static RoleFaction GetFactionFromRole(RoleBehaviour role)
        {
            if (role is ITownOfUsRole touRole)
            {
                return touRole.RoleAlignment switch
                {
                    RoleAlignment.NeutralKilling         => RoleFaction.NeutralKilling,
                    RoleAlignment.NeutralBenign          => RoleFaction.NeutralBenign,
                    RoleAlignment.NeutralEvil            => RoleFaction.NeutralEvil,
                    RoleAlignment.NeutralOutlier         => RoleFaction.NeutralOutlier,
                    RoleAlignment.ImpostorConcealing or RoleAlignment.ImpostorKilling
                        or RoleAlignment.ImpostorPower or RoleAlignment.ImpostorSupport => RoleFaction.Impostor,
                    RoleAlignment.CrewmateInvestigative  => RoleFaction.CrewInvestigative,
                    RoleAlignment.CrewmateKilling        => RoleFaction.CrewKilling,
                    RoleAlignment.CrewmatePower          => RoleFaction.CrewPower,
                    RoleAlignment.CrewmateProtective     => RoleFaction.CrewProtective,
                    RoleAlignment.CrewmateSupport        => RoleFaction.CrewSupport,
                    _                                    => RoleFaction.Crewmate,
                };
            }
            // Fallback for non-TOU roles: use canonical name lookup
            if (role.IsImpostor()) return RoleFaction.Impostor;
            return RoleCategory.GetFaction(GetCanonicalName(role));
        }

        private static readonly HashSet<string> _bannedRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Haunter", "Spectre", "Teleporter", "Pestilence", "Traitor", "Mayor"
        };

        public static bool IsBannedRole(string roleName) => _bannedRoles.Contains(roleName);

        private static void AddRole(DraftRolePool pool, string name, RoleBehaviour role,
            int maxCount, int weight, RoleFaction faction)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!pool.MaxCounts.ContainsKey(name))
            {
                pool.Roles.Add(name);
                pool.MaxCounts[name]  = Math.Max(1, maxCount);
                pool.Weights[name]    = Math.Max(1, weight);
                pool.Factions[name]   = faction;
                pool.RoleLookup[name] = role;
            }
            else
            {
                pool.MaxCounts[name] = Math.Max(pool.MaxCounts[name], maxCount);
                pool.Weights[name]   = Math.Max(pool.Weights[name], weight);
            }
        }

        private static IEnumerable<string> FallbackRoles() => new[]
        {
            "Aurial","Forensic","Lookout","Mystic","Seer","Snitch","Sonar","Trapper",
            "Deputy","Sheriff","Veteran","Vigilante","Jailor","Monarch","Politician",
            "Prosecutor","Swapper","TimeLord","Altruist","Cleric","Medic","Mirrorcaster",
            "Oracle","Warden","EngineerTou","Imitator","Medium","Plumber","Sentry",
            "Transporter","Eclipsal","Escapist","Grenadier","Morphling","Swooper","Venerer",
            "Ambusher","Bomber","Parasite","Scavenger","Warlock","Ambassador","Puppeteer",
            "Spellslinger","Blackmailer","Hypnotist","Janitor","Miner","Undertaker",
            "Fairy","Mercenary","Survivor","Doomsayer","Executioner","Jester",
            "Arsonist","Glitch","Juggernaut","Plaguebearer","SoulCollector",
            "Vampire","Werewolf","Chef","Inquisitor"
        };
    }
}
