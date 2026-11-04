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
        private ConfigEntry<bool> bridgeEnabled;
        private bool bridgeRunning;

        private void Awake()
        {
            enabledConfig = Config.Bind("General", "Enabled", true, "Keep a copy of the current level's database for external SQL editors.");
            mirrorPath = Config.Bind("General", "MirrorPath", "", "Where to write the mirror. Empty means <game data folder>\\Connect\\current.db.");
            debounceMs = Config.Bind("General", "DebounceMs", 300, "Quiet time after the last database change before the mirror is refreshed.");
            sendTables = Config.Bind("Editor", "SendTables", true, "Make the mirror writable and send any new table created in it by your SQL editor to the game. When off, the mirror is read-only.");
            bridgeEnabled = Config.Bind("Bridge", "Enabled", false, "Listen on 127.0.0.1 so tools such as ddsql can query the running game. Off by default; the connection details and a random token are written to <game data folder>\\Connect\\bridge.json. The environment variable DDCONNECT_BRIDGE=1 also turns it on.");

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
            bool bridge = bridgeEnabled.Value || Environment.GetEnvironmentVariable("DDCONNECT_BRIDGE") == "1";
            if (send || bridge)
            {
                try
                {
                    GameUi.Init();
                }
                catch (Exception ex)
                {
                    send = false;
                    Logger.LogError($"Could not hook the game's table UI; sending editor tables and the bridge's state command are disabled. {ex}");
                }
            }
            EditorTables.Init(Logger);

            MirrorService.Init(Logger, dataDir, mirror, debounceMs.Value, send, Patches.CurrentLevel);
            MirrorService.MarkDirty("startup");
            Logger.LogInfo($"Mirroring the current level to {mirror}" + (send ? " (new tables created there are sent to the game)" : ""));

            if (bridge) StartBridge(dataDir);
        }

        private void StartBridge(string dataDir)
        {
            MainThread.Init(Logger);
            try
            {
                QueryRunner.Init(Logger, new Harmony(Guid + ".bridge"));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not hook the game's search bar; the bridge's save command is disabled. {ex}");
            }

            try
            {
                BridgeServer.Start(Logger, Path.Combine(Path.Combine(dataDir, "Connect"), "bridge.json"));
                bridgeRunning = true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not start the bridge. {ex}");
            }
        }

        private void Update()
        {
            try
            {
                EditorTables.Tick();
                if (bridgeRunning) MainThread.Pump(this);
            }
            catch (Exception ex)
            {
                Logger.LogError($"DD Connect update failed: {ex}");
            }
        }

        private void OnDestroy() => BridgeServer.Stop();
    }
}
