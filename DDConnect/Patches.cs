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

        public static int CurrentLevel() => getCurrLevel();

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

            // Mono.Data.Sqlite's Commit event wraps sqlite3_commit_hook, which fires for every
            // commit on the connection, including autocommit DDL like CREATE TABLE ... AS.
            commitEvent = getConnection.ReturnType.GetEvent("Commit")
                ?? throw new MissingMemberException(getConnection.ReturnType.FullName, "Commit");
            commitHandler = Delegate.CreateDelegate(commitEvent.EventHandlerType,
                typeof(Patches).GetMethod(nameof(OnCommit), BindingFlags.NonPublic | BindingFlags.Static));

            harmony.Patch(getConnection, postfix: new HarmonyMethod(typeof(Patches), nameof(GetConnectionPostfix)));
            harmony.Patch(setLevel, postfix: new HarmonyMethod(typeof(Patches), nameof(SetLevelPostfix)));
        }

        private static void GetConnectionPostfix(string path, object __result)
        {
            if (__result == null || path != "Level " + getCurrLevel()) return;
            commitEvent.AddEventHandler(__result, commitHandler);
        }

        private static void OnCommit(object sender, object e) => MirrorService.MarkDirty("commit");

        private static void SetLevelPostfix() => MirrorService.MarkDirty("level change");
    }
}
