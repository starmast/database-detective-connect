using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DDConnect
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.ddconnect";
        public const string Name = MyPluginInfo.PLUGIN_NAME;
        public const string Version = MyPluginInfo.PLUGIN_VERSION;

        private ConfigEntry<bool> enabledConfig;
        private ConfigEntry<string> mirrorPath;
        private ConfigEntry<int> debounceMs;

        private void Awake()
        {
            enabledConfig = Config.Bind("General", "Enabled", true, "Keep a read-only mirror of the current level's database for external SQL editors.");
            mirrorPath = Config.Bind("General", "MirrorPath", "", "Where to write the mirror. Empty means <game data folder>\\Connect\\current.db.");
            debounceMs = Config.Bind("General", "DebounceMs", 300, "Quiet time after the last database change before the mirror is refreshed.");

            if (!enabledConfig.Value)
            {
                Logger.LogInfo("Disabled in config.");
                return;
            }

            string dataDir = Application.persistentDataPath;
            string mirror = string.IsNullOrWhiteSpace(mirrorPath.Value)
                ? Path.Combine(Path.Combine(dataDir, "Connect"), "current.db")
                : mirrorPath.Value;

            try
            {
                Patches.Apply(new Harmony(Guid));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not hook the game's database code; mirror disabled. {ex}");
                return;
            }

            MirrorService.Init(Logger, dataDir, mirror, debounceMs.Value, Patches.CurrentLevel);
            MirrorService.MarkDirty("startup");
            Logger.LogInfo($"Mirroring the current level to {mirror}");
        }
    }
}
