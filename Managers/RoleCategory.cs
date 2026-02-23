using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    public enum RoleFaction
    {
        Crewmate,
        CrewInvestigative,
        CrewKilling,
        CrewPower,
        CrewProtective,
        CrewSupport,
        Impostor,
        NeutralKilling,
        NeutralBenign,
        NeutralEvil,
        NeutralOutlier
    }

    public static class RoleCategory
    {
        private static readonly HashSet<string> ImpostorRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Concealing
            "Eclipsal", "Escapist", "Grenadier", "Morphling", "Swooper", "Venerer",
            // Killing
            "Ambusher", "Bomber", "Parasite", "Scavenger", "Warlock",
            // Power
            "Ambassador", "Puppeteer", "Spellslinger", "Traitor",
            // Support
            "Blackmailer", "Hypnotist", "Janitor", "Miner", "Undertaker"
        };

        private static readonly HashSet<string> NeutralKillingRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Arsonist", "Glitch", "Juggernaut", "Plaguebearer", "Pestilence", "SoulCollector", "Vampire", "Werewolf"
        };

        private static readonly HashSet<string> NeutralBenignRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Amnesiac", "Fairy", "Mercenary", "Survivor"
        };

        private static readonly HashSet<string> NeutralEvilRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Doomsayer", "Executioner", "Jester", "Spectre"
        };

        private static readonly HashSet<string> NeutralOutlierRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Chef", "Inquisitor"
        };

        private static readonly HashSet<string> CrewInvestigativeRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Aurial", "Forensic", "Investigator", "Lookout", "Mystic", "Seer", "Snitch", "Sonar", "Spy", "Trapper"
        };

        private static readonly HashSet<string> CrewKillingRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Deputy", "Hunter", "Sheriff", "Veteran", "Vigilante"
        };

        private static readonly HashSet<string> CrewPowerRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Jailor", "Mayor", "Monarch", "Politician", "Prosecutor", "Swapper", "TimeLord"
        };

        private static readonly HashSet<string> CrewProtectiveRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "Altruist", "Cleric", "Medic", "Mirrorcaster", "Oracle", "Warden"
        };

        private static readonly HashSet<string> CrewSupportRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "EngineerTou", "Imitator", "Medium", "Plumber", "Sentry", "Transporter"
        };

        public static RoleFaction GetFaction(string roleName)
        {
            var n = Normalize(roleName);
            if (ImpostorRoles.Contains(n))          return RoleFaction.Impostor;
            if (NeutralKillingRoles.Contains(n))    return RoleFaction.NeutralKilling;
            if (NeutralBenignRoles.Contains(n))     return RoleFaction.NeutralBenign;
            if (NeutralEvilRoles.Contains(n))       return RoleFaction.NeutralEvil;
            if (NeutralOutlierRoles.Contains(n))    return RoleFaction.NeutralOutlier;
            if (CrewInvestigativeRoles.Contains(n)) return RoleFaction.CrewInvestigative;
            if (CrewKillingRoles.Contains(n))       return RoleFaction.CrewKilling;
            if (CrewPowerRoles.Contains(n))         return RoleFaction.CrewPower;
            if (CrewProtectiveRoles.Contains(n))    return RoleFaction.CrewProtective;
            if (CrewSupportRoles.Contains(n))       return RoleFaction.CrewSupport;
            return RoleFaction.Crewmate;
        }

        public static bool IsImpostor(string roleName)       => ImpostorRoles.Contains(Normalize(roleName));
        public static bool IsNeutralKilling(string roleName) => NeutralKillingRoles.Contains(Normalize(roleName));
        public static bool IsNeutralBenign(string roleName)  => NeutralBenignRoles.Contains(Normalize(roleName));
        public static bool IsNeutralEvil(string roleName)    => NeutralEvilRoles.Contains(Normalize(roleName));
        public static bool IsNeutralOutlier(string roleName) => NeutralOutlierRoles.Contains(Normalize(roleName));

        /// <summary>Returns true for any neutral sub-faction.</summary>
        public static bool IsNeutral(string roleName)
        {
            var n = Normalize(roleName);
            return NeutralBenignRoles.Contains(n) || NeutralEvilRoles.Contains(n) || NeutralOutlierRoles.Contains(n);
        }

        public static bool IsCrewInvestigative(string roleName) => CrewInvestigativeRoles.Contains(Normalize(roleName));
        public static bool IsCrewKilling(string roleName)       => CrewKillingRoles.Contains(Normalize(roleName));
        public static bool IsCrewPower(string roleName)         => CrewPowerRoles.Contains(Normalize(roleName));
        public static bool IsCrewProtective(string roleName)    => CrewProtectiveRoles.Contains(Normalize(roleName));
        public static bool IsCrewSupport(string roleName)       => CrewSupportRoles.Contains(Normalize(roleName));

        private static string Normalize(string roleName) =>
            (roleName ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
    }
}
