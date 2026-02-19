using System;
using System.Collections.Generic;
using System.Linq;
using DraftModeTOUM.Patches;
using MiraAPI.Roles;
using MiraAPI.Utilities;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    public static class DraftUiManager
    {
        private static DraftSelectionMinigame? _minigame;

        public static void ShowPicker(List<string> roles)
        {
            if (HudManager.Instance == null || roles == null || roles.Count == 0) return;

            EnsureMinigame();
            if (_minigame == null)
            {
                DraftModePlugin.Logger.LogError("[DraftUiManager] Cannot show picker — minigame failed to create.");
                return;
            }

            // Hide the status overlay while the full picker wheel is open
            DraftStatusOverlay.Hide();

            var cards = BuildCards(roles);
            _minigame.Open(cards, OnPickSelected);
        }

        // Called whenever turn state changes so non-pickers still see updated list
        public static void RefreshTurnList()
        {
            if (_minigame == null || _minigame.gameObject == null) return;
            if (!_minigame.gameObject.activeSelf) return;
            _minigame.RefreshTurnList();
        }

        public static void CloseAll()
        {
            if (_minigame == null) return;
            try
            {
                if (_minigame.gameObject != null && _minigame.gameObject.activeSelf)
                    _minigame.Close();
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[DraftUiManager] CloseAll exception (safe to ignore): {ex.Message}");
            }
            _minigame = null;

            // Restore the status overlay once the picker wheel closes
            if (DraftManager.IsDraftActive)
                DraftStatusOverlay.Show();
        }

        private static void EnsureMinigame()
        {
            if (_minigame != null)
            {
                bool destroyed = false;
                try { destroyed = (_minigame.gameObject == null); }
                catch { destroyed = true; }
                if (destroyed) _minigame = null;
            }

            if (_minigame == null)
            {
                _minigame = DraftSelectionMinigame.Create();
                if (_minigame == null)
                    DraftModePlugin.Logger.LogError("[DraftUiManager] DraftSelectionMinigame.Create() returned null!");
            }
        }

        private static void OnPickSelected(int index)
        {
            DraftNetworkHelper.SendPickToHost(index);
        }

        private static List<DraftRoleCard> BuildCards(List<string> roles)
        {
            var cards = new List<DraftRoleCard>();

            for (int i = 0; i < roles.Count; i++)
            {
                string roleName = roles[i];
                var role     = FindRoleByName(roleName);
                string team  = role != null ? MiscUtils.GetParsedRoleAlignment(role) : "Unknown";
                var icon     = GetRoleIcon(role);
                var color    = GetRoleColor(role);
                cards.Add(new DraftRoleCard(roleName, team, icon, color, i));
            }

            // Random card always last at index 3
            cards.Add(new DraftRoleCard("Random", "Random", TouRoleIcons.RandomAny.LoadAsset(), Color.white, 3));
            return cards;
        }

        private static RoleBehaviour? FindRoleByName(string roleName)
        {
            if (RoleManager.Instance == null) return null;
            string normalized = Normalize(roleName);
            return RoleManager.Instance.AllRoles.ToArray()
                .FirstOrDefault(r => Normalize(r.NiceName) == normalized);
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
