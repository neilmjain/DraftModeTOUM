using HarmonyLib;
using MiraAPI.GameOptions;
using TownOfUs.Options;
using TownOfUs.Patches;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// Two-part suppression of TOU's base role-list system when Draft Mode is enabled:
    ///
    /// PART 1 — UI HIDE
    ///   Patches RoleOptions.GroupVisible so TOU's entire "Role Settings" section
    ///   disappears from the options menu while Draft Mode is on.  This stops hosts
    ///   from accidentally configuring TOU's role-list settings that have no effect
    ///   during a draft game.
    ///
    /// PART 2 — ASSIGNMENT GUARD
    ///   Patches TOU's SelectRoles prefix (Priority.First + 100) so it runs before
    ///   TOU's own patch.  When draft is active we:
    ///     a) Zero all neutral min/max counts in TOU's RoleOptions (covers MinMaxList mode).
    ///     b) This is a safety net only — in RoleList mode TOU's slot buckets drive
    ///        assignment, but every player already has an /up request from
    ///        DraftManager.ApplyAllRoles(), so TOU's AssignRolesToPlayers naturally
    ///        exhausts all players via /up with no random leftovers.
    /// </summary>

    // ── PART 1: Hide TOU's "Role Settings" group in the options UI ───────────
    //
    // RoleOptions.GroupVisible is a Func<bool> property with a backing field set
    // in the constructor.  We patch the getter so our extra check runs after the
    // base check.  Returning false from GroupVisible makes MiraAPI skip the group.

    [HarmonyPatch(typeof(RoleOptions), nameof(RoleOptions.GroupVisible), MethodType.Getter)]
    public static class HideTouRoleOptionsWhenDraftActive
    {
        [HarmonyPostfix]
        public static void Postfix(ref System.Func<bool> __result)
        {
            // Capture the original delegate
            var original = __result;
            __result = () =>
            {
                // Honour TOU's own checks first (e.g. hide in HideAndSeek mode)
                if (original != null && !original.Invoke()) return false;

                // Hide if Draft Mode is enabled
                var draftOpts = OptionGroupSingleton<DraftModeOptions>.Instance;
                if (draftOpts != null && draftOpts.EnableDraft) return false;

                return true;
            };
        }
    }

    // ── PART 2: Zero TOU neutral counts before SelectRoles runs ──────────────
    //
    // TOU's SelectRolesPatch has [HarmonyPriority(Priority.First)] = 800.
    // We use Priority.First + 100 = 900 to run BEFORE it.

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    public static class ZeroTouNeutralsForDraftPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(HarmonyLib.Priority.First + 100)]
        public static void Prefix()
        {
            var draftOpts = OptionGroupSingleton<DraftModeOptions>.Instance;
            if (draftOpts == null || !draftOpts.EnableDraft) return;

            try
            {
                var roleOpts = OptionGroupSingleton<RoleOptions>.Instance;
                if (roleOpts == null)
                {
                    DraftModePlugin.Logger.LogWarning("[TouSuppressPatch] RoleOptions not ready — skipping zeroing.");
                    return;
                }

                // Zero neutral min/max so TOU's MinMaxList path spawns zero random neutrals.
                // In RoleList mode these fields are ignored, but zeroing them is harmless.
                // sendRpc: false — we don't want to broadcast this temporary
                // zeroing to clients; it's host-side only for this game session.
                roleOpts.MinNeutralBenign.SetValue(0f,  false);
                roleOpts.MaxNeutralBenign.SetValue(0f,  false);
                roleOpts.MinNeutralEvil.SetValue(0f,    false);
                roleOpts.MaxNeutralEvil.SetValue(0f,    false);
                roleOpts.MinNeutralKiller.SetValue(0f,  false);
                roleOpts.MaxNeutralKiller.SetValue(0f,  false);
                roleOpts.MinNeutralOutlier.SetValue(0f, false);
                roleOpts.MaxNeutralOutlier.SetValue(0f, false);

                DraftModePlugin.Logger.LogInfo("[TouSuppressPatch] Zeroed TOU neutral min/max for draft game.");
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger.LogWarning($"[TouSuppressPatch] Exception zeroing role options: {ex.Message}");
            }
        }
    }
}
