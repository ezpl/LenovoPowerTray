namespace ChargeKeeper.Services;

/// <summary>What one lid notification was, as against what the code concluded from it.</summary>
internal enum LidEventKind
{
    /// <summary>Closed, and the previous notification said open.</summary>
    Closed,

    /// <summary>Opened, and the previous notification said closed.</summary>
    Opened,

    /// <summary>Closed, and the previous notification already said closed. Nothing de-duplicates
    /// these: a repeat arms a fresh wait exactly as a transition does.</summary>
    ClosedRepeat,

    /// <summary>Opened, and the previous notification already said open.</summary>
    OpenedRepeat,

    /// <summary>The value Windows delivers once at registration, before any transition.</summary>
    ClosedAtRegistration,

    /// <inheritdoc cref="ClosedAtRegistration"/>
    OpenedAtRegistration,
}

/// <summary>
/// One lid notification exactly as it arrived: the byte, whether it changed anything, and the
/// readings taken beside it that a false event contradicts itself on.
/// </summary>
/// <remarks>
/// The lid is the one thing the platform offers no way to ask about — <c>GetPwrCapabilities</c> says
/// a lid exists and nothing reports its position — so the notification can never be checked against a
/// second opinion. Every field here is circumstantial contradiction rather than verification.
/// </remarks>
internal readonly record struct LidEventObservation(
    byte Payload,
    LidEventKind Kind,
    TimeSpan? SincePrevious,
    TimeSpan? SinceInput,
    bool NearOwnSchemeWrite,
    bool NearDisplayChange,
    int? BatteryPercent,
    bool? BatteryCharging)
{
    /// <summary>Whether the byte says closed. The byte is the record; this is the reading of it.</summary>
    public bool Closed => Payload == LidEventLog.ClosedPayload;

    /// <summary>Whether this delivery carried the same value as the one before it.</summary>
    public bool IsRepeat => Kind is LidEventKind.ClosedRepeat or LidEventKind.OpenedRepeat;

    /// <summary>Whether input reached the session recently enough to contradict a reported close.
    /// Only a close can be contradicted: nobody types under a shut lid, while input during an open
    /// is exactly what an open predicts.</summary>
    public bool InputContradictsIt =>
        Closed && SinceInput is { } idle && idle < LidEventLog.InputThatContradictsAClose;
}

/// <summary>
/// The last lid notification, and the wording every line about one is composed from. State and
/// sentences only, so what a reader ends up seeing is testable without closing a lid.
/// <see cref="LidDelayService"/> owns the subscription and feeds this what arrived.
/// </summary>
/// <remarks>
/// The trail recorded the conclusion rather than the observation: an entry saying a lid closed was
/// indistinguishable from a correct one whatever produced it. Costs no line on an ordinary day —
/// every field rides on the entry that was already written, and the two extra lines are written only
/// where something arrived that should not have.
/// </remarks>
internal static class LidEventLog
{
    /// <summary>The byte Windows delivers for a shut lid. One is open.</summary>
    public const byte ClosedPayload = 0;

    /// <summary>How recent input has to be to contradict a reported close. Long enough to cover the
    /// gap between a keystroke and the notification reaching this process, short enough that a
    /// reading inside it cannot be someone who left the machine and then shut it.</summary>
    public static readonly TimeSpan InputThatContradictsAClose = TimeSpan.FromSeconds(2);

    /// <summary>How close to the application's own power-scheme write, or to a display change, a
    /// notification has to land before it is named as possibly theirs.</summary>
    public static readonly TimeSpan NearEnoughToBeRelated = TimeSpan.FromSeconds(5);

    private static readonly System.Threading.Lock _sync = new();
    private static LidEventObservation? _last;
    private static DateTimeOffset? _lastAt;
    private static DateTimeOffset? _displayChangedAt;

    /// <summary>Raised (off the UI thread) whenever a notification is recorded, so the published
    /// surface follows the event rather than waiting for a battery tick.</summary>
    public static event Action? Recorded;

    /// <summary>The last notification, or null when none has arrived this session.</summary>
    public static LidEventObservation? Last { get { lock (_sync) return _last; } }

    /// <summary>The instant it arrived, or null when none has.</summary>
    public static DateTimeOffset? LastAt { get { lock (_sync) return _lastAt; } }

    /// <summary>Notes a display topology or DPI change, so a lid notification landing beside one can
    /// say so. A dock and a monitor being switched both reach this.</summary>
    public static void NoteDisplayChange()
    {
        lock (_sync) _displayChangedAt = DateTimeOffset.Now;
    }

    /// <summary>Whether a display change landed within <see cref="NearEnoughToBeRelated"/> of
    /// <paramref name="now"/>.</summary>
    public static bool DisplayChangedRecently(DateTimeOffset now)
    {
        lock (_sync) return WithinTheWindow(_displayChangedAt, now);
    }

    /// <summary>Records the notification and raises <see cref="Recorded"/>.</summary>
    public static void Record(LidEventObservation observation, DateTimeOffset at)
    {
        lock (_sync)
        {
            _last   = observation;
            _lastAt = at;
        }

        // Never let a subscriber's failure escape: this is reached from the lid callback, on an OS
        // thread, where an escaped exception terminates the process.
        try { Recorded?.Invoke(); }
        catch (Exception ex) { AppLog.Error("LidEventLog.Recorded", ex); }
    }

    /// <summary>Whether <paramref name="instant"/> falls inside the window ending at
    /// <paramref name="now"/>. A null instant never does.</summary>
    public static bool WithinTheWindow(DateTimeOffset? instant, DateTimeOffset now) =>
        instant is { } at && now >= at && now - at <= NearEnoughToBeRelated;

    /// <summary>Which kind of delivery this is, from the value that arrived and the one before it.
    /// A null previous value means the registration replay, which Windows delivers once before any
    /// transition.</summary>
    public static LidEventKind KindOf(bool closed, bool? previous) => (closed, previous) switch
    {
        (true,  null)  => LidEventKind.ClosedAtRegistration,
        (false, null)  => LidEventKind.OpenedAtRegistration,
        (true,  true)  => LidEventKind.ClosedRepeat,
        (false, false) => LidEventKind.OpenedRepeat,
        (true,  _)     => LidEventKind.Closed,
        _              => LidEventKind.Opened,
    };

    /// <summary>The reading as a receiver holds it: what arrived, and whether it changed anything.
    /// The words are the closed list the entity announces.</summary>
    public static string Label(LidEventKind kind) => kind switch
    {
        LidEventKind.Closed               => "Closed",
        LidEventKind.Opened               => "Opened",
        LidEventKind.ClosedRepeat         => "Closed again, a repeat",
        LidEventKind.OpenedRepeat         => "Opened again, a repeat",
        LidEventKind.ClosedAtRegistration => "Closed at registration",
        _                                 => "Opened at registration",
    };

    /// <summary>Every word the entity can publish, in the order the kinds are declared.</summary>
    public static IReadOnlyList<string> Words { get; } =
        [.. Enum.GetValues<LidEventKind>().Select(Label)];

    /// <summary>
    /// The cause clause for the entry that was already being written. Every reading rides here, so
    /// an ordinary day costs no extra line — the entry is roughly one clause longer than it was.
    /// </summary>
    /// <remarks>The kind of delivery names the cause proper; the idle reading follows it, ahead of
    /// every other reading, because it is the only field that can contradict the event. Everything
    /// after it is consistent with both a real close and a false one.</remarks>
    public static string Cause(LidEventObservation o)
    {
        var clauses = new List<string>(6)
        {
            o.Kind switch
            {
                LidEventKind.ClosedAtRegistration or LidEventKind.OpenedAtRegistration =>
                    "lid-switch registration replay (initial state, not a real transition)",
                LidEventKind.ClosedRepeat or LidEventKind.OpenedRepeat =>
                    "lid switch, repeating the value it last reported",
                _ => "lid switch",
            },
            Input(o),
        };

        if (o.SincePrevious is { } since)
            clauses.Add($"{Interval(since)} since the previous notification");
        if (o.NearOwnSchemeWrite)
            clauses.Add("within seconds of this application writing the power scheme itself");
        if (o.NearDisplayChange)
            clauses.Add("within seconds of a display change");
        clauses.Add(Battery(o));

        return string.Join("; ", clauses);
    }

    /// <summary>The entry itself, which now names the byte rather than only the conclusion drawn
    /// from it.</summary>
    public static string What(LidEventObservation o) =>
        $"Lid {(o.Closed ? "closed" : "opened")} (payload {o.Payload})";

    /// <summary>
    /// The extra line for a value re-delivered with no transition, or null for one that changed
    /// something. Silent forever on a machine whose lid behaves, which is why it is worth a line of
    /// its own where it is not.
    /// </summary>
    public static string? RepeatLine(LidEventObservation o)
    {
        if (!o.IsRepeat) return null;

        string when = o.SincePrevious is { } since ? $" {Interval(since)} ago" : "";
        return $"The lid switch reported {(o.Closed ? "closed" : "open")} again, the same value as " +
               $"the notification before it{when}. Nothing de-duplicates these, so a repeat is " +
               "acted on exactly as a real transition is.";
    }

    /// <summary>
    /// The extra line for a notification landing beside this application's own power-scheme write,
    /// or null for one that did not. Every settings reload reaches that write while the subscription
    /// is live, so this is the only way a self-inflicted re-delivery is told from an external event.
    /// </summary>
    public static string? SchemeWriteLine(LidEventObservation o) =>
        o.NearOwnSchemeWrite
            ? "This notification arrived within seconds of this application re-activating the power " +
              "scheme, which every settings reload provokes. Proximity is not proof, but an event " +
              "with no such neighbour did not come from here."
            : null;

    private static string Input(LidEventObservation o) => o.SinceInput switch
    {
        null => "the time since the last keyboard or mouse input could not be read",
        // A close contradicted: nobody types under a shut lid, so the lid was open as this arrived.
        { } idle when o.InputContradictsIt =>
            $"{Interval(idle)} since the last keyboard or mouse input, so the lid was open",
        // A large figure is weak evidence either way: this reading sees only the calling session, so
        // input on the secure desktop or in another session never reaches it.
        { } idle => $"{Interval(idle)} since the last keyboard or mouse input in this session",
    };

    private static string Battery(LidEventObservation o) => o.BatteryPercent switch
    {
        null => "no battery reading yet",
        { } percent => $"battery at {percent} %, {(o.BatteryCharging is true ? "charging" : "not charging")}",
    };

    /// <summary>A span in the largest unit that keeps it readable, with sub-second precision where
    /// that is the whole point of the reading.</summary>
    private static string Interval(TimeSpan span) => span switch
    {
        { TotalSeconds: < 10 } => $"{span.TotalSeconds:0.#} s",
        { TotalMinutes: < 1 }  => $"{span.TotalSeconds:0} s",
        { TotalHours: < 1 }    => $"{span.TotalMinutes:0} min",
        _                      => $"{span.TotalHours:0.#} h",
    };
}
