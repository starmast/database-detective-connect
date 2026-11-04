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
        private ConfigEntry<bool> sendTables;

        private void Awake()
        {
            enabledConfig = Config.Bind("General", "Enabled", true, "Keep a copy of the current level's database for external SQL editors.");
            mirrorPath = Config.Bind("General", "MirrorPath", "", "Where to write the mirror. Empty means <game data folder>\\Connect\\current.db.");
            debounceMs = Config.Bind("General", "DebounceMs", 300, "Quiet time after the last database change before the mirror is refreshed.");
            sendTables = Config.Bind("Editor", "SendTables", true, "Make the mirror writable and send any new table created in it by your SQL editor to the game. When off, the mirror is read-only.");

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

            bool send = sendTables.Value;
            if (send)
            {
                try
                {
                    GameUi.Init();
                    EditorTables.Init(Logger);
                }
                catch (Exception ex)
                {
                    send = false;
                    Logger.LogError($"Could not hook the game's table UI; sending tables from your editor is disabled. {ex}");
                }
            }

            MirrorService.Init(Logger, dataDir, mirror, debounceMs.Value, send, Patches.CurrentLevel);
            MirrorService.MarkDirty("startup");
            Logger.LogInfo($"Mirroring the current level to {mirror}" + (send ? " (new tables created there are sent to the game)" : ""));
        }

        private void Update()
        {
            try
            {
                EditorTables.Tick();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Editor table update failed: {ex}");
            }
        }
    }
}
