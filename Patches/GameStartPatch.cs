using DraftModeTOUM.Managers;
using HarmonyLib;
using Reactor.Utilities;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// First application attempt: fires when the intro cutscene begins.
    /// Also kicks off the retry coroutine which will keep trying every 0.5s
    /// for up to 10s in case any players aren't fully loaded yet.
    /// </summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class IntroCutsceneBeginPatch
    {
        public static void Prefix()
        {
            if (DraftManager.PendingRoleAssignments.Count == 0) return;

            DraftModePlugin.Logger.LogInfo(
                "[GameStartPatch] IntroCutscene.CoBegin fired — attempting first role application");

            // First immediate attempt
            DraftManager.ApplyPendingRolesOnGameStart();

            // Start retry loop in case anything failed or players weren't loaded yet
            Coroutines.Start(DraftManager.CoApplyRolesWithRetry());
        }
    }

    /// <summary>
    /// Second application attempt: fires when the ship map fully loads.
    /// This is later than IntroCutscene so all PlayerControls should exist.
    /// The retry loop handles deduplication — already-applied players are skipped.
    /// </summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    public static class ShipStatusRoleApplyPatch
    {
        public static void Postfix()
        {
            if (DraftManager.PendingRoleAssignments.Count == 0) return;

            DraftModePlugin.Logger.LogInfo(
                "[GameStartPatch] ShipStatus.Start fired — re-attempting any unfinished role assignments");

            // This is a later, more reliable moment — attempt again.
            // The retry coroutine may still be running from IntroCutscene;
            // ApplyPendingRolesOnGameStart() is safe to call concurrently because
            // _appliedPlayers guards against double-applying.
            DraftManager.ApplyPendingRolesOnGameStart();
        }
    }
}
