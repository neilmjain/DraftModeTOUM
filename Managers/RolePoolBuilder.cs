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

namespace DraftModeTOUM.Managers
{
    // ─────────────────────────────────────────────────────────────────────────
    // DraftSlot — represents one player's draft slot when Role List mode is on.
    // The SlotBucket tells us what kind of roles are valid for that slot.
    // ─────────────────────────────────────────────────────────────────────────
    public sealed class DraftSlot
    {
        public RoleListOption Bucket { get; }
        public List<string>   ValidRoles { get; }   // canonical locale keys

        public DraftSlot(RoleListOption bucket, List<string> validRoles)
        {
            Bucket     = bucket;
            ValidRoles = validRoles;
        }
    }

    public sealed class DraftRolePool
    {
        // ── Classic / vanilla mode fields ────────────────────────────────────
        public List<string> Roles { get; } = new List<string>();
        public Dictionary<string, int>          MaxCounts  { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int>          Weights    { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleFaction>  Factions   { get; } = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleBehaviour> RoleLookup { get; } = new Dictionary<string, RoleBehaviour>(StringComparer.OrdinalIgnoreCase);

        // ── Role List mode fields ────────────────────────────────────────────
        /// <summary>True when TOU's Role Assignment Type is set to "Role List".</summary>
        public bool IsRoleListMode { get; set; }

        /// <summary>
        /// One entry per player slot (index 0 = first player to draft).
        /// Only populated when IsRoleListMode is true.
        /// </summary>
        public List<DraftSlot> Slots { get; } = new List<DraftSlot>();
    }

    public static class RolePoolBuilder
    {
        // ─────────────────────────────────────────────────────────────────────
        // Entry point
        // ─────────────────────────────────────────────────────────────────────
        public static DraftRolePool BuildPool()
        {
            var pool = new DraftRolePool();

            try
            {
                // Check if TOU's Role List mode is active
                var roleOpts = OptionGroupSingleton<RoleOptions>.Instance;
                var distribution = roleOpts?.CurrentRoleDistribution() ?? RoleDistribution.Vanilla;

                if (distribution == RoleDistribution.RoleList)
                {
                    pool.IsRoleListMode = true;
                    BuildFromRoleList(pool, roleOpts!);
                    DraftModePlugin.Logger.LogInfo(
                        $"[RolePoolBuilder] Role List mode — {pool.Slots.Count} slots built");
                }
                else
                {
                    pool.IsRoleListMode = false;
                    BuildFromRoleOptions(pool);
                    DraftModePlugin.Logger.LogInfo(
                        $"[RolePoolBuilder] Classic mode — {pool.Roles.Count} enabled roles");
                }
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RolePoolBuilder] Failed building pool: {ex.Message}");
            }

            // Fallback for classic mode only
            if (!pool.IsRoleListMode && pool.Roles.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning(
                    "[RolePoolBuilder] No enabled roles detected — using fallback role list");
                foreach (var roleName in GetFallbackRoles())
                    AddRole(pool, roleName, null!, 1, 100, RoleCategory.GetFaction(roleName));
            }

            return pool;
        }

        // ─────────────────────────────────────────────────────────────────────
        // ROLE LIST MODE: build per-slot data from TOU's Slot1-15 options
        // ─────────────────────────────────────────────────────────────────────
        private static void BuildFromRoleList(DraftRolePool pool, RoleOptions roleOpts)
        {
            // 1. Collect all enabled roles (count > 0 && chance > 0) grouped by alignment
            //    Also populate pool.Roles / Weights / Factions / RoleLookup for UI lookups.
            var byAlignment = new Dictionary<RoleAlignment, List<string>>();

            var gameRoleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
            var registeredRoles = MiscUtils.AllRegisteredRoles.ToArray();

            foreach (var role in registeredRoles)
            {
                if (role == null) continue;
                if (!CustomRoleUtils.CanSpawnOnCurrentMode(role)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;
                if (role is ICustomRole customRole && (customRole.Configuration.HideSettings || !customRole.VisibleInSettings()))
                    continue;

                string canonical = GetCanonicalName(role);
                if (IsBannedRole(canonical)) continue;

                // In Role List mode, TOU uses roles that are enabled (count>0, chance>0)
                int count  = gameRoleOptions?.GetNumPerGame(role.Role)  ?? 0;
                int chance = gameRoleOptions?.GetChancePerGame(role.Role) ?? 0;
                if (count <= 0 || chance <= 0) continue;

                if (role is ITownOfUsRole touRole)
                {
                    var alignment = touRole.RoleAlignment;
                    if (!byAlignment.ContainsKey(alignment))
                        byAlignment[alignment] = new List<string>();
                    if (!byAlignment[alignment].Contains(canonical))
                        byAlignment[alignment].Add(canonical);
                }

                // Register in the flat pool too (for UI / lookup)
                var faction = GetFactionFromRole(role);
                AddRole(pool, canonical, role, count, chance, faction);
            }

            // 2. Build the slot list from Slot1–15 options
            var slotOptions = new[]
            {
                roleOpts.Slot1,  roleOpts.Slot2,  roleOpts.Slot3,
                roleOpts.Slot4,  roleOpts.Slot5,  roleOpts.Slot6,
                roleOpts.Slot7,  roleOpts.Slot8,  roleOpts.Slot9,
                roleOpts.Slot10, roleOpts.Slot11, roleOpts.Slot12,
                roleOpts.Slot13, roleOpts.Slot14, roleOpts.Slot15
            };

            foreach (var slotOpt in slotOptions)
            {
                var bucket     = (RoleListOption)slotOpt.Value;
                var validRoles = GetRolesForBucket(bucket, byAlignment, pool);
                pool.Slots.Add(new DraftSlot(bucket, validRoles));
            }
        }

        /// <summary>
        /// Returns the list of canonical role names that are valid for a given Role List bucket,
        /// based on which roles are actually enabled in the role settings.
        /// </summary>
        private static List<string> GetRolesForBucket(
            RoleListOption bucket,
            Dictionary<RoleAlignment, List<string>> byAlignment,
            DraftRolePool pool)
        {
            List<string> GetA(RoleAlignment a) =>
                byAlignment.TryGetValue(a, out var l) ? l : new List<string>();

            switch (bucket)
            {
                // ── Crewmate ─────────────────────────────────────────────────
                case RoleListOption.CrewInvest:
                    return GetA(RoleAlignment.CrewmateInvestigative);

                case RoleListOption.CrewKilling:
                    return GetA(RoleAlignment.CrewmateKilling);

                case RoleListOption.CrewProtective:
                    return GetA(RoleAlignment.CrewmateProtective);

                case RoleListOption.CrewPower:
                    return GetA(RoleAlignment.CrewmatePower);

                case RoleListOption.CrewSupport:
                    return GetA(RoleAlignment.CrewmateSupport);

                case RoleListOption.CrewCommon:
                    return Concat(
                        GetA(RoleAlignment.CrewmateInvestigative),
                        GetA(RoleAlignment.CrewmateProtective),
                        GetA(RoleAlignment.CrewmateSupport));

                case RoleListOption.CrewSpecial:
                    return Concat(
                        GetA(RoleAlignment.CrewmateKilling),
                        GetA(RoleAlignment.CrewmatePower));

                case RoleListOption.CrewRandom:
                    return Concat(
                        GetA(RoleAlignment.CrewmateInvestigative),
                        GetA(RoleAlignment.CrewmateKilling),
                        GetA(RoleAlignment.CrewmateProtective),
                        GetA(RoleAlignment.CrewmatePower),
                        GetA(RoleAlignment.CrewmateSupport));

                // ── Impostor ─────────────────────────────────────────────────
                case RoleListOption.ImpConceal:
                    return GetA(RoleAlignment.ImpostorConcealing);

                case RoleListOption.ImpKilling:
                    return GetA(RoleAlignment.ImpostorKilling);

                case RoleListOption.ImpPower:
                    return GetA(RoleAlignment.ImpostorPower);

                case RoleListOption.ImpSupport:
                    return GetA(RoleAlignment.ImpostorSupport);

                case RoleListOption.ImpCommon:
                    return Concat(
                        GetA(RoleAlignment.ImpostorConcealing),
                        GetA(RoleAlignment.ImpostorSupport));

                case RoleListOption.ImpSpecial:
                    return Concat(
                        GetA(RoleAlignment.ImpostorKilling),
                        GetA(RoleAlignment.ImpostorPower));

                case RoleListOption.ImpRandom:
                    return Concat(
                        GetA(RoleAlignment.ImpostorConcealing),
                        GetA(RoleAlignment.ImpostorKilling),
                        GetA(RoleAlignment.ImpostorPower),
                        GetA(RoleAlignment.ImpostorSupport));

                // ── Neutral ───────────────────────────────────────────────────
                case RoleListOption.NeutBenign:
                    return GetA(RoleAlignment.NeutralBenign);

                case RoleListOption.NeutEvil:
                    return GetA(RoleAlignment.NeutralEvil);

                case RoleListOption.NeutKilling:
                    return GetA(RoleAlignment.NeutralKilling);

                case RoleListOption.NeutOutlier:
                    return GetA(RoleAlignment.NeutralOutlier);

                case RoleListOption.NeutCommon:
                    return Concat(
                        GetA(RoleAlignment.NeutralBenign),
                        GetA(RoleAlignment.NeutralEvil));

                case RoleListOption.NeutSpecial:
                    return Concat(
                        GetA(RoleAlignment.NeutralKilling),
                        GetA(RoleAlignment.NeutralOutlier));

                case RoleListOption.NeutWildcard:
                    return Concat(
                        GetA(RoleAlignment.NeutralBenign),
                        GetA(RoleAlignment.NeutralEvil),
                        GetA(RoleAlignment.NeutralOutlier));

                case RoleListOption.NeutRandom:
                    return Concat(
                        GetA(RoleAlignment.NeutralBenign),
                        GetA(RoleAlignment.NeutralEvil),
                        GetA(RoleAlignment.NeutralKilling),
                        GetA(RoleAlignment.NeutralOutlier));

                // ── Cross-faction ─────────────────────────────────────────────
                case RoleListOption.NonImp:
                    // Any non-impostor: all crew + all neutral
                    return pool.Roles
                        .Where(r => pool.Factions.TryGetValue(r, out var f) && f != RoleFaction.Impostor)
                        .ToList();

                case RoleListOption.Any:
                    return pool.Roles.ToList();

                default:
                    // Fallback — return everything
                    return pool.Roles.ToList();
            }
        }

        private static List<string> Concat(params List<string>[] lists)
        {
            var result = new List<string>();
            foreach (var l in lists)
                foreach (var r in l)
                    if (!result.Contains(r))
                        result.Add(r);
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // CLASSIC MODE: read from vanilla RoleOptions counts / chances
        // ─────────────────────────────────────────────────────────────────────
        private static void BuildFromRoleOptions(DraftRolePool pool)
        {
            var roleOptions = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
            if (roleOptions == null) return;

            var roles = MiscUtils.AllRegisteredRoles.ToArray();

            foreach (var role in roles)
            {
                if (role == null) continue;
                if (!CustomRoleUtils.CanSpawnOnCurrentMode(role)) continue;
                if (role.Role is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost or RoleTypes.GuardianAngel) continue;

                if (role is ICustomRole customRole)
                {
                    if (customRole.Configuration.HideSettings || !customRole.VisibleInSettings())
                        continue;
                }

                string canonicalName = GetCanonicalName(role);
                if (IsBannedRole(canonicalName)) continue;

                int count  = roleOptions.GetNumPerGame(role.Role);
                int chance = roleOptions.GetChancePerGame(role.Role);
                if (count <= 0 || chance <= 0) continue;

                var faction = GetFactionFromRole(role);
                AddRole(pool, canonicalName, role, count, chance, faction);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers shared by both modes
        // ─────────────────────────────────────────────────────────────────────

        public static string GetCanonicalName(RoleBehaviour role)
        {
            if (role is ITownOfUsRole touRole)
                return touRole.LocaleKey;

            var typeName = role.GetType().Name;
            if (typeName.EndsWith("Role", StringComparison.OrdinalIgnoreCase))
                typeName = typeName[..^4];
            return typeName;
        }

        private static RoleFaction GetFactionFromRole(RoleBehaviour role)
        {
            if (role is ITownOfUsRole touRole)
            {
                return touRole.RoleAlignment switch
                {
                    RoleAlignment.NeutralKilling => RoleFaction.NeutralKilling,
                    RoleAlignment.NeutralBenign or
                    RoleAlignment.NeutralEvil or
                    RoleAlignment.NeutralOutlier => RoleFaction.Neutral,
                    RoleAlignment.ImpostorConcealing or
                    RoleAlignment.ImpostorKilling or
                    RoleAlignment.ImpostorPower or
                    RoleAlignment.ImpostorSupport => RoleFaction.Impostor,
                    _ => RoleFaction.Crewmate
                };
            }

            if (role.IsImpostor()) return RoleFaction.Impostor;
            if (role.IsNeutral())  return RoleFaction.Neutral;
            return RoleFaction.Crewmate;
        }

        private static readonly HashSet<string> _bannedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Haunter", "Spectre", "Teleporter", "Pestilence", "Traitor", "Mayor"
        };

        public static bool IsBannedRole(string roleName) => _bannedRoles.Contains(roleName);

        private static void AddRole(DraftRolePool pool, string roleName, RoleBehaviour role, int maxCount, int weight, RoleFaction faction)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return;
            if (!pool.MaxCounts.ContainsKey(roleName))
            {
                pool.Roles.Add(roleName);
                pool.MaxCounts[roleName]  = Math.Max(1, maxCount);
                pool.Weights[roleName]    = Math.Max(1, weight);
                pool.Factions[roleName]   = faction;
                pool.RoleLookup[roleName] = role;
            }
            else
            {
                pool.MaxCounts[roleName] = Math.Max(pool.MaxCounts[roleName], maxCount);
                pool.Weights[roleName]   = Math.Max(pool.Weights[roleName], weight);
            }
        }

        private static IEnumerable<string> GetFallbackRoles()
        {
            return new[]
            {
                "Aurial","Forensic","Lookout","Mystic","Seer",
                "Snitch","Sonar","Trapper","Deputy","Sheriff",
                "Veteran","Vigilante","Jailor","Monarch","Politician",
                "Prosecutor","Swapper","TimeLord","Altruist","Cleric","Medic",
                "Mirrorcaster","Oracle","Warden","EngineerTou","Imitator","Medium",
                "Plumber","Sentry","Transporter","Eclipsal","Escapist","Grenadier",
                "Morphling","Swooper","Venerer","Ambusher","Bomber","Parasite",
                "Scavenger","Warlock","Ambassador","Puppeteer","Spellslinger",
                "Blackmailer","Hypnotist","Janitor","Miner","Undertaker",
                "Fairy","Mercenary","Survivor","Doomsayer","Executioner","Jester",
                "Arsonist","Glitch","Juggernaut","Plaguebearer",
                "SoulCollector","Vampire","Werewolf","Chef","Inquisitor"
            };
        }
    }
}
