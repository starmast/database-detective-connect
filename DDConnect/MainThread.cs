using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace DDConnect
{
    /// <summary>Outcome of one bridge command, turned into a reply line by the server.</summary>
    internal sealed class BridgeResult
    {
        public bool Ok;
        public string Code;
        public string Message;
        public Action<JsonWriter> Fill;

        public static BridgeResult Success(Action<JsonWriter> fill = null) => new BridgeResult { Ok = true, Fill = fill };

        public static BridgeResult Fail(string code, string message) => new BridgeResult { Code = code, Message = message };

        public string ToJson(long id)
        {
            var w = new JsonWriter().BeginObject().Prop("id", id).Prop("ok", Ok);
            if (Ok)
            {
                Fill?.Invoke(w);
            }
            else
            {
                w.BeginObject("error").Prop("code", Code).Prop("message", Message).EndObject();
            }
            return w.EndObject().ToString();
        }
    }

    /// <summary>
    /// Runs bridge commands that touch Unity or the game's objects on the main thread, and caches a
    /// couple of facts (current level, whether a case is open) so connection threads never have to ask.
    /// </summary>
    internal static class MainThread
    {
        public sealed class Pending
        {
            public Func<Pending, IEnumerator> Body;
            public BridgeResult Result;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public volatile bool Abandoned;
        }

        private const float ProbeSeconds = 0.5f;

        private static readonly ConcurrentQueue<Pending> queue = new ConcurrentQueue<Pending>();
        private static ManualLogSource log;
        private static float nextProbe;

        /// <summary>The level the game currently has loaded (0 is the main menu).</summary>
        public static volatile int Level;

        /// <summary>True when a case is open, i.e. the query bar exists.</summary>
        public static volatile bool InLevel;

        public static void Init(ManualLogSource logger) => log = logger;

        /// <summary>Called from any thread. Waits for the main thread to run <paramref name="body"/> and returns what it produced.</summary>
        public static BridgeResult Run(Func<Pending, IEnumerator> body, int timeoutMs)
        {
            var pending = new Pending { Body = body };
            queue.Enqueue(pending);
            if (!pending.Done.Wait(timeoutMs))
            {
                pending.Abandoned = true;
                return BridgeResult.Fail("timeout", "The game did not respond in time.");
            }
            return pending.Result ?? BridgeResult.Fail("internal", "The command produced no result.");
        }

        /// <summary>Call every frame from the plugin's Update.</summary>
        public static void Pump(MonoBehaviour host)
        {
            Level = Patches.CurrentLevel();
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + ProbeSeconds;
                InLevel = GameUi.Ready && Level > 0 && GameUi.FindQueryButton() != null;
            }

            while (queue.TryDequeue(out Pending pending))
            {
                if (!pending.Abandoned) host.StartCoroutine(Drive(pending));
            }
        }

        private static IEnumerator Drive(Pending pending)
        {
            try
            {
                IEnumerator body = pending.Body(pending);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = body.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        log.LogError($"Bridge command failed: {ex}");
                        pending.Result = BridgeResult.Fail("internal", ex.Message);
                        break;
                    }
                    if (!more) break;
                    yield return body.Current;
                }
            }
            finally
            {
                pending.Done.Set();
            }
        }
    }
}
