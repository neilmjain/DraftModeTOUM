using DraftModeTOUM.Managers;
using HarmonyLib;

namespace DraftModeTOUM.Patches
{
    /// <summary>
    /// Applies pending draft role assignments when the game actually starts.
    /// Using IntroCutscene.Begin means the ship is fully loaded and all players
    /// are present — role Initialize() calls (including target assignment for
    /// roles like Executioner/Monarch) will work correctly here.
    /// 
    /// Doing it here instead of at draft-end means players sit as Crewmate
    /// in the lobby with no ability buttons visible.
    /// </summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class IntroCutsceneBeginPatch
    {
        public static void Prefix()
        {
            if (DraftManager.PendingRoleAssignments.Count == 0) return;
            DraftManager.ApplyPendingRolesOnGameStart();
        }
    }
}