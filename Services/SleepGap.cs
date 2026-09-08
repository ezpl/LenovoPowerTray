namespace ChargeKeeper.Services;

/// <summary>
/// How much of a span the machine was actually awake for. Elapsed time on its own says nothing: a
/// suspended process resumes and carries on counting, so a wait that was asleep almost throughout
/// and one that was held awake produce the same elapsed reading.
/// </summary>
/// <remarks>
/// The awake side comes from the platform's unbiased interrupt time, which stops while the machine
/// is suspended; the wall side from the ordinary clock, which does not. The difference is the time
/// spent asleep. Nothing branches on it — it is a reading, put where a reader of the power trail
/// will see it.
/// <para>The wording names a lid-close wait, the only span measured this way.</para>
/// </remarks>
internal readonly record struct SleepGap(TimeSpan Wall, TimeSpan Awake)
{
    /// <summary>
    /// Below this a difference is timer granularity or a clock correction rather than a sleep.
    /// Deliberately short: the standby entry this exists to catch happened half a minute into a two
    /// hour wait, so a threshold in minutes would have missed the case it was written for.
    /// </summary>
    public const int SmallestSleepSeconds = 5;

    /// <inheritdoc cref="SmallestSleepSeconds"/>
    public static TimeSpan SmallestSleep => TimeSpan.FromSeconds(SmallestSleepSeconds);

    /// <summary>Wall-clock time the machine spent suspended. Never negative: the awake counter can
    /// read marginally ahead of the wall clock, which is granularity, not time travel.</summary>
    public TimeSpan Slept => Wall - Awake is { Ticks: > 0 } slept ? slept : TimeSpan.Zero;

    /// <summary>Whether the machine went away during the span, rather than merely ticking.</summary>
    public bool MachineSlept => Slept >= SmallestSleep;

    /// <summary>The reading as a clause for the cause of an event line, or null where there is no
    /// sleep to state.</summary>
    public string? Fragment() =>
        MachineSlept
            ? $"the wait spent {SleepWatch.Duration(Slept)} asleep and {SleepWatch.Duration(Awake)} awake"
            : null;

    /// <summary>The same reading as its own sentence, for a line that is already one. It names how
    /// it knows, because the elapsed figure beside it says something different.</summary>
    public string? Sentence() =>
        Fragment() is { } fragment ? $"Measured against the clock, {fragment}." : null;

    /// <summary>Adds the clause to an event line's cause, where there is one to add.</summary>
    public static string AddTo(string cause, SleepGap? gap) =>
        gap?.Fragment() is { } fragment ? $"{cause}; {fragment}" : cause;

    /// <summary>Adds the sentence to a line already written as one, where there is one to add.</summary>
    public static string AddSentenceTo(string line, SleepGap? gap) =>
        gap?.Sentence() is { } sentence ? $"{line} {sentence}" : line;
}
