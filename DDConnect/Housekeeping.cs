using System.Collections;
using System.Text.RegularExpressions;

namespace DDConnect
{
    /// <summary>
    /// The bridge's cleanup commands, there to make testing repeatable: delete a table the way the game's own
    /// "Delete table" menu does, and close the windows that fill the taskbar. Main thread only.
    /// </summary>
    internal static class Housekeeping
    {
        private static readonly Regex NamePattern = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,63}$");

        public static IEnumerator Drop(MainThread.Pending pending, string name)
        {
            if (name == null || !NamePattern.IsMatch(name))
            {
                pending.Result = BridgeResult.Fail("bad_request", "Give the name of a table: letters, digits and underscores, starting with a letter or underscore.");
                yield break;
            }
            if (QueryRunner.Ready && QueryRunner.Busy)
            {
                pending.Result = BridgeResult.Fail("busy", "A save is still running.");
                yield break;
            }
            UnityEngine.Object button = MainThread.InLevel ? GameUi.FindQueryButton() : null;
            if (button == null)
            {
                pending.Result = BridgeResult.Fail("not_in_level", "Open a case in the game first.");
                yield break;
            }

            string refusal = GameUi.DeleteTable(button, name);
            if (refusal != null)
            {
                pending.Result = BridgeResult.Fail("game_refused", refusal);
                yield break;
            }
            pending.Result = BridgeResult.Success(w => w.Prop("table", name).Prop("message", "Deleted table " + name + "."));
        }

        public static IEnumerator CloseWindows(MainThread.Pending pending)
        {
            int closed = GameUi.CloseWindows();
            pending.Result = BridgeResult.Success(w => w.Prop("closed", closed));
            yield break;
        }
    }
}
