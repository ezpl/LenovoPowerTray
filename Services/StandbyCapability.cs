using ChargeKeeper.Helpers;

namespace ChargeKeeper.Services;

/// <summary>
/// What sort of sleep this machine does. Recorded once at startup so every later entry in the power
/// trail belongs to a machine of a known kind; nothing in the application branches on it.
/// </summary>
/// <remarks>
/// Read from the OS power capabilities rather than by running <c>powercfg /a</c>: the same in-process
/// call already answers whether the machine has a lid, it costs no process launch, and its answer is
/// a flag rather than localised text that would have to be parsed.
/// </remarks>
internal readonly record struct StandbyCapability(bool ModernStandby, bool SupportsS3)
{
    /// <summary>Asks the platform. Null where the query failed.</summary>
    public static StandbyCapability? Read() =>
        NativeMethods.StandbyFlags() is { } flags
            ? new StandbyCapability(flags.ModernStandby, flags.SupportsS3)
            : null;

    /// <summary>The trail entry for a reading, including the one that could not be taken.</summary>
    public static string Describe(StandbyCapability? capability) => capability switch
    {
        { ModernStandby: true } => "This machine sleeps by Modern Standby (S0 low-power idle)",
        { SupportsS3: true }    => "This machine sleeps by traditional S3 suspend-to-RAM",
        { }                     => "This machine reports neither Modern Standby nor S3 sleep",
        null                    => "This machine's sleep type could not be read from the OS power capabilities",
    };
}
