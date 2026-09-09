using ChargeKeeper.Services;
using Xunit;

namespace ChargeKeeper.Tests;

/// <summary>
/// What one lid notification is recorded as. The trail used to hold the conclusion — an entry saying
/// a lid closed, indistinguishable from a correct one whatever produced it — so these pin the fields
/// that can contradict the event, and the cost rule that keeps them free on a machine whose lid
/// behaves.
/// </summary>
public class LidEventTrailTests
{
    private static LidEventObservation Event(
        byte payload = LidEventLog.ClosedPayload,
        LidEventKind kind = LidEventKind.Closed,
        double? sincePreviousSeconds = null,
        double? sinceInputSeconds = 600,
        bool nearOwnSchemeWrite = false,
        bool nearDisplayChange = false,
        int? batteryPercent = 72,
        bool? batteryCharging = false) =>
        new(payload, kind,
            sincePreviousSeconds is { } previous ? TimeSpan.FromSeconds(previous) : null,
            sinceInputSeconds is { } input ? TimeSpan.FromSeconds(input) : null,
            nearOwnSchemeWrite, nearDisplayChange, batteryPercent, batteryCharging);

    // ── The cost rule ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnOrdinaryLidClose_CostsNoExtraLine()
    {
        // The constraint the whole record is built to: every field rides on the entry that was
        // already written, so a machine whose lid behaves logs exactly what it logged before.
        var ordinary = Event();

        Assert.Null(LidEventLog.RepeatLine(ordinary));
        Assert.Null(LidEventLog.SchemeWriteLine(ordinary));
    }

    [Fact]
    public void AnOrdinaryLidOpen_CostsNoExtraLine()
    {
        var ordinary = Event(payload: 1, kind: LidEventKind.Opened, sinceInputSeconds: 0.2);

        Assert.Null(LidEventLog.RepeatLine(ordinary));
        Assert.Null(LidEventLog.SchemeWriteLine(ordinary));
    }

    [Fact]
    public void TheRegistrationReplay_CostsNoExtraLine()
    {
        var replay = Event(kind: LidEventKind.ClosedAtRegistration);

        Assert.Null(LidEventLog.RepeatLine(replay));
        Assert.Null(LidEventLog.SchemeWriteLine(replay));
    }

    [Fact]
    public void ARepeatedValue_CostsExactlyOneExtraLine()
    {
        var repeat = Event(kind: LidEventKind.ClosedRepeat, sincePreviousSeconds: 360);

        Assert.NotNull(LidEventLog.RepeatLine(repeat));
        Assert.Null(LidEventLog.SchemeWriteLine(repeat));
    }

    [Fact]
    public void AnEventBesideTheApplicationsOwnSchemeWrite_CostsExactlyOneExtraLine()
    {
        var beside = Event(nearOwnSchemeWrite: true);

        Assert.Null(LidEventLog.RepeatLine(beside));
        Assert.NotNull(LidEventLog.SchemeWriteLine(beside));
    }

    [Fact]
    public void ADisplayChangeBesideTheEvent_CostsNoExtraLine() =>
        // Named on the line, never given one of its own: a display change is common and a lid event
        // beside one is not by itself wrong.
        Assert.Null(LidEventLog.RepeatLine(Event(nearDisplayChange: true)));

    // ── The field that can contradict the event ──────────────────────────────────────────────────

    [Fact]
    public void SubSecondInput_ContradictsAReportedClose() =>
        // Nobody types under a shut lid. This is the only reading that can disagree with the
        // notification; every other one is consistent with both a real close and a false one.
        Assert.True(Event(sinceInputSeconds: 0.4).InputContradictsIt);

    [Fact]
    public void LongIdle_ContradictsNothing() =>
        Assert.False(Event(sinceInputSeconds: 600).InputContradictsIt);

    [Fact]
    public void SubSecondInput_ContradictsNoReportedOpen() =>
        // Input during an open is exactly what an open predicts, so it is no contradiction at all.
        Assert.False(Event(payload: 1, kind: LidEventKind.Opened, sinceInputSeconds: 0.4)
                         .InputContradictsIt);

    [Fact]
    public void AnUnreadableIdleReading_ContradictsNothing() =>
        Assert.False(Event(sinceInputSeconds: null).InputContradictsIt);

    [Fact]
    public void TheCauseClause_NamesTheIdleReadingAheadOfEveryOtherReading()
    {
        // Ordering guard, not decoration: after the clause naming the cause itself, a reader has to
        // meet the one reading that can disagree with the event before the readings that cannot.
        string cause = LidEventLog.Cause(Event(sincePreviousSeconds: 90, nearOwnSchemeWrite: true,
                                               nearDisplayChange: true));

        int input    = cause.IndexOf("keyboard or mouse input", StringComparison.Ordinal);
        int previous = cause.IndexOf("since the previous notification", StringComparison.Ordinal);
        int scheme   = cause.IndexOf("writing the power scheme", StringComparison.Ordinal);
        int display  = cause.IndexOf("display change", StringComparison.Ordinal);
        int battery  = cause.IndexOf("battery at", StringComparison.Ordinal);

        Assert.True(input > 0);
        Assert.All([previous, scheme, display, battery], position => Assert.True(position > input));
    }

    [Fact]
    public void TheCauseClause_SaysTheLidWasOpenWhereInputContradictsTheClose() =>
        Assert.Contains("the lid was open", LidEventLog.Cause(Event(sinceInputSeconds: 0.4)),
                        StringComparison.Ordinal);

    [Fact]
    public void TheCauseClause_QualifiesALongIdleReadingAsThisSessionOnly() =>
        // A small figure is proof and a large one is weak evidence: the reading sees only the
        // calling session, so the wording must not let a large one read as nobody being present.
        Assert.Contains("in this session", LidEventLog.Cause(Event(sinceInputSeconds: 600)),
                        StringComparison.Ordinal);

    [Fact]
    public void TheCauseClause_SaysSoWhereTheIdleReadingCouldNotBeTaken() =>
        Assert.Contains("could not be read", LidEventLog.Cause(Event(sinceInputSeconds: null)),
                        StringComparison.Ordinal);

    // ── The rest of the line ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheEntry_NamesTheByteThatArrived()
    {
        Assert.Equal("Lid closed (payload 0)", LidEventLog.What(Event()));
        Assert.Equal("Lid opened (payload 1)",
                     LidEventLog.What(Event(payload: 1, kind: LidEventKind.Opened)));
    }

    [Fact]
    public void TheCauseClause_NamesTheBatteryReadingOrItsAbsence()
    {
        Assert.Contains("battery at 72 %, not charging", LidEventLog.Cause(Event()),
                        StringComparison.Ordinal);
        Assert.Contains("no battery reading yet",
                        LidEventLog.Cause(Event(batteryPercent: null, batteryCharging: null)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheCauseClause_NamesTheReplayAsOne() =>
        Assert.Contains("registration replay",
                        LidEventLog.Cause(Event(kind: LidEventKind.ClosedAtRegistration)),
                        StringComparison.Ordinal);

    [Fact]
    public void TheCauseClause_OmitsTheIntervalWhereThereIsNoPreviousNotification() =>
        Assert.DoesNotContain("since the previous notification", LidEventLog.Cause(Event()),
                              StringComparison.Ordinal);

    // ── Which kind of delivery this is ───────────────────────────────────────────────────────────

    [Fact]
    public void KindOf_TellsATransitionFromARepeatAndFromTheReplay()
    {
        // Written as one fact rather than a theory because the kinds are internal and a public test
        // signature cannot carry them.
        Assert.Equal(LidEventKind.ClosedAtRegistration, LidEventLog.KindOf(true,  previous: null));
        Assert.Equal(LidEventKind.OpenedAtRegistration, LidEventLog.KindOf(false, previous: null));
        Assert.Equal(LidEventKind.ClosedRepeat,         LidEventLog.KindOf(true,  previous: true));
        Assert.Equal(LidEventKind.OpenedRepeat,         LidEventLog.KindOf(false, previous: false));
        Assert.Equal(LidEventKind.Closed,               LidEventLog.KindOf(true,  previous: false));
        Assert.Equal(LidEventKind.Opened,               LidEventLog.KindOf(false, previous: true));
    }

    // ── The window the two neighbours are judged against ─────────────────────────────────────────

    [Fact]
    public void WithinTheWindow_IsFalseForAnInstantThatNeverHappened() =>
        Assert.False(LidEventLog.WithinTheWindow(null, DateTimeOffset.Now));

    [Fact]
    public void WithinTheWindow_HoldsInsideTheWindowAndNotOutsideIt()
    {
        var now = DateTimeOffset.Now;

        Assert.True(LidEventLog.WithinTheWindow(now - TimeSpan.FromSeconds(1), now));
        Assert.True(LidEventLog.WithinTheWindow(now - LidEventLog.NearEnoughToBeRelated, now));
        Assert.False(LidEventLog.WithinTheWindow(
            now - LidEventLog.NearEnoughToBeRelated - TimeSpan.FromSeconds(1), now));
    }

    [Fact]
    public void WithinTheWindow_IsFalseForAnInstantInTheFuture() =>
        // The wall clock can go backwards — a time sync, a resume — and a negative age must not
        // read as a neighbour that has not happened yet.
        Assert.False(LidEventLog.WithinTheWindow(DateTimeOffset.Now + TimeSpan.FromSeconds(30),
                                                 DateTimeOffset.Now));

    // ── What reaches the broker ──────────────────────────────────────────────────────────────────

    // The announced list itself is pinned beside the other enum sensors' lists, in
    // MqttEnumSensorTests; what belongs here is that no kind can be added without reaching it.
    [Fact]
    public void EveryKind_HasAWordInTheAnnouncedList() =>
        Assert.All(Enum.GetValues<LidEventKind>(),
                   kind => Assert.Contains(LidEventLog.Label(kind), LidEventLog.Words));

    // ── Ordering, in the source that has to obey it ──────────────────────────────────────────────

    [Fact]
    public void TheIdleReading_IsTakenBeforeTheLockOnClose()
    {
        // Locking the workstation stops the session input tick advancing, so a reading taken after
        // it describes the lock rather than the moment the notification arrived — and the one field
        // that can contradict a close would then always agree with it.
        string source = File.ReadAllText(RepoFiles.Find("Services/LidDelayService.cs"));

        int read = source.IndexOf("NativeMethods.SinceLastInput()", StringComparison.Ordinal);
        int lockOnClose = source.IndexOf("LockIfConfigured();", StringComparison.Ordinal);

        Assert.True(read > 0);
        Assert.True(lockOnClose > read);
    }
}
