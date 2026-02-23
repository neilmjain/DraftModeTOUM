using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Roles;
using TownOfUs.Utilities;

namespace DraftModeTOUM.Managers
{
    public sealed class DraftRolePool
    {
        public List<string> Roles { get; } = new List<string>();
        public Dictionary<string, int> MaxCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Weights { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RoleFaction> Factions { get; } = new Dictionary<string, RoleFaction>(StringComparer.OrdinalIgnoreCase);

        // Maps canonical locale key → RoleBehaviour so we can look up the role
        // for icons/colours without going through the (potentially renamed) NiceName.
        public Dictionary<string, RoleBehaviour> RoleLookup { get; } = new Dictionary<string, RoleBehaviour>(StringComparer.OrdinalIgnoreCase);
    }

    public static class RolePoolBuilder
    {
        public static DraftRolePool BuildPool()
        {
            var pool = new DraftRolePool();

            try
            {
                BuildFromRoleOptions(pool);
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning(
                    $"[RolePoolBuilder] Failed reading role options: {ex.Message}");
            }

            if (pool.Roles.Count == 0)
            {
                DraftModePlugin.Logger.LogWarning(
                    "[RolePoolBuilder] No enabled roles detected — using fallback role list");

                foreach (var roleName in GetAllRoles())
                {
                    AddRole(pool, roleName, 1, 100, RoleCategory.GetFaction(roleName));
                }
            }

            DraftModePlugin.Logger.LogInfo(
                $"[RolePoolBuilder] Found {pool.Roles.Count} enabled roles");

            return pool;
        }

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

                // Use the canonical LocaleKey (e.g. "SoulCollector") as the role identifier.
                // This is immune to name-change mods because it never goes through the
                // localization / RoleName property that name-change mods override.
                string canonicalName = GetCanonicalName(role);

                // Hard-banned roles — never appear in draft regardless of settings
                if (IsBannedRole(canonicalName)) continue;

                int count = roleOptions.GetNumPerGame(role.Role);
                int chance = roleOptions.GetChancePerGame(role.Role);
                if (count <= 0 || chance <= 0) continue;

                // Determine faction directly from the role object — never from the name string.
                var faction = GetFactionFromRole(role);

                AddRole(pool, canonicalName, role, count, chance, faction);
            }
        }

        /// <summary>
        /// Returns the canonical name for a role that is stable regardless of locale or
        /// name-change mods.  For TOU roles this is ITownOfUsRole.LocaleKey (e.g. "SoulCollector").
        /// For everything else we fall back to the C# type name, which is also stable.
        /// </summary>
        public static string GetCanonicalName(RoleBehaviour role)
        {
            if (role is ITownOfUsRole touRole)
                return touRole.LocaleKey;

            // Fallback: strip the "Role" suffix from the type name (e.g. "SheriffRole" → "Sheriff")
            var typeName = role.GetType().Name;
            if (typeName.EndsWith("Role", StringComparison.OrdinalIgnoreCase))
                typeName = typeName[..^4];
            return typeName;
        }

        /// <summary>
        /// Determines the draft faction for a role purely from the role object —
        /// never from a potentially-renamed name string.
        /// </summary>
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

            // Vanilla/unknown — use the IsImpostor / IsNeutral helpers
            if (role.IsImpostor()) return RoleFaction.Impostor;
            if (role.IsNeutral())  return RoleFaction.Neutral;
            return RoleFaction.Crewmate;
        }

        // Roles that can never be drafted no matter what the host configures
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
                pool.MaxCounts[roleName] = Math.Max(1, maxCount);
                pool.Weights[roleName] = Math.Max(1, weight);
                pool.Factions[roleName] = faction;
                pool.RoleLookup[roleName] = role;
            }
            else
            {
                pool.MaxCounts[roleName] = Math.Max(pool.MaxCounts[roleName], maxCount);
                pool.Weights[roleName] = Math.Max(pool.Weights[roleName], weight);
            }
        }

        private static IEnumerable<string> GetAllRoles()
        {
            // These are canonical LocaleKey values — stable regardless of locale or name-change mods.
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