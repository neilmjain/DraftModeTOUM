using System.Collections.Generic;

namespace DraftModeTOUM.Managers
{
    public enum RoleFaction
    {
        Crewmate,
        Impostor,
        Neutral
    }

    public static class RoleCategory
    {
        private static readonly HashSet<string> ImpostorRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
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

        private static readonly HashSet<string> NeutralRoles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Benign
            "Amnesiac", "Fairy", "Mercenary", "Survivor",
            // Evil
            "Doomsayer", "Executioner", "Jester", "Spectre",
            // Killing
            "Arsonist", "Glitch", "Juggernaut", "Plaguebearer", "Pestilence", "SoulCollector", "Vampire", "Werewolf",
            // Outlier
            "Chef", "Inquisitor"
        };

        public static RoleFaction GetFaction(string roleName)
        {
            var normalized = Normalize(roleName);
            if (ImpostorRoles.Contains(normalized)) return RoleFaction.Impostor;
            if (NeutralRoles.Contains(normalized)) return RoleFaction.Neutral;
            return RoleFaction.Crewmate;
        }

        public static bool IsImpostor(string roleName) => ImpostorRoles.Contains(Normalize(roleName));
        public static bool IsNeutral(string roleName) => NeutralRoles.Contains(Normalize(roleName));

        private static string Normalize(string roleName)
        {
            return (roleName ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);
        }
    }
}
