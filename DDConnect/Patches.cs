using System;
using System.Reflection;
using HarmonyLib;

namespace DDConnect
{
    /// <summary>
    /// Hooks the game's single connection factory (DatabaseUtils.GetConnection(string)) and
    /// LevelManager.SetLevel. Game types are resolved by name so the plugin compiles without
    /// referencing the game's assemblies.
    /// </summary>
    internal static class Patches
    {
        private static Func<int> getCurrLevel;
        private static EventInfo commitEvent;
        private static Delegate commitHandler;

        /// <summary>The game's level number for a workshop case (LevelManager.CUSTOM_LEVEL).</summary>
        public const int CustomLevel = -1;

        private static FieldInfo currentModField;
        private static FieldInfo currentModPathField;
        private static FieldInfo modDatabaseField;

        public static int CurrentLevel() => getCurrLevel();

        /// <summary>
        /// The database file of the workshop case that is loaded, or null when none is. A custom case does not use
        /// the numbered "Level N" files; the game opens the file the case shipped with, in its workshop folder.
        /// Plain static reads, so it is safe from any thread.
        /// </summary>
        public static string CustomDatabasePath()
        {
            if (currentModField == null) return null;
            object mod = currentModField.GetValue(null);
            string directory = (string)currentModPathField.GetValue(null);
            string database = mod == null ? null : (string)modDatabaseField.GetValue(mod);
            return string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(database) ? null : System.IO.Path.Combine(directory, database);
        }

        public static void Apply(Harmony harmony)
        {
            Type databaseUtils = AccessTools.TypeByName("DatabaseUtils") ?? throw new MissingMemberException("DatabaseUtils");
            Type levelManager = AccessTools.TypeByName("LevelManager") ?? throw new MissingMemberException("LevelManager");

            MethodInfo getConnection = AccessTools.Method(databaseUtils, "GetConnection", new[] { typeof(string) })
                ?? throw new MissingMethodException("DatabaseUtils", "GetConnection(string)");
            MethodInfo setLevel = AccessTools.Method(levelManager, "SetLevel", new[] { typeof(int) })
                ?? throw new MissingMethodException("LevelManager", "SetLevel(int)");
            MethodInfo getLevel = AccessTools.Method(levelManager, "GetCurrLevel")
                ?? throw new MissingMethodException("LevelManager", "GetCurrLevel()");

            getCurrLevel = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), getLevel);

            // Workshop cases exist only in newer builds, so every part of this is optional.
            Type modLoader = AccessTools.TypeByName("ModLoader");
            Type modType = AccessTools.TypeByName("Mod");
            if (modLoader != null && modType != null)
            {
                currentModField = AccessTools.Field(modLoader, "currentMod");
                currentModPathField = AccessTools.Field(modLoader, "currentModPath");
                modDatabaseField = AccessTools.Field(modType, "database");
                if (currentModField == null || currentModPathField == null || modDatabaseField == null) currentModField = null;
            }
            MethodInfo moddedConnection = AccessTools.Method(databaseUtils, "GetModdedConnection", Type.EmptyTypes);
            MethodInfo setCustomLevel = AccessTools.Method(levelManager, "SetCustomLevel");

            // Mono.Data.Sqlite's Commit event wraps sqlite3_commit_hook, which fires for every
            // commit on the connection, including autocommit DDL like CREATE TABLE ... AS.
            commitEvent = getConnection.ReturnType.GetEvent("Commit")
                ?? throw new MissingMemberException(getConnection.ReturnType.FullName, "Commit");
            commitHandler = Delegate.CreateDelegate(commitEvent.EventHandlerType,
                typeof(Patches).GetMethod(nameof(OnCommit), BindingFlags.NonPublic | BindingFlags.Static));

            harmony.Patch(getConnection, postfix: new HarmonyMethod(typeof(Patches), nameof(GetConnectionPostfix)));
            harmony.Patch(setLevel, postfix: new HarmonyMethod(typeof(Patches), nameof(SetLevelPostfix)));
            if (moddedConnection != null && currentModField != null)
            {
                harmony.Patch(moddedConnection, postfix: new HarmonyMethod(typeof(Patches), nameof(ModdedConnectionPostfix)));
            }
            if (setCustomLevel != null) harmony.Patch(setCustomLevel, postfix: new HarmonyMethod(typeof(Patches), nameof(SetLevelPostfix)));
        }

        private static void GetConnectionPostfix(string path, object __result)
        {
            if (__result == null || path != "Level " + getCurrLevel()) return;
            commitEvent.AddEventHandler(__result, commitHandler);
        }

        private static void ModdedConnectionPostfix(object __result)
        {
            if (__result == null || getCurrLevel() != CustomLevel) return;
            commitEvent.AddEventHandler(__result, commitHandler);
        }

        private static void OnCommit(object sender, object e) => MirrorService.MarkDirty("commit");

        private static void SetLevelPostfix() => MirrorService.MarkDirty("level change");
    }
}
