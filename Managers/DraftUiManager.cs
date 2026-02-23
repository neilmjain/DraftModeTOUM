using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Patches;
using MiraAPI.LocalSettings;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class DraftUiManager
    {
        private static DraftCircleMinigame? _circleMinigame;

        private static bool UseCircle
        {
            get
            {
                var local = MiraAPI.LocalSettings.LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
                if (local != null && local.OverrideUiStyle.Value)
                    return local.UseCircleStyle.Value;

                return MiraAPI.GameOptions.OptionGroupSingleton<DraftModeOptions>.Instance.UseCircleStyle;
            }
        }

        public static void ShowPicker(List<string> roles)
        {
            if (HudManager.Instance == null || roles == null || roles.Count == 0) return;

            // Hide the text but keep the solid black background active
            DraftStatusOverlay.SetState(OverlayState.BackgroundOnly);

            if (UseCircle)
            {
                ShowCircle(roles);
            }
            else
            {
                DraftScreenController.Show(roles.ToArray());
            }
        }

        public static void RefreshTurnList()
        {
            if (UseCircle && _circleMinigame != null &&
                _circleMinigame.gameObject != null && _circleMinigame.gameObject.activeSelf)
            {
                _circleMinigame.RefreshTurnList();
            }
        }

        public static void CloseAll()
        {
            DraftScreenController.Hide();

            var circle = _circleMinigame;
            _circleMinigame = null;
            if (circle != null)
            {
                try
                {
                    bool alive = false;
                    try { alive = circle.gameObject != null && circle.gameObject.activeSelf; } catch { }
                    if (alive) circle.Close();
                    else
                    {
                        bool exists = false;
                        try { exists = circle.gameObject != null; } catch { }
                        if (exists) UnityEngine.Object.Destroy(circle.gameObject);
                    }
                }
                catch { }
            }

            if (DraftManager.IsDraftActive)
                DraftStatusOverlay.SetState(OverlayState.Waiting);
        }

        private static void ShowCircle(List<string> roles)
        {
            EnsureCircleMinigame();
            if (_circleMinigame == null) return;

            var cards = BuildCards(roles);
            _circleMinigame.Open(cards, OnPickSelected);
        }

        private static void EnsureCircleMinigame()
        {
            if (_circleMinigame != null)
            {
                bool destroyed = false;
                try { destroyed = (_circleMinigame.gameObject == null); }
                catch { destroyed = true; }
                if (destroyed) _circleMinigame = null;
            }
            if (_circleMinigame == null)
                _circleMinigame = DraftCircleMinigame.Create();
        }

        private static void OnPickSelected(int index)
        {
            var circle = _circleMinigame;
            _circleMinigame = null;
            try { circle?.Close(); } catch { }
            DraftNetworkHelper.SendPickToHost(index);
        }

        private static List<DraftRoleCard> BuildCards(List<string> roles)
        {
            var cards = new List<DraftRoleCard>();
            for (int i = 0; i < roles.Count; i++)
            {
                string canonicalKey = roles[i]; // always a LocaleKey now, e.g. "SoulCollector"
                var role = FindRoleByCanonicalKey(canonicalKey);

                // Display name: use TOU's own locale system so it shows "Soul Collector"
                // in the player's language, but is NOT affected by name-change mods because
                // we're reading from the key, not from role.NiceName / role.RoleName.
                string displayName = GetDisplayName(canonicalKey, role);
                string team = role != null ? MiscUtils.GetParsedRoleAlignment(role) : "Unknown";
                var icon = GetRoleIcon(role);
                var color = GetRoleColor(role);
                cards.Add(new DraftRoleCard(displayName, team, icon, color, i));
            }
            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, roles.Count));
            return cards;
        }


        private static RoleBehaviour? FindRoleByCanonicalKey(string canonicalKey)
        {
            if (RoleManager.Instance == null) return null;

            // Fastest path: the pool already built a lookup table for us.
            var state = DraftManager.GetCurrentPickerState();
            // Pool lookup lives on DraftManager via the stored pool reference exposed below.
            var poolRole = DraftManager.FindRoleInPool(canonicalKey);
            if (poolRole != null) return poolRole;

            // Fallback: scan all registered roles by canonical key.
            return MiscUtils.AllRegisteredRoles
                .FirstOrDefault(r => RolePoolBuilder.GetCanonicalName(r)
                    .Equals(canonicalKey, System.StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDisplayName(string canonicalKey, RoleBehaviour? role)
        {
            // Try TOU locale first — most reliable and translation-aware.
            try
            {
                var locKey = $"TouRole{canonicalKey}";
                var localized = TouLocale.Get(locKey);
                if (!string.IsNullOrWhiteSpace(localized) && localized != locKey)
                    return localized;
            }
            catch { }

            // If the role object is available, use its canonical locale key as display
            // (still not going through NiceName / GetRoleName).
            if (role is TownOfUs.Roles.ITownOfUsRole touRole)
                return touRole.LocaleKey;

            // Last resort: make the canonical key human-readable ("SoulCollector" -> "Soul Collector").
            return System.Text.RegularExpressions.Regex
                .Replace(canonicalKey, "([A-Z])", " $1").Trim();
        }

        private static Sprite? GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
                return cr.Configuration.Icon.LoadAsset();
            if (role?.RoleIconSolid != null)
                return role.RoleIconSolid;
            return TouRoleIcons.RandomAny.LoadAsset();
        }

        private static Color GetRoleColor(RoleBehaviour? role)
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            return role != null ? role.TeamColor : Color.white;
        }

        private static string Normalize(string s) =>
            (s ?? string.Empty).ToLowerInvariant().Replace(" ", "").Replace("-", "");
    }
}