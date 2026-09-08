using ChargeKeeper.Helpers;

namespace ChargeKeeper.Services;

/// <summary>
/// What Windows made of a <c>SetThreadExecutionState</c> call. The call returns the thread's previous
/// state, or zero when it failed, and both the lid-close delay and keep-awake stand or fall on that
/// answer — an unrecorded refusal is a feature that looks like it is working.
/// </summary>
internal static class ExecutionStateHold
{
    /// <summary>The clause for a call that returned <paramref name="previous"/>.</summary>
    public static string Outcome(uint previous) =>
        previous == 0
            ? "Windows refused it"
            : $"Windows accepted it, replacing {Held(previous)}";

    /// <summary>What the thread was holding before the call, in the words the trail uses.</summary>
    private static string Held(uint state) => (state & NativeMethods.ES_SYSTEM_REQUIRED) != 0
        ? ((state & NativeMethods.ES_DISPLAY_REQUIRED) != 0 ? "a system and display hold" : "a system hold")
        : ((state & NativeMethods.ES_DISPLAY_REQUIRED) != 0 ? "a display hold" : "no hold");
}
