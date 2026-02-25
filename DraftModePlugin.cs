using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using DraftModeTOUM.Managers;
using DraftModeTOUM.Patches;
using MiraAPI.PluginLoading;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using UnityEngine;

namespace DraftModeTOUM
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.reactor.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("mira.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("auavengers.tou.mira", BepInDependency.DependencyFlags.HardDependency)]
    [ReactorModFlags(ModFlags.RequireOnAllClients)]
    public class DraftModePlugin : BasePlugin, IMiraPlugin
    {
        public static ManualLogSource Logger = null!;
        private Harmony _harmony = null!;

        public string OptionsTitleText => "Draft Mode";

        public ConfigFile GetConfigFile() => Config;

        public override void Load()
        {
            Logger = Log;
            Logger.LogInfo($"DraftModeTOUM v{PluginInfo.PLUGIN_VERSION} loading...");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<DraftTicker>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftDashboardReporter>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftScreenController>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftCircleMinigame>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftStatusOverlay>();
                ClassInjector.RegisterTypeInIl2Cpp<DraftRecapOverlay>();
                Logger.LogInfo("Draft UI Components registered.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to register types: {ex}");
            }

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            // DraftDashboardReporter is started lazily from MainMenuManagerPatch
            // and re-ensured on lobby join — NOT here, as Unity scene isn't ready during Load().

            Logger.LogInfo("DraftModeTOUM loaded successfully!");
        }

        public override bool Unload()
        {
            _harmony?.UnpatchSelf();
            return base.Unload();
        }
    }

    internal static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.draftmodetoun.mod";
        public const string PLUGIN_NAME = "DraftModeTOUM";
        public const string PLUGIN_VERSION = "1.0.5";
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class OnDisconnectPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftScreenController.Hide();
            DraftUiManager.CloseAll();
            DraftRecapOverlay.Hide();
            bool draftStillInProgress = DraftManager.IsDraftActive;
            DraftManager.Reset(cancelledBeforeCompletion: draftStillInProgress);
            DraftModePlugin.Logger.LogInfo($"[DraftModePlugin] Session cleared on disconnect.");
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    public static class BeginGameCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftScreenController.Hide();
            // Do NOT hide DraftStatusOverlay here to avoid lobby flash.
            DraftRecapOverlay.Hide();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] Game starting...");
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    public static class IntroCutsceneHidePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            // Backup method: Hides overlay when "Shhh" screen appears
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    public static class ShipStatusStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Ultimate backup: Hides overlay when map loads
            DraftScreenController.Hide();
            DraftStatusOverlay.SetState(OverlayState.Hidden);
            DraftRecapOverlay.Hide();
        }
    }

    /// <summary>
    /// Start the reporter as soon as the main menu is alive.
    /// This is the earliest point where Unity scene/GameObject creation is safe.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    public static class MainMenuManagerStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftDashboardReporter.EnsureExists();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] DashboardReporter ensured from MainMenu.");
        }
    }

    /// <summary>
    /// Re-ensure the reporter every time we successfully join a server,
    /// in case it was destroyed during a previous session.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    public static class OnGameJoinedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DraftDashboardReporter.EnsureExists();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] DashboardReporter ensured on game join.");
        }
    }
}