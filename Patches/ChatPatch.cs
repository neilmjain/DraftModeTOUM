using DraftModeTOUM.Managers;
using HarmonyLib;
<<<<<<< HEAD
=======
using MiraAPI.GameOptions;
using System.Linq;
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
using UnityEngine;

namespace DraftModeTOUM.Patches
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    public static class ChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First + 100)]
        public static bool Prefix(ChatController __instance)
        {
            string msg = __instance.freeChatField.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(msg)) return true;

            // ── /draft ────────────────────────────────────────────────────────
            if (msg.StartsWith("/draft", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase)
                && !msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                    DraftManager.SendSystemMessage("Only the host can start a draft.");
                else if (DraftManager.IsDraftActive)
<<<<<<< HEAD
                    DraftManager.SendSystemMessage("A draft is already in progress.");
=======
                {
                    DraftManager.SendChatLocal("<color=red>Draft already active.</color>");
                }
                else if (!OptionGroupSingleton<DraftModeOptions>.Instance.EnableDraft)
                {
                    DraftManager.SendChatLocal("<color=red>Draft Mode is disabled in settings.</color>");
                }
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                else
                    DraftManager.StartDraft();
<<<<<<< HEAD

                __instance.freeChatField.textArea.Clear();
=======
                }
                ClearChat(__instance);
                return false;
            }

            if (msg.StartsWith("/draftend", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendChatLocal("<color=red>Only host can end the draft.</color>");
                }
                else if (!DraftManager.IsDraftActive)
                {
                    DraftManager.SendChatLocal("<color=red>No draft is currently active.</color>");
                }
                else
                {
                    DraftManager.Reset(cancelledBeforeCompletion: true);
                    DraftManager.SendChatLocal("<color=#FFD700>Draft has been cancelled by the host.</color>");
                }
                ClearChat(__instance);
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                return false;
            }

            // ── /draftrecap ───────────────────────────────────────────────────
            if (msg.StartsWith("/draftrecap", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendSystemMessage("Only the host can change draft settings.");
                }
                else
                {
<<<<<<< HEAD
                    DraftManager.ShowRecap = !DraftManager.ShowRecap;
                    string state = DraftManager.ShowRecap
                        ? "<color=#00FF00>ON</color>"
                        : "<color=#FF4444>OFF</color>";
                    DraftManager.SendSystemMessage($"Draft recap is now: {state}");
=======
                    var opts = OptionGroupSingleton<DraftModeOptions>.Instance;
                    opts.ShowRecap = !opts.ShowRecap;
                    DraftManager.ShowRecap = opts.ShowRecap;
                    string status = DraftManager.ShowRecap ? "<color=green>ON</color>" : "<color=red>OFF</color>";
                    DraftManager.SendChatLocal($"<color=#FFD700>Draft recap is now: {status}</color>");
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
                }
                ClearChat(__instance);
                return false;
            }

<<<<<<< HEAD
            // ── /draftmod ─────────────────────────────────────────────────────
            if (msg.StartsWith("/draftmod", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!AmongUsClient.Instance.AmHost)
                {
                    DraftManager.SendSystemMessage("Only the host can change draft settings.");
                }
                else
                {
                    Patches.RequireModPatch.RequireDraftMod = !Patches.RequireModPatch.RequireDraftMod;
                    string state = Patches.RequireModPatch.RequireDraftMod
                        ? "<color=#00FF00>ON</color>"
                        : "<color=#FF4444>OFF</color>";
                    DraftManager.SendSystemMessage($"Require DraftModeTOUM: {state}");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

            // ── Number picks (chat fallback while screen is open) ─────────────
            if (DraftManager.IsDraftActive
                && (msg == "1" || msg == "2" || msg == "3" || msg == "4"))
            {
                var currentPicker = DraftManager.GetCurrentPickerState();
                if (currentPicker != null
                    && currentPicker.PlayerId == PlayerControl.LocalPlayer.PlayerId)
                {
                    int index = int.Parse(msg) - 1;
                    DraftNetworkHelper.SendPickToHost(index);
                    // No system message here — the screen already gives visual feedback
                }
                else
                {
                    DraftManager.SendSystemMessage("It is not your turn to pick.");
                }
                __instance.freeChatField.textArea.Clear();
                return false;
            }

=======
>>>>>>> db2561092809c7deaf51cfab10e6a01faa92058c
            return true;
        }

        private static void ClearChat(ChatController chat)
        {
            chat.freeChatField.Clear();
            chat.quickChatMenu.Clear();
            chat.quickChatField.Clear();
            chat.UpdateChatMode();
        }
    }
}
