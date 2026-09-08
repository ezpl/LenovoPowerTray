using ChargeKeeper.Helpers;

namespace ChargeKeeper.Services;

/// <summary>A wall-clock instant and the machine's awake time taken together, so a later pair can be
/// compared against it. The awake reading is null on a platform that refused the query.</summary>
internal readonly record struct AwakeMark(DateTimeOffset Wall, TimeSpan? Awake);

/// <summary>Takes those pairs and turns two of them into a <see cref="SleepGap"/>.</summary>
internal static class AwakeClock
{
    public static AwakeMark Mark() => new(DateTimeOffset.Now, NativeMethods.UnbiasedAwakeTime());

    /// <summary>The span since <paramref name="mark"/> split into awake and asleep, or null where
    /// either end has no awake reading — a fabricated gap would be worse than none.</summary>
    public static SleepGap? Since(AwakeMark mark)
    {
        if (mark.Awake is not { } before) return null;
        if (NativeMethods.UnbiasedAwakeTime() is not { } after) return null;

        return new SleepGap(DateTimeOffset.Now - mark.Wall, after - before);
    }
}
