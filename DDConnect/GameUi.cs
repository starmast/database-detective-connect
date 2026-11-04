using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DDConnect
{
    /// <summary>
    /// Reflection access to the game's table UI (icons, panels, notifications). Game types are
    /// resolved by name so the plugin compiles without the game's assemblies. All members must
    /// be used from the main thread.
    /// </summary>
    internal static class GameUi
    {
        private static Type queryButtonType;
        private static FieldInfo panelConstructorField;
        private static FieldInfo iconConstructorField;
        private static FieldInfo errorHandlerField;

        private static MethodInfo getAllTableNames;
        private static MethodInfo dropTable;
        private static MethodInfo isTableNameValid;
        private static MethodInfo createUserPanel;
        private static MethodInfo createIcon;
        private static MethodInfo addIcon;
        private static MethodInfo createNotification;
        private static MethodInfo openWindow;

        /// <summary>Resolves everything up front so a game update fails loudly at startup, not mid-session.</summary>
        public static void Init()
        {
            queryButtonType = Resolve("QueryButton");
            panelConstructorField = Field(queryButtonType, "panelConstructor");
            iconConstructorField = Field(queryButtonType, "iconConstructor");
            errorHandlerField = Field(queryButtonType, "errorHandler");

            Type databaseUtils = Resolve("DatabaseUtils");
            Type iconGenerator = Resolve("IconGenerator");
            Type createPanels = Resolve("CreatePanels");
            Type notifications = Resolve("NotificationHandler");

            getAllTableNames = Method(databaseUtils, "GetAllTableNames", typeof(IDbConnection));
            dropTable = Method(databaseUtils, "DropTable", typeof(string), typeof(IDbConnection));
            isTableNameValid = Method(iconGenerator, "IsTableNameValid", notifications, typeof(string));
            createIcon = Method(iconGenerator, "CreateIcon", typeof(string));
            createUserPanel = Method(createPanels, "CreateUserPanel", typeof(string), typeof(bool), typeof(string));
            addIcon = Method(Resolve("ThomasGridLayoutGroup"), "AddIcon", typeof(Transform), typeof(bool));
            createNotification = Method(notifications, "CreateNotificationPanel", typeof(string), typeof(bool));
            openWindow = Method(Resolve("PanelManager"), "OpenWindow", typeof(GameObject));
        }

        /// <summary>The query bar only exists inside a case, so this doubles as an "is a case loaded" check.</summary>
        public static UnityEngine.Object FindQueryButton() => UnityEngine.Object.FindAnyObjectByType(queryButtonType);

        public static int IconCount(UnityEngine.Object button) =>
            ((Component)iconConstructorField.GetValue(button)).transform.childCount;

        public static HashSet<string> TableNames() => (HashSet<string>)Invoke(getAllTableNames, null, new object[] { null });

        /// <summary>The game's own name check. On failure the game also shows its own message to the player.</summary>
        public static bool IsTableNameValid(UnityEngine.Object button, string name) =>
            (bool)Invoke(isTableNameValid, null, new[] { errorHandlerField.GetValue(button), name });

        /// <summary>Builds the panel and desktop icon for a table that already exists, as GenerateIcons does on load.</summary>
        public static void ShowTable(UnityEngine.Object button, string name)
        {
            var panel = (GameObject)Invoke(createUserPanel, panelConstructorField.GetValue(button), new object[] { name, true, null });
            panel.SetActive(false);
            var icon = (GameObject)Invoke(createIcon, iconConstructorField.GetValue(button), new object[] { name });
            Invoke(addIcon, null, new object[] { icon.transform, true });
        }

        public static void DropTable(string name) => Invoke(dropTable, null, new object[] { name, null });

        public static void Notify(UnityEngine.Object button, string message)
        {
            var window = (GameObject)Invoke(createNotification, errorHandlerField.GetValue(button), new object[] { message, false });
            Invoke(openWindow, null, new object[] { window });
        }

        /// <summary>Calls through reflection but surfaces the game's own exception (its messages are player-friendly).</summary>
        private static object Invoke(MethodInfo method, object target, object[] args)
        {
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static Type Resolve(string name) => AccessTools.TypeByName(name) ?? throw new MissingMemberException(name);

        private static FieldInfo Field(Type type, string name) =>
            AccessTools.Field(type, name) ?? throw new MissingFieldException(type.Name, name);

        private static MethodInfo Method(Type type, string name, params Type[] parameters) =>
            AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.Name, name);
    }
}
