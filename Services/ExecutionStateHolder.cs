using System.Collections.Concurrent;
using ChargeKeeper.Helpers;

namespace ChargeKeeper.Services;

/// <summary>
/// One execution-state hold, owned by a single caller. <c>SetThreadExecutionState</c> is per-thread
/// — the request dies with the thread that made it — so the hold lives on one dedicated background
/// thread fed by a request queue, and releasing means posting <c>ES_CONTINUOUS</c> alone to that
/// same thread.
/// </summary>
/// <remarks>
/// Shared by <see cref="KeepAwakeService"/> and <see cref="LidDelayService"/>, each of which
/// constructs its own instance: the thread type is shared, the session running on it is not.
/// </remarks>
/// <param name="threadName">Name of the dedicated background thread.</param>
/// <param name="logContext">Names the caller in the outcome line, e.g. "keep-awake session".</param>
/// <param name="describeTaken">Renders the "hold taken" log line for the flags being applied.</param>
/// <param name="errorSource">Tag under which a failed call is logged.</param>
internal sealed class ExecutionStateHolder(string threadName, string logContext,
    Func<uint, string> describeTaken, string errorSource)
{
    private readonly BlockingCollection<uint> _requests = new();
    private Thread? _thread;

    /// <summary>Whether the holder thread has been started. A release posted before anything was
    /// ever taken would sit in the queue forever with nothing consuming it.</summary>
    public bool IsStarted => _thread is not null;

    /// <summary>Starts the holder thread if it is not already running. No-op otherwise.</summary>
    public void EnsureStarted()
    {
        if (_thread is not null) return;
        // Background thread: process exit tears it down, which releases the execution state anyway.
        _thread = new Thread(Loop) { IsBackground = true, Name = threadName };
        _thread.Start();
    }

    /// <summary>Queues a flags value to be applied on the holder thread.</summary>
    public void Post(uint flags) => _requests.Add(flags);

    private void Loop()
    {
        foreach (uint flags in _requests.GetConsumingEnumerable())
        {
            try
            {
                // The return value is the thread's previous state, or zero when the call failed. A
                // refusal recorded as nothing is a hold that looks taken while nothing is, so the
                // outcome is named on every take and every release.
                uint previous = NativeMethods.SetThreadExecutionState(flags);
                // Logged here, not at the request sites: this is when the OS learns about the hold.
                PowerLog.Event(
                    flags == NativeMethods.ES_CONTINUOUS ? "OS keep-awake hold released" : describeTaken(flags),
                    $"{logContext}; {ExecutionStateHold.Outcome(previous)}");
            }
            catch (Exception ex) { AppLog.Error(errorSource, ex); }
        }
    }
}
