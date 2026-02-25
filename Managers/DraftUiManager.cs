using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace DraftModeTOUM.Managers
{
    /// <summary>
    /// Manages the lifetime of every draft-related UI panel so they can all
    /// be closed in one call (e.g. on disconnect or game-start).
    /// Also exposes helpers used by DraftRpcPatch and DraftSelectionMinigame.
    /// </summary>
    public static class DraftUiManager
    {
        private static readonly List<GameObject> _tracked = new();

        /// <summary>Register a UI root so CloseAll() can reach it.</summary>
        public static void Track(GameObject go)
        {
            if (go != null && !_tracked.Contains(go))
                _tracked.Add(go);
        }

        /// <summary>Hide and untrack every registered UI panel.</summary>
        public static void CloseAll()
        {
            foreach (var go in _tracked)
            {
                if (go != null)
                    go.SetActive(false);
            }
            _tracked.Clear();

            // Also close the picker screen if one is open
            DraftScreenController.Hide();
        }

        // ── Picker ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Open the role-picker screen showing the given offered role IDs.
        /// Called when this client is the current picker.
        /// </summary>
        public static void ShowPicker(List<ushort> roleIds)
        {
            DraftScreenController.Show(roleIds.ToArray());
        }

        // ── Turn list ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Refresh the status overlay's turn-order display after the draft state changes.
        /// </summary>
        public static void RefreshTurnList()
        {
            DraftStatusOverlay.Refresh();
        }

        // ── Card building ─────────────────────────────────────────────────────────

        /// <summary>
        /// Convert a list of role IDs into <see cref="DraftRoleCard"/> objects ready
        /// for the selection screen to instantiate.
        /// </summary>
        public static List<DraftRoleCard> BuildCards(List<ushort> roleIds)
        {
            var cards = new List<DraftRoleCard>();
            int index = 0;
            foreach (var id in roleIds)
            {
                var role = RoleManager.Instance?.GetRole((RoleTypes)id);
                string roleName = role?.NiceName ?? id.ToString();
                string teamName = RoleCategory.GetFactionFromRole(role).ToString();
                Sprite? icon    = null; // caller falls back to TouRoleIcons.RandomAny
                Color color     = RoleColors.GetColor(roleName);
                cards.Add(new DraftRoleCard(roleName, teamName, icon, color, index));
                index++;
            }
            return cards;
        }

        // ── Utility ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Normalise a role/team name string for case-insensitive, space-insensitive matching.
        /// </summary>
        public static string Normalize(string s) =>
            (s ?? string.Empty).Replace(" ", "").Replace("-", "");
    }
}
