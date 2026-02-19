using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using DraftModeTOUM.Patches;
using UnityEngine;

namespace DraftModeTOUM
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("gg.reactor.api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("auavengers.tou.mira", BepInDependency.DependencyFlags.HardDependency)]
    public class DraftModePlugin : BasePlugin
    {
        public static ManualLogSource Logger;
        private Harmony _harmony;

        public override void Load()
        {
            Logger = Log;
            Logger.LogInfo($"DraftModeTOUM v{PluginInfo.PLUGIN_VERSION} loading...");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<DraftTicker>();
                Logger.LogInfo("DraftTicker registered successfully.");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to register DraftTicker: {ex}");
            }

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll();

            // Manually patch internal TOUM class + OnPlayerJoined rejoin guard
            RequireModPatch.Apply(_harmony);

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
        public const string PLUGIN_VERSION = "1.0.3";
    }

    // Clear kicked/verified lists when the host disconnects from a lobby
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    public static class OnDisconnectPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RequireModPatch.ClearSession();
            DraftModePlugin.Logger.LogInfo("[DraftModePlugin] Session cleared on disconnect.");
        }
    }
}