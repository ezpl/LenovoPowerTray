using ChargeKeeper.Services;
using Xunit;

namespace ChargeKeeper.Tests;

/// <summary>
/// The reading that separates a wait the machine was held awake through from one it slept through.
/// Both produce the same elapsed time, so the detection itself is the guard: a threshold that
/// drifted upwards, or an awake counter that keeps running across a suspend, would report every
/// slept-through wait as held and say nothing about it.
/// </summary>
public class SleepGapTests
{
    private static string NativeSource() => File.ReadAllText(RepoFiles.Find("Helpers/NativeMethods.cs"));

    // ---- the detection ------------------------------------------------------------------------

    [Fact]
    public void TheSmallestSleepCounted_IsFiveSeconds()
    {
        // The standby entry this was written for happened 32 seconds into a two hour wait, so a
        // threshold in minutes would miss the case it exists to catch.
        Assert.Equal(5, SleepGap.SmallestSleepSeconds);
        Assert.Equal(TimeSpan.FromSeconds(5), SleepGap.SmallestSleep);
    }

    [Fact]
    public void ADifferenceUnderTheThreshold_IsNotCalledASleep()
    {
        var gap = new SleepGap(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(4));

        Assert.False(gap.MachineSlept);
        Assert.Null(gap.Fragment());
        Assert.Null(gap.Sentence());
    }

    [Fact]
    public void ADifferenceAtTheThreshold_IsASleep()
    {
        var gap = new SleepGap(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30) - SleepGap.SmallestSleep);

        Assert.True(gap.MachineSlept);
        Assert.Equal(SleepGap.SmallestSleep, gap.Slept);
    }

    [Fact]
    public void AnAwakeCounterReadingAheadOfTheWallClock_IsGranularityRatherThanNegativeSleep()
    {
        var gap = new SleepGap(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(31));

        Assert.Equal(TimeSpan.Zero, gap.Slept);
        Assert.False(gap.MachineSlept);
    }

    // ---- what a reader ends up seeing ---------------------------------------------------------

    [Fact]
    public void TheReadingNamesBothSidesOfTheWait()
    {
        // The field case: a two hour wait, awake for half a minute of it.
        var gap = new SleepGap(TimeSpan.FromHours(2), TimeSpan.FromSeconds(32));

        Assert.Equal("the wait spent 1 hour 59 minutes asleep and less than a minute awake", gap.Fragment());
        Assert.Equal("Measured against the clock, the wait spent 1 hour 59 minutes asleep and " +
                     "less than a minute awake.", gap.Sentence());
    }

    [Fact]
    public void ALineWithNoSleepToReport_IsLeftExactlyAsItWas()
    {
        const string cause = "lid reopened";
        const string line  = "The lid-close wait ended because the 30 minute delay ran out.";
        var held = new SleepGap(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

        Assert.Equal(cause, SleepGap.AddTo(cause, held));
        Assert.Equal(cause, SleepGap.AddTo(cause, null));
        Assert.Equal(line,  SleepGap.AddSentenceTo(line, held));
        Assert.Equal(line,  SleepGap.AddSentenceTo(line, null));
    }

    [Fact]
    public void ASleptThroughWait_SaysSoOnTheLineThatWasDueAnyway()
    {
        var slept = new SleepGap(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2));

        Assert.Equal("lid reopened; the wait spent 28 minutes asleep and 2 minutes awake",
                     SleepGap.AddTo("lid reopened", slept));
        Assert.Equal("Still waiting with the lid closed: 30 minutes into the 120 minute delay. " +
                     "Measured against the clock, the wait spent 28 minutes asleep and 2 minutes awake.",
                     SleepGap.AddSentenceTo(
                         "Still waiting with the lid closed: 30 minutes into the 120 minute delay.", slept));
    }

    // ---- the counter the whole reading rests on -----------------------------------------------

    [Fact]
    public void TheAwakeReading_ComesFromTheCounterThatStopsDuringASuspend()
    {
        // A biased counter — GetTickCount64, the wall clock, a Stopwatch — keeps running across a
        // suspend, so every gap would measure zero and every slept-through wait would read as held.
        string source = NativeSource();

        Assert.Contains("QueryUnbiasedInterruptTime", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTickCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAwakeReadingIsInTimeSpanTicks_AndAFailedQueryIsNotZero()
    {
        // The API reports 100 ns units, which is a TimeSpan tick, so no scaling belongs in between;
        // and a failed query read as zero would put a fabricated sleep of the machine's whole uptime
        // into the trail.
        string body = SourceMethods.Body(NativeSource(), "UnbiasedAwakeTime");

        Assert.Contains("TimeSpan.FromTicks", body, StringComparison.Ordinal);
        Assert.Contains("null", body, StringComparison.Ordinal);
    }
}
