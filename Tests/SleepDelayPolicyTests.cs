using System.Text.RegularExpressions;
using ChargeKeeper.Services;
using Xunit;

namespace ChargeKeeper.Tests;

// What Windows does once nothing holds the machine awake: the wording, and a guard on the two
// identifiers the reading depends on.
public class SleepDelayPolicyTests
{
    private static string NativeMethodsSource() =>
        File.ReadAllText(RepoFiles.Find(Path.Combine("Helpers", "NativeMethods.cs")));

    /// <summary>Matches a GUID field declaration whatever the alignment whitespace happens to be.</summary>
    private static bool DeclaresGuid(string source, string field, string guid) =>
        Regex.IsMatch(source, $@"\b{Regex.Escape(field)}\s*=\s*new\(""{Regex.Escape(guid)}""\)");

    // Guards. These two identifiers are published by Windows and are the whole of what makes the
    // reading point at the sleep delay rather than at some other per-scheme setting. A wrong pair
    // does not fail: it returns a plausible number for something else, which is then stated to the
    // user as the moment their machine sleeps. Pinned as literals for that reason.

    [Fact]
    public void SleepSubgroupGuid_IsTheOneWindowsPublishes() =>
        Assert.True(DeclaresGuid(NativeMethodsSource(), "GUID_SUB_SLEEP",
                                 "238c9fa8-0aad-41ed-83f4-97be242c8f20"),
                    "The sleep subgroup GUID is no longer the one Windows publishes.");

    [Fact]
    public void StandbyIdleGuid_IsTheOneWindowsPublishes() =>
        Assert.True(DeclaresGuid(NativeMethodsSource(), "GUID_STANDBYIDLE",
                                 "29f6c1db-86da-48c5-9fdb-f2b67b1f44da"),
                    "The standby-idle setting GUID is no longer the one Windows publishes.");

    /// <summary>
    /// The declaration of <c>ReadSleepDelay</c>, which is expression-bodied like the lid-action read
    /// beside it and so cannot be pulled out by <see cref="SourceMethods"/>. It ends at the
    /// wrapper's fallback argument, which is the last thing on its final line.
    /// </summary>
    private static string ReadSleepDelayDeclaration()
    {
        string source = NativeMethodsSource();
        int start = source.IndexOf("ReadSleepDelay()", StringComparison.Ordinal);
        Assert.True(start >= 0, "NativeMethods no longer declares ReadSleepDelay.");

        int end = source.IndexOf("}, null);", start, StringComparison.Ordinal);
        Assert.True(end > start, "ReadSleepDelay no longer ends at the active-scheme wrapper.");
        return source[start..end];
    }

    [Fact]
    public void ReadSleepDelay_ReadsTheSleepSettingAndWritesNothing()
    {
        // The reading is a P/Invoke pair no test can stand up, so the guard is on where it points.
        // The write calls sit in the same file against the same wrapper, and a sleep delay written
        // by accident would change when the machine sleeps — the one thing #174 must not do.
        string declaration = ReadSleepDelayDeclaration();

        Assert.Contains("GUID_SUB_SLEEP", declaration, StringComparison.Ordinal);
        Assert.Contains("GUID_STANDBYIDLE", declaration, StringComparison.Ordinal);
        Assert.Contains("PowerReadACValueIndex", declaration, StringComparison.Ordinal);
        Assert.Contains("PowerReadDCValueIndex", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerWrite", declaration, StringComparison.Ordinal);
    }

    // ForPowerSource

    [Fact]
    public void ForPowerSource_OnMains_TakesTheAcValue() =>
        Assert.Equal(18000u, SleepDelayPolicy.ForPowerSource((18000u, 600u), onBattery: false));

    [Fact]
    public void ForPowerSource_OnBattery_TakesTheDcValue() =>
        Assert.Equal(600u, SleepDelayPolicy.ForPowerSource((18000u, 600u), onBattery: true));

    [Fact]
    public void ForPowerSource_FailedRead_StaysNull() =>
        // Null must not collapse to zero: zero is a promise that nothing sleeps the machine.
        Assert.Null(SleepDelayPolicy.ForPowerSource(null, onBattery: false));

    // Describe

    [Fact]
    public void Describe_FailedRead_SaysNothing() =>
        Assert.Null(SleepDelayPolicy.Describe(null, onBattery: false));

    [Fact]
    public void Describe_OnMains_NamesThePeriodAndTheSource()
    {
        string? line = SleepDelayPolicy.Describe(18000, onBattery: false);
        Assert.Equal("On mains, Windows sleeps this computer after 5 h of no use once nothing holds "
                     + "it awake. Another application holding its own request pushes that out.", line);
    }

    [Fact]
    public void Describe_OnBattery_NamesTheOtherSource() =>
        Assert.StartsWith("On battery, Windows sleeps this computer after 10 m of no use",
                          SleepDelayPolicy.Describe(600, onBattery: true), StringComparison.Ordinal);

    [Fact]
    public void Describe_Zero_SaysWindowsNeverSleepsIt() =>
        // Zero is a real setting, not a missing one, and reads as "never" rather than "at once".
        Assert.Equal("On mains, Windows is set never to sleep this computer when it is idle.",
                     SleepDelayPolicy.Describe(0, onBattery: false));

    [Fact]
    public void Describe_StatesARuleRatherThanAMoment()
    {
        // The clock starts when the machine goes idle, which the app cannot see, so no wall-clock
        // moment may be promised.
        string line = SleepDelayPolicy.Describe(18000, onBattery: false)!;
        Assert.DoesNotContain("left", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("until", line, StringComparison.OrdinalIgnoreCase);
    }

    // Period

    [Theory]
    [InlineData(30u,    "30 s")]
    [InlineData(60u,    "1 m")]
    [InlineData(600u,   "10 m")]
    [InlineData(3600u,  "1 h")]
    [InlineData(5400u,  "1 h 30 m")]
    [InlineData(18000u, "5 h")]
    public void Period_ReadsAsTheAppSaysSpansElsewhere(uint seconds, string expected) =>
        Assert.Equal(expected, SleepDelayPolicy.Period(seconds));

    [Fact]
    public void Period_IgnoresASecondsRemainderAboveAMinute() =>
        Assert.Equal("10 m", SleepDelayPolicy.Period(629));
}
