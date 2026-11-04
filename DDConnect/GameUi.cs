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
        private static MethodInfo getSavedQueries;
        private static object warningIcon;

        // What the game's own "Delete table" menu entry touches (IconRightClickController.DeleteTable).
        private static Type iconComponentType;
        private static Type panelManagerType;
        private static Type clickDragType;
        private static Type queryInputUtilsType;
        private static MethodInfo iconName;
        private static MethodInfo isDeletable;
        private static MethodInfo destroyPanel;
        private static MethodInfo removeIconPos;
        private static MethodInfo removeQueryTable;
        private static MethodInfo removeIconPosition;
        private static MethodInfo clickDragRemoveIcon;
        private static MethodInfo clickDragClearSelection;
        private static MethodInfo highlightKeywords;

        // Windows that hold a taskbar button, and how the game closes one.
        private static FieldInfo windowsToTaskbars;
        private static MethodInfo getTaskbar;
        private static Type panelType;
        private static MethodInfo closePanel;

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
            // Newer builds added a third parameter (closeConnection); accept either.
            dropTable = AccessTools.Method(databaseUtils, "DropTable", new[] { typeof(string), typeof(IDbConnection), typeof(bool) })
                ?? Method(databaseUtils, "DropTable", typeof(string), typeof(IDbConnection));
            isTableNameValid = Method(iconGenerator, "IsTableNameValid", notifications, typeof(string));
            createIcon = Method(iconGenerator, "CreateIcon", typeof(string));
            createUserPanel = Method(createPanels, "CreateUserPanel", typeof(string), typeof(bool), typeof(string));
            addIcon = Method(Resolve("ThomasGridLayoutGroup"), "AddIcon", typeof(Transform), typeof(bool));
            Type iconType = AccessTools.Inner(notifications, "Icon") ?? throw new MissingMemberException("NotificationHandler.Icon");
            warningIcon = Enum.Parse(iconType, "WARNING");
            createNotification = Method(notifications, "CreateNotificationPanel", typeof(string), iconType, typeof(string));
            openWindow = Method(Resolve("PanelManager"), "OpenWindow", typeof(GameObject));
            getSavedQueries = Method(Resolve("RestoreQueryManager"), "GetSavedQueries");
            Ready = true;

            // Housekeeping is a testing aid; if the game changed under it, searches and queries should still work.
            try
            {
                InitHousekeeping();
                HousekeepingReady = true;
            }
            catch (Exception ex) when (ex is MissingMemberException || ex is AmbiguousMatchException)
            {
                HousekeepingError = ex.Message;
            }
        }

        /// <summary>False when the game's delete and window-close code could not be hooked; see <see cref="HousekeepingError"/>.</summary>
        public static bool HousekeepingReady { get; private set; }

        public static string HousekeepingError { get; private set; }

        private static void InitHousekeeping()
        {
            iconComponentType = Resolve("Icon");
            panelManagerType = Resolve("PanelManager");
            clickDragType = Resolve("ClickDrag");
            queryInputUtilsType = Resolve("QueryInputUtils");
            iconName = Method(iconComponentType, "GetName", iconComponentType);
            isDeletable = Method(panelManagerType, "IsDeletable", typeof(string));
            destroyPanel = Method(panelManagerType, "DestroyPanel", typeof(string));
            removeIconPos = Method(Resolve("ThomasGridLayoutGroup"), "RemoveIconPos", typeof(Vector2));
            removeQueryTable = Method(Resolve("RestoreQueryManager"), "RemoveQueryTable", typeof(string));
            removeIconPosition = Method(Resolve("Save"), "RemoveIconPosition", typeof(string), typeof(bool));
            clickDragRemoveIcon = Method(clickDragType, "RemoveIcon", iconComponentType);
            clickDragClearSelection = Method(clickDragType, "ClearSelectedIcons");
            highlightKeywords = Method(queryInputUtilsType, "HighlightKeywords");

            Type taskbarManager = Resolve("TaskbarManager");
            windowsToTaskbars = Field(taskbarManager, "windowsToTaskbars");
            getTaskbar = Method(taskbarManager, "GetTaskbar");
            panelType = Resolve("Panel");
            closePanel = Method(panelType, "ClosePanel");
        }

        /// <summary>False until <see cref="Init"/> has resolved every game member.</summary>
        public static bool Ready { get; private set; }

        /// <summary>Saved searches of the current case, keyed by table name.</summary>
        public static Dictionary<string, string> SavedQueries() => (Dictionary<string, string>)Invoke(getSavedQueries, null, null);

        /// <summary>The query bar only exists inside a case, so this doubles as an "is a case loaded" check.</summary>
        public static UnityEngine.Object FindQueryButton() =>
            UnityEngine.Object.FindAnyObjectByType(queryButtonType, FindObjectsInactive.Include);

        /// <summary>True when the query bar is on screen. A hidden bar cannot run searches (its coroutines will not start).</summary>
        public static bool IsVisible(UnityEngine.Object button) => ((Component)button).gameObject.activeInHierarchy;

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

        public static void DropTable(string name) =>
            Invoke(dropTable, null, dropTable.GetParameters().Length == 3 ? new object[] { name, null, true } : new object[] { name, null });

        /// <summary>
        /// Deletes a table the way the icon's "Delete table" menu entry does: query record, icon position, the table
        /// itself, its window and its desktop icon. Returns null on success, otherwise why the game would not allow it.
        /// </summary>
        public static string DeleteTable(UnityEngine.Object button, string name)
        {
            Component icon = FindIcon(button, name, out string actual);
            if (icon == null) return $"There is no table named {name} on the desktop.";

            UnityEngine.Object tables = UnityEngine.Object.FindAnyObjectByType(panelManagerType);
            if (tables == null) throw new InvalidOperationException("The game's table manager was not found.");
            // The case's own tables are not deletable in the game, so they are not here either.
            if (!(bool)Invoke(isDeletable, tables, new object[] { actual })) return $"The game does not let you delete {actual}; it is part of the case.";

            Transform holder = icon.transform.parent;
            Component clickDrag = icon.GetComponentInParent(clickDragType);
            Invoke(removeIconPos, null, new object[] { (Vector2)holder.localPosition });
            Invoke(removeQueryTable, null, new object[] { actual });
            Invoke(removeIconPosition, null, new object[] { actual, false });
            DropTable(actual);
            Invoke(destroyPanel, tables, new object[] { actual });
            if (clickDrag != null) Invoke(clickDragRemoveIcon, clickDrag, new object[] { icon });
            // The game refreshes its blocked-word check after a delete, since a table name can be on the list.
            UnityEngine.Object inputUtils = UnityEngine.Object.FindAnyObjectByType(queryInputUtilsType);
            if (inputUtils != null) Invoke(highlightKeywords, inputUtils, null);
            if (clickDrag != null) Invoke(clickDragClearSelection, clickDrag, null);
            UnityEngine.Object.Destroy(holder.gameObject);
            return null;
        }

        private static Component FindIcon(UnityEngine.Object button, string name, out string actual)
        {
            actual = null;
            Transform container = ((Component)iconConstructorField.GetValue(button)).transform;
            foreach (Transform holder in container)
            {
                Component icon = holder.GetComponentInChildren(iconComponentType, true);
                if (icon == null) continue;
                string text;
                try
                {
                    text = (string)Invoke(iconName, null, new object[] { icon });
                }
                catch (Exception)
                {
                    continue; // not a table icon (no "Table Name" label)
                }
                if (!string.Equals(text, name, StringComparison.OrdinalIgnoreCase)) continue;
                actual = text;
                return icon;
            }
            return null;
        }

        /// <summary>Closes every window that has a taskbar button, as pressing each one's close button would. Returns how many.</summary>
        public static int CloseWindows()
        {
            var windows = windowsToTaskbars.GetValue(null) as System.Collections.IDictionary;
            if (windows == null) return 0;

            // Closing removes the entry, so work from a copy.
            var open = new List<GameObject>();
            foreach (System.Collections.DictionaryEntry entry in windows)
            {
                var window = entry.Key as GameObject;
                if (window != null) open.Add(window);
            }

            int closed = 0;
            foreach (GameObject window in open)
            {
                if (window == null) continue;
                Component panel = window.GetComponent(panelType);
                if (panel == null) continue;
                Invoke(closePanel, panel, null);
                closed++;
            }
            return closed;
        }

        /// <summary>
        /// True while taskbar buttons are still sliding away after their windows closed. The game counts them toward
        /// "taskbar is full" until the animation ends, so a search started right after closing would be refused.
        /// </summary>
        public static bool TaskbarDraining()
        {
            var windows = windowsToTaskbars.GetValue(null) as System.Collections.IDictionary;
            var taskbar = (Component)Invoke(getTaskbar, null, null);
            return taskbar != null && taskbar.transform.childCount > (windows?.Count ?? 0);
        }

        public static void Notify(UnityEngine.Object button, string message)
        {
            // The game's red "Error" alarm is for refused searches; a notice about editor tables is a warning, not an error.
            var window = (GameObject)Invoke(createNotification, errorHandlerField.GetValue(button), new[] { "SQL editor", warningIcon, message });
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
