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

        /// <summary>
        /// Builds DraftRoleCard list from a list of role name strings sent by the host.
        /// Used by both the Circle UI and the Card UI.
        /// </summary>
        public static List<DraftRoleCard> BuildCards(List<string> roles)
        {
            var cards = new List<DraftRoleCard>();
            for (int i = 0; i < roles.Count; i++)
            {
                string roleName = roles[i];
                var role = FindRoleByName(roleName);
                string team = GetTeamLabel(role, roleName);
                var icon = GetRoleIcon(role);
                var color = GetRoleColor(role, roleName);
                cards.Add(new DraftRoleCard(roleName, team, icon, color, i));
            }
            if (DraftManager.ShowRandomOption)
                cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, roles.Count));
            return cards;
        }

        /// <summary>
        /// Finds a RoleBehaviour by matching against both NiceName and GetRoleName(),
        /// after normalizing both sides. This handles mismatches between what the host
        /// sends (GetRoleName) and what the client has (NiceName).
        /// </summary>
        public static RoleBehaviour? FindRoleByName(string roleName)
        {
            if (RoleManager.Instance == null) return null;
            string normalized = Normalize(roleName);
            return RoleManager.Instance.AllRoles.ToArray().FirstOrDefault(r =>
            {
                if (r == null) return false;
                if (Normalize(r.NiceName) == normalized) return true;
                try { if (Normalize(r.GetRoleName()) == normalized) return true; } catch { }
                return false;
            });
        }

        /// <summary>
        /// Gets the faction label string for a role. Falls back to RoleCategory lookup
        /// by name string if the role object is null (i.e. lookup failed).
        /// </summary>
        public static string GetTeamLabel(RoleBehaviour? role, string roleName)
        {
            if (role != null)
            {
                try { return MiscUtils.GetParsedRoleAlignment(role); } catch { }
            }

            // Fallback: use our hardcoded faction map by name
            return RoleCategory.GetFaction(roleName) switch
            {
                RoleFaction.Impostor      => "Impostor",
                RoleFaction.NeutralKilling => "Neutral Killing",
                RoleFaction.Neutral        => "Neutral",
                _                          => "Crewmate"
            };
        }

        public static Sprite? GetRoleIcon(RoleBehaviour? role)
        {
            if (role is ICustomRole cr && cr.Configuration.Icon != null)
            {
                try { return cr.Configuration.Icon.LoadAsset(); } catch { }
            }
            if (role?.RoleIconSolid != null)
                return role.RoleIconSolid;
            return null; // caller decides fallback
        }

        public static Color GetRoleColor(RoleBehaviour? role, string roleName)
        {
            if (role is ICustomRole cr) return cr.RoleColor;
            if (role != null) return role.TeamColor;
            // Fallback to our color map
            return RoleColors.GetColor(roleName);
        }

        private static string Normalize(string s) =>
            (s ?? string.Empty).ToLowerInvariant().Replace(" ", "").Replace("-", "");
    }
}
