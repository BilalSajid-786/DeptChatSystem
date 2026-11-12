using System.Diagnostics;

namespace DeptChat.Mobile.Services;

/// <summary>
/// Internal timing diagnostics for the staff department → customer list flow.
/// Every recording method is [Conditional("DEPTCHAT_TIMING")], so in normal builds the
/// compiler removes the calls entirely. Only a build made with -p:DeptChatTiming=true records anything.
/// </summary>
public static class PerfTrace
{
    private const double SlowFrameMs = 34;
    private const double FreezeMs = 100;
    private const int MaxSteps = 22;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly List<string> Steps = new();

    private static long _tapAtMs = -1;

    // "Leave" trace: from leaving the customer list (e.g. Back) for the next few seconds.
    private const long LeaveWindowMs = 3000;
    private static readonly List<string> LeaveSteps = new();
    private static long _leaveAtMs = -1;
    private static int _leaveFrames;
    private static int _leaveSlowFrames;
    private static double _leaveWorstFrameMs;

    // Chat rows shown (by layout type) and .NET GC counts since the previous frame,
    // reported on each freeze so we can see whether pictures or memory cleanup caused it.
    private static int _rowsText, _rowsImage, _rowsDocument, _rowsVoice;
    private static int _lastGc0 = -1, _lastGc1, _lastGc2;

    private static bool LeaveActive => _leaveAtMs >= 0 && Clock.ElapsedMilliseconds - _leaveAtMs < LeaveWindowMs;

    private static int _deptReloadCount;
    private static long _deptReloadLastMs;
    private static long _deptReloadTotalMs;

    private static int _slowFrames;
    private static int _framesSeen;
    private static double _worstFrameMs;
    private static long _worstFrameEndedAtMs;

    /// <summary>Milliseconds since app start. Cheap; safe to call in any build.</summary>
    public static long Now => Clock.ElapsedMilliseconds;

    public static event Action? Changed;

    [Conditional("DEPTCHAT_TIMING")]
    public static void MarkTap(string departmentName)
    {
        lock (Gate)
        {
            _tapAtMs = Now;
            Steps.Clear();
            Steps.Add($"Tap: {departmentName}");
            _slowFrames = 0;
            _framesSeen = 0;
            _worstFrameMs = 0;
            _worstFrameEndedAtMs = 0;
        }
#if ANDROID && DEPTCHAT_TIMING
        // Watch frames from the moment of the tap, so a freeze while the page is being
        // built or first drawn is measured too.
        FrameWatcher.Instance.Start();
#endif
        Changed?.Invoke();
    }

    /// <summary>Records a step with the time elapsed since the last department tap.</summary>
    [Conditional("DEPTCHAT_TIMING")]
    public static void Step(string label)
    {
        lock (Gate)
        {
            AddStepLocked(label);
        }
        Changed?.Invoke();
    }

    /// <summary>Resumes frame watching when the page reappears (e.g. back from a chat).</summary>
    [Conditional("DEPTCHAT_TIMING")]
    public static void StartFrames()
    {
#if ANDROID && DEPTCHAT_TIMING
        FrameWatcher.Instance.Start();
#endif
    }

    [Conditional("DEPTCHAT_TIMING")]
    public static void StopFrames()
    {
#if ANDROID && DEPTCHAT_TIMING
        FrameWatcher.Instance.Stop();
#endif
    }

    /// <summary>Starts the leave trace (called when the customer list disappears).</summary>
    [Conditional("DEPTCHAT_TIMING")]
    public static void MarkLeave(string label)
    {
        lock (Gate)
        {
            _leaveAtMs = Now;
            LeaveSteps.Clear();
            LeaveSteps.Add($"Last leave: {label}");
            _leaveFrames = 0;
            _leaveSlowFrames = 0;
            _leaveWorstFrameMs = 0;
        }
#if ANDROID && DEPTCHAT_TIMING
        FrameWatcher.Instance.EnsurePosted();
#endif
    }

    /// <summary>Records a step in the leave trace, if one is running.</summary>
    [Conditional("DEPTCHAT_TIMING")]
    public static void LeaveStep(string label)
    {
        lock (Gate)
        {
            if (!LeaveActive) return;
            LeaveSteps.Add($"  +{Now - _leaveAtMs,5} ms  {label}");
            if (LeaveSteps.Count > 12) LeaveSteps.RemoveAt(1);
        }
    }

    /// <summary>Counts a chat row being shown with the given layout ("text", "image", "document", "voice").</summary>
    [Conditional("DEPTCHAT_TIMING")]
    public static void RowShown(string kind)
    {
        lock (Gate)
        {
            switch (kind)
            {
                case "image": _rowsImage++; break;
                case "document": _rowsDocument++; break;
                case "voice": _rowsVoice++; break;
                default: _rowsText++; break;
            }
        }
    }

    /// <summary>What happened during the frame that just ended: GC runs and rows shown.</summary>
    private static string TakeFrameDetailsLocked()
    {
        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);
        var details = _lastGc0 < 0
            ? string.Empty
            : $" gc {g0 - _lastGc0}/{g1 - _lastGc1}/{g2 - _lastGc2}"
              + (_rowsText + _rowsImage + _rowsDocument + _rowsVoice > 0
                  ? $" rows t{_rowsText} i{_rowsImage} d{_rowsDocument} v{_rowsVoice}"
                  : string.Empty);
        _lastGc0 = g0; _lastGc1 = g1; _lastGc2 = g2;
        _rowsText = _rowsImage = _rowsDocument = _rowsVoice = 0;
        return details;
    }

    [Conditional("DEPTCHAT_TIMING")]
    public static void DepartmentsReloaded(long startedAtMs)
    {
        lock (Gate)
        {
            var took = Now - startedAtMs;
            _deptReloadCount++;
            _deptReloadLastMs = took;
            _deptReloadTotalMs += took;
        }
        Changed?.Invoke();
    }

    [Conditional("DEPTCHAT_TIMING")]
    public static void Frame(double frameMs)
    {
        var freeze = false;
        lock (Gate)
        {
            var details = TakeFrameDetailsLocked();
            if (LeaveActive)
            {
                _leaveFrames++;
                if (frameMs > SlowFrameMs) _leaveSlowFrames++;
                if (frameMs > _leaveWorstFrameMs) _leaveWorstFrameMs = frameMs;
                if (frameMs > FreezeMs)
                {
                    LeaveSteps.Add($"  +{Now - _leaveAtMs,5} ms  FREEZE {frameMs:0} ms{details}");
                    if (LeaveSteps.Count > 12) LeaveSteps.RemoveAt(1);
                }
                return;
            }

            _framesSeen++;
            if (frameMs > SlowFrameMs) _slowFrames++;
            if (frameMs > _worstFrameMs)
            {
                _worstFrameMs = frameMs;
                _worstFrameEndedAtMs = _tapAtMs >= 0 ? Now - _tapAtMs : 0;
            }

            if (frameMs > FreezeMs)
            {
                // Logged when the freeze ends; it started about frameMs earlier.
                AddStepLocked($"FREEZE {frameMs:0} ms{details}");
                freeze = true;
            }
        }
        if (freeze) Changed?.Invoke();
    }

    private static void AddStepLocked(string label)
    {
        if (_tapAtMs < 0) return;
        Steps.Add($"+{Now - _tapAtMs,5} ms  {label}");
        if (Steps.Count > MaxSteps) Steps.RemoveAt(1);
    }

    public static string Snapshot()
    {
        lock (Gate)
        {
            var lines = new List<string>(Steps)
            {
                $"Dept-screen reloads: {_deptReloadCount} (last {_deptReloadLastMs} ms, total {_deptReloadTotalMs} ms)",
                $"Frames since tap: {_framesSeen}, slow (>34ms): {_slowFrames}, worst: {_worstFrameMs:0} ms (ended +{_worstFrameEndedAtMs} ms)"
            };
            if (LeaveSteps.Count > 0)
            {
                lines.Add("----");
                lines.AddRange(LeaveSteps);
                lines.Add($"  Leave frames: {_leaveFrames}, slow: {_leaveSlowFrames}, worst: {_leaveWorstFrameMs:0} ms");
            }
            return string.Join("\n", lines);
        }
    }

#if ANDROID && DEPTCHAT_TIMING
    /// <summary>
    /// Measures the gap between screen frames using Android's Choreographer.
    /// The first gap is measured from Start(), so a freeze right after the tap is captured.
    /// </summary>
    private sealed class FrameWatcher : Java.Lang.Object, Android.Views.Choreographer.IFrameCallback
    {
        public static readonly FrameWatcher Instance = new();

        private long _lastFrameNanos;
        private bool _running;
        private bool _callbackPosted;

        public void Start()
        {
            _lastFrameNanos = Java.Lang.JavaSystem.NanoTime();
            _running = true;
            if (!_callbackPosted)
            {
                _callbackPosted = true;
                Android.Views.Choreographer.Instance?.PostFrameCallback(this);
            }
        }

        public void Stop() => _running = false;

        /// <summary>Keeps frame callbacks going (used by the leave trace even after the page stopped).</summary>
        public void EnsurePosted()
        {
            if (_callbackPosted) return;
            _lastFrameNanos = Java.Lang.JavaSystem.NanoTime();
            _callbackPosted = true;
            Android.Views.Choreographer.Instance?.PostFrameCallback(this);
        }

        public void DoFrame(long frameTimeNanos)
        {
            _callbackPosted = false;
            if (!_running && !LeaveActive) return;

            if (_lastFrameNanos > 0 && frameTimeNanos > _lastFrameNanos)
            {
                Frame((frameTimeNanos - _lastFrameNanos) / 1_000_000.0);
            }
            _lastFrameNanos = frameTimeNanos;

            _callbackPosted = true;
            Android.Views.Choreographer.Instance?.PostFrameCallback(this);
        }
    }
#endif
}
