using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DDConnect
{
    /// <summary>
    /// The bridge's save command: runs a query through the game's own search bar and saves the result as a
    /// table. Going through the real QueryButton means every rule the game applies to a player (blocked words,
    /// name checks, parser, time limit, icon cap, row cap) applies here too, the table gets its icon, and the
    /// query is stored so "Restore query" works. Main thread only.
    /// </summary>
    internal static class QueryRunner
    {
        private const float ResultTimeoutSeconds = 12f;
        private const int MaxQueryLength = 20000;

        // What the in-game name box accepts; it also keeps the name safe to type into the box.
        private static readonly Regex NamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,63}$");
        private static readonly Regex RichText = new Regex("<[^>]*>");

        private static ManualLogSource log;
        private static FieldInfo queryInputField;
        private static FieldInfo tableNameField;
        private static FieldInfo saveToggleField;
        private static FieldInfo hasQueryChangedField;
        private static MethodInfo pressMethod;
        private static FieldInfo panelManagerField;
        private static Type panelType;
        private static MethodInfo closePanel;
        private static MethodInfo openWindow;
        private static Type queryInputUtilsType;
        private static MethodInfo highlightMethod;
        private static ICollection<string> blockedWords;

        // Characters the game's search box refuses to let a player type (QueryInputUtils.ILLEGAL_CHARS). The semicolon
        // matters most: without it a query cannot become a second statement.
        private static readonly char[] IllegalChars = { ';', '\r', '\v', '\f' };

        private static volatile bool busy;

        // Written by the Harmony postfixes below while a save is in flight (main thread only).
        private static bool capturing;
        private static string savedTable;
        private static string lastNotice;

        private struct QueryBar
        {
            public string Query;
            public string Name;
            public bool Save;
        }

        public static bool Busy => busy;

        public static void Init(ManualLogSource logger, Harmony harmony)
        {
            log = logger;
            Type queryButton = AccessTools.TypeByName("QueryButton") ?? throw new MissingMemberException("QueryButton");
            queryInputField = AccessTools.Field(queryButton, "queryInput") ?? throw new MissingFieldException("QueryButton", "queryInput");
            tableNameField = AccessTools.Field(queryButton, "tableNameInput") ?? throw new MissingFieldException("QueryButton", "tableNameInput");
            saveToggleField = AccessTools.Field(queryButton, "saveToggle") ?? throw new MissingFieldException("QueryButton", "saveToggle");
            hasQueryChangedField = AccessTools.Field(queryButton, "hasQueryChanged") ?? throw new MissingFieldException("QueryButton", "hasQueryChanged");
            pressMethod = AccessTools.Method(queryButton, "QueryButtonPressed", Type.EmptyTypes)
                ?? throw new MissingMethodException("QueryButton", "QueryButtonPressed()");

            Type restore = AccessTools.TypeByName("RestoreQueryManager") ?? throw new MissingMemberException("RestoreQueryManager");
            Type notifications = AccessTools.TypeByName("NotificationHandler") ?? throw new MissingMemberException("NotificationHandler");
            MethodInfo saveQueryTable = AccessTools.Method(restore, "SaveQueryTable", new[] { typeof(string), typeof(string) })
                ?? throw new MissingMethodException("RestoreQueryManager", "SaveQueryTable(string, string)");
            MethodInfo createPanel = AccessTools.Method(notifications, "CreateNotificationPanel", new[] { typeof(string), typeof(bool) })
                ?? throw new MissingMethodException("NotificationHandler", "CreateNotificationPanel(string, bool)");
            MethodInfo setMessage = AccessTools.Method(notifications, "SetNotificationMessage", new[] { typeof(GameObject), typeof(string) })
                ?? throw new MissingMethodException("NotificationHandler", "SetNotificationMessage(GameObject, string)");

            panelManagerField = AccessTools.Field(queryButton, "panelManager") ?? throw new MissingFieldException("QueryButton", "panelManager");
            panelType = AccessTools.TypeByName("Panel") ?? throw new MissingMemberException("Panel");
            closePanel = AccessTools.Method(panelType, "ClosePanel", Type.EmptyTypes)
                ?? throw new MissingMethodException("Panel", "ClosePanel()");
            Type panelManagerType = AccessTools.TypeByName("PanelManager") ?? throw new MissingMemberException("PanelManager");
            openWindow = AccessTools.Method(panelManagerType, "OpenWindow", new[] { typeof(GameObject) })
                ?? throw new MissingMethodException("PanelManager", "OpenWindow(GameObject)");

            // The game decides what is a blocked word while a player types (QueryInputUtils.HighlightKeywords sets
            // QueryInputUtils.contrabandUsed), and QueryButton only reads that result. Text set from code does not go
            // through the typing checks, so Submit refreshes the word check itself and Validate repeats the rules.
            queryInputUtilsType = AccessTools.TypeByName("QueryInputUtils") ?? throw new MissingMemberException("QueryInputUtils");
            highlightMethod = AccessTools.Method(queryInputUtilsType, "HighlightKeywords", Type.EmptyTypes)
                ?? throw new MissingMethodException("QueryInputUtils", "HighlightKeywords()");
            Type queryParser = AccessTools.TypeByName("QueryParser") ?? throw new MissingMemberException("QueryParser");
            FieldInfo illegal = AccessTools.Field(queryParser, "ILLEGAL_MATERIALS") ?? throw new MissingFieldException("QueryParser", "ILLEGAL_MATERIALS");
            blockedWords = (ICollection<string>)illegal.GetValue(null);

            // The full-taskbar message is a plain popup, not a NotificationHandler one, so it needs its own hook.
            Type taskbar = AccessTools.TypeByName("TaskbarManager") ?? throw new MissingMemberException("TaskbarManager");
            MethodInfo taskbarFull = AccessTools.Method(taskbar, "IsMaximumTaskbarButtons", new[] { typeof(GameObject) })
                ?? throw new MissingMethodException("TaskbarManager", "IsMaximumTaskbarButtons(GameObject)");

            harmony.Patch(taskbarFull, postfix: new HarmonyMethod(typeof(QueryRunner), nameof(TaskbarFullPostfix)));
            harmony.Patch(saveQueryTable, postfix: new HarmonyMethod(typeof(QueryRunner), nameof(SaveQueryTablePostfix)));
            harmony.Patch(createPanel, postfix: new HarmonyMethod(typeof(QueryRunner), nameof(NoticePostfix)));
            harmony.Patch(setMessage, postfix: new HarmonyMethod(typeof(QueryRunner), nameof(NoticePostfix)));
            Ready = true;
        }

        /// <summary>False until <see cref="Init"/> has hooked the game, so the bridge can refuse save cleanly.</summary>
        public static bool Ready { get; private set; }

        public static IEnumerator Save(MainThread.Pending pending, string name, string query)
        {
            if (busy)
            {
                pending.Result = BridgeResult.Fail("busy", "Another save is still running.");
                yield break;
            }
            string invalid = Validate(name, query);
            if (invalid != null)
            {
                pending.Result = BridgeResult.Fail("bad_request", invalid);
                yield break;
            }
            UnityEngine.Object button = MainThread.InLevel ? GameUi.FindQueryButton() : null;
            if (button == null)
            {
                pending.Result = BridgeResult.Fail("not_in_level", "Open a case in the game first.");
                yield break;
            }

            // The search bar's coroutines cannot start while its window is closed, and the game finishes its own
            // setup only when the window is first shown. So open the window the way the game does, and close it after.
            bool openedWindow = false;
            if (!SearchReady(button))
            {
                if (!OpenSearchWindow(button))
                {
                    pending.Result = BridgeResult.Fail("unavailable", "Could not open the game's search window. Open it yourself, then try again.");
                    yield break;
                }
                openedWindow = true;
                float opening = 0f;
                while (!SearchReady(button) && opening < 2f)
                {
                    yield return null;
                    opening += Time.unscaledDeltaTime;
                }
                if (!SearchReady(button))
                {
                    CloseSearchWindow(button);
                    pending.Result = BridgeResult.Fail("timeout", "The game's search window did not open in time.");
                    yield break;
                }
            }

            busy = true;
            QueryBar saved = default;
            bool submitted = false;
            string failure = null;
            try
            {
                try
                {
                    saved = ReadBar(button);
                    savedTable = null;
                    lastNotice = null;
                    capturing = true;
                    Submit(button, name, query);
                    submitted = true;
                }
                catch (Exception ex)
                {
                    log.LogError($"Could not submit the query to the game: {ex}");
                    failure = "Could not submit the query to the game: " + ex.Message;
                }

                if (submitted)
                {
                    float waited = 0f;
                    while (!NameMatches(savedTable, name) && lastNotice == null && waited < ResultTimeoutSeconds)
                    {
                        yield return null;
                        waited += Time.unscaledDeltaTime;
                    }

                    if (NameMatches(savedTable, name))
                    {
                        pending.Result = BridgeResult.Success(w => w.Prop("table", name).Prop("message", "Saved as table " + name + "."));
                    }
                    else
                    {
                        failure = lastNotice ?? "Timed out waiting for the game to finish the query.";
                    }
                }

                capturing = false;
                try
                {
                    if (button != null) WriteBar(button, saved);
                }
                catch (Exception ex)
                {
                    log.LogDebug($"Could not restore the query bar: {ex.Message}");
                }
            }
            finally
            {
                capturing = false;
                busy = false;
                if (openedWindow) CloseSearchWindow(button);
            }

            if (failure != null) pending.Result = BridgeResult.Fail("game_refused", failure);
        }

        /// <summary>The bar is on screen and the game has run its own setup (QueryButton.Start sets panelManager).</summary>
        private static bool SearchReady(UnityEngine.Object button) =>
            GameUi.IsVisible(button) && panelManagerField.GetValue(button) != null;

        private static Component SearchWindow(UnityEngine.Object button) =>
            ((Component)button).GetComponentInParent(panelType, true);

        private static bool OpenSearchWindow(UnityEngine.Object button)
        {
            try
            {
                Component window = SearchWindow(button);
                if (window == null) return false;
                openWindow.Invoke(null, new object[] { window.gameObject });
                return true;
            }
            catch (Exception ex)
            {
                log.LogError($"Could not open the search window: {ex}");
                return false;
            }
        }

        /// <summary>Puts the window back the way it was. If the game objects cannot be closed cleanly it is left open.</summary>
        private static void CloseSearchWindow(UnityEngine.Object button)
        {
            try
            {
                if (button == null) return;
                Component window = SearchWindow(button);
                if (window != null) closePanel.Invoke(window, null);
            }
            catch (Exception ex)
            {
                log.LogDebug($"Could not close the search window again: {ex.Message}");
            }
        }

        private static string Validate(string name, string query)
        {
            if (name == null || !NamePattern.IsMatch(name))
            {
                return "Table names must start with a letter or underscore and use only letters, digits and underscores (64 characters at most).";
            }
            if (string.IsNullOrWhiteSpace(query)) return "The query is empty.";
            if (query.Length > MaxQueryLength) return $"The query is longer than {MaxQueryLength} characters.";
            if (query.IndexOfAny(IllegalChars) >= 0)
            {
                return "The game's search box does not allow semicolons or other line-control characters, so one search is one statement.";
            }
            string word = FindBlockedWord(query);
            if (word != null) return "Illegal word detected! The word " + word + " cannot be used in your query.";
            return null;
        }

        /// <summary>The same test as the game's highlighter: a blocked word standing alone between separators.</summary>
        private static string FindBlockedWord(string query)
        {
            foreach (string word in blockedWords)
            {
                int at = 0;
                while ((at = query.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    bool startOk = at == 0 || IsSeparator(query[at - 1]);
                    int end = at + word.Length;
                    bool endOk = end >= query.Length || IsSeparator(query[end]);
                    if (startOk && endOk) return word;
                    at++;
                }
            }
            return null;
        }

        private static bool IsSeparator(char c) => char.IsWhiteSpace(c) || c == ',' || c == '(' || c == ')' || c == '*';

        private static bool NameMatches(string saved, string name) =>
            saved != null && string.Equals(saved, name, StringComparison.OrdinalIgnoreCase);

        private static QueryBar ReadBar(UnityEngine.Object button)
        {
            return new QueryBar
            {
                Query = (string)GetProperty(queryInputField.GetValue(button), "text"),
                Name = (string)GetProperty(tableNameField.GetValue(button), "text"),
                Save = (bool)GetProperty(saveToggleField.GetValue(button), "isOn"),
            };
        }

        private static void WriteBar(UnityEngine.Object button, QueryBar bar)
        {
            SetProperty(saveToggleField.GetValue(button), "isOn", bar.Save);
            SetProperty(tableNameField.GetValue(button), "text", bar.Name);
            SetProperty(queryInputField.GetValue(button), "text", bar.Query);
        }

        /// <summary>Fills in the query bar exactly as a player would and presses the search button.</summary>
        private static void Submit(UnityEngine.Object button, string name, string query)
        {
            SetProperty(queryInputField.GetValue(button), "text", query);
            SetProperty(tableNameField.GetValue(button), "text", name);
            SetProperty(saveToggleField.GetValue(button), "isOn", true);
            // Recompute the game's blocked-word state for this text, as typing would have; fail closed if it cannot.
            UnityEngine.Object inputUtils = UnityEngine.Object.FindAnyObjectByType(queryInputUtilsType);
            if (inputUtils == null) throw new InvalidOperationException("The game's query box helper was not found.");
            highlightMethod.Invoke(inputUtils, null);
            // Otherwise the game may just reopen the previous result window instead of running the query.
            hasQueryChangedField.SetValue(button, true);
            pressMethod.Invoke(button, null);
        }

        private static object GetProperty(object target, string name) =>
            AccessTools.Property(target.GetType(), name).GetValue(target, null);

        private static void SetProperty(object target, string name, object value) =>
            AccessTools.Property(target.GetType(), name).SetValue(target, value, null);

        private static void SaveQueryTablePostfix(string tableName)
        {
            if (capturing) savedTable = tableName;
        }

        private static void TaskbarFullPostfix(bool __result)
        {
            if (capturing && __result)
            {
                lastNotice = "Your taskbar is full. Close some windows in the game (or run ddsql close-windows), then try again.";
            }
        }

        // Both NotificationHandler methods name their text parameter "message".
        private static void NoticePostfix(string message)
        {
            if (!capturing || string.IsNullOrEmpty(message)) return;
            lastNotice = RichText.Replace(message, "").Trim();
        }
    }
}
