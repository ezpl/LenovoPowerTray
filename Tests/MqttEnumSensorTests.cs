using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ChargeKeeper.Services;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;
using Xunit;

namespace ChargeKeeper.Tests;

/// <summary>
/// The two readings drawn from a declared list of words, and the words themselves.
/// </summary>
/// <remarks>
/// A receiver holds the state of one of these against the list announced with it and rejects
/// anything not on it, so the words are part of the published contract in the way a display name is
/// not: changing one breaks every automation and template comparing against it. The literals here
/// are spelled in full rather than composed from the source they guard, so a changed word fails
/// instead of being followed.
/// </remarks>
public class MqttEnumSensorTests
{
    private const string DeviceId = "chargekeeper_office_x1";

    [Fact]
    public void TheLidCloseWaitWords_AreTheOnesAReceiverAlreadyMatchesOn() =>
        Assert.Equal(
            [
                "Off",
                "Idle",
                "Waiting for the timer",
                "Waiting for the battery target",
                "Waiting for the timer or the battery target",
                "Waiting with nothing left to reach",
            ],
            LidWaitStates.Words);

    [Fact]
    public void TheLastChangeWords_AreTheOnesAReceiverAlreadyMatchesOn() =>
        Assert.Equal(
            [
                "Lid closed",
                "Lid opened",
                "Wait ended with nothing to wait for",
                "Wait ended on the delay",
                "Wait ended on the battery target",
                "Wait ended on the temperature ceiling",
                "Wait ended on a charger",
                "Keep awake started",
                "Keep awake ended",
            ],
            AppChangeLog.Words);

    [Fact]
    public void EveryWayAWaitCanEnd_HasAPublishedChangeOfItsOwn()
    {
        // The two vocabularies are declared apart, so a member added to one and forgotten in the
        // other would silently publish some other change's word.
        var mapped = Enum.GetValues<LidWaitEnd>().Select(AppChangeLog.From).ToList();

        Assert.Equal(Enum.GetValues<LidWaitEnd>().Length, mapped.Distinct().Count());
        Assert.All(mapped, change => Assert.StartsWith("Wait ended", AppChangeLog.Label(change),
                                                        StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDeclaredList_CarriesTheLiteralAnAbsentReadingPublishes() =>
        // Without it a gap in the reading is a state the receiver was never offered, which it logs
        // as an error rather than showing as nothing known.
        Assert.Equal(MqttPayload.None, MqttEnumSensor.Announced(["Alpha", "Beta"]).Last());

    [Theory]
    [InlineData(MqttEntityCatalog.LastChange)]
    [InlineData(MqttEntityCatalog.LidWait)]
    public void EachEnumSensor_AnnouncesItsWordsAsTheReceiverReadsThem(string entityId)
    {
        var entry = Component(entityId);

        Assert.Equal("sensor", entry.GetProperty("p").GetString());
        Assert.Equal(MqttEnumSensor.DeviceClass, entry.GetProperty("device_class").GetString());

        var expected = entityId == MqttEntityCatalog.LastChange
            ? MqttEnumSensor.Announced(AppChangeLog.Words)
            : MqttEnumSensor.Announced(LidWaitStates.Words);

        Assert.Equal(
            expected,
            entry.GetProperty("options").EnumerateArray().Select(v => v.GetString()!).ToList());
    }

    [Theory]
    [InlineData(MqttEntityCatalog.LastChange)]
    [InlineData(MqttEntityCatalog.LidWait)]
    public void EachEnumSensor_PublishesOnlyWordsItDeclared(string entityId)
    {
        // Whatever the reading is, and whether or not there is one, the payload is on the list.
        var declared = Component(entityId).GetProperty("options")
                                          .EnumerateArray().Select(v => v.GetString()!).ToList();

        string?[] states =
        [
            MqttTestBed.Build(MqttTestBed.Live(), MqttTestBed.Surface()).Find(entityId)!.ReadState(),
            MqttTestBed.Build(MqttTestBed.Live(), surface: null).Find(entityId)!.ReadState(),
        ];

        Assert.All(states, state => Assert.Contains(state, declared));
    }

    [Fact]
    public void TheLidCloseWait_NamesWhatEachWaitIsActuallyOn()
    {
        Assert.Equal(LidWaitState.Off, LidWaitStates.From(false, false, false, false));
        Assert.Equal(LidWaitState.Idle, LidWaitStates.From(true, false, true, true));
        Assert.Equal(LidWaitState.WaitingForTheTimer, LidWaitStates.From(true, true, true, false));
        Assert.Equal(LidWaitState.WaitingForTheBatteryTarget, LidWaitStates.From(true, true, false, true));
        Assert.Equal(LidWaitState.WaitingForEither, LidWaitStates.From(true, true, true, true));
        Assert.Equal(LidWaitState.WaitingWithNothingLeftToReach,
                     LidWaitStates.From(true, true, false, false));
    }

    [Fact]
    public void ARunningWait_KeepsItsOwnStateWhenTheSettingIsSwitchedOffUnderIt() =>
        // The setting stops the next lid close; it does not end the wait already running.
        Assert.Equal(LidWaitState.WaitingForTheTimer, LidWaitStates.From(false, true, true, false));

    [Fact]
    public void ACountdown_IsAbsentWhenThereIsNothingToCountTo() =>
        Assert.Null(SurfaceReader.MinutesUntil(null, DateTimeOffset.Now));

    [Fact]
    public void ACountdown_RoundsUpAndNeverGoesBelowZero()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(12, SurfaceReader.MinutesUntil(now.AddMinutes(11.2), now));
        Assert.Equal(1, SurfaceReader.MinutesUntil(now.AddSeconds(1), now));
        Assert.Equal(0, SurfaceReader.MinutesUntil(now.AddMinutes(-5), now));
    }

    [Theory]
    [InlineData(MqttEntityCatalog.LidWaitRemaining)]
    [InlineData(MqttEntityCatalog.KeepAwakeHoldRemaining)]
    public void EachCountdown_ReportsNothingRatherThanZeroWithNoWaitRunning(string entityId)
    {
        // The timer condition can be on with the lid open, and a keep-awake session can run with no
        // clock expiry at all: both are "no countdown", not "no time left".
        var entity = MqttTestBed.Build(MqttTestBed.Live(), MqttTestBed.Surface()).Find(entityId)!;

        Assert.Equal(MqttPayload.None, entity.ReadState());
    }

    [Fact]
    public void EachCountdown_ReportsTheMinutesTheSurfaceCarries()
    {
        var set = MqttTestBed.Build(
            MqttTestBed.Live(),
            MqttTestBed.Surface(lidWait: LidWaitState.WaitingForTheTimer,
                                lidWaitRemaining: 7, keepAwakeRemaining: 42));

        Assert.Equal("7", set.Find(MqttEntityCatalog.LidWaitRemaining)!.ReadState());
        Assert.Equal("42", set.Find(MqttEntityCatalog.KeepAwakeHoldRemaining)!.ReadState());
        Assert.Equal("Waiting for the timer", set.Find(MqttEntityCatalog.LidWait)!.ReadState());
    }

    [Fact]
    public void TheLastChangePair_ReportsTheChangeAndTheInstantItHappenedAt()
    {
        var at = new DateTimeOffset(2026, 9, 9, 14, 32, 5, TimeSpan.FromHours(2));
        var set = MqttTestBed.Build(
            MqttTestBed.Live(),
            MqttTestBed.Surface(lastChange: AppChange.WaitEndedOnTheDelay, lastChangeAt: at));

        Assert.Equal("Wait ended on the delay", set.Find(MqttEntityCatalog.LastChange)!.ReadState());
        Assert.Equal("2026-09-09T14:32:05.0000000+02:00",
                     set.Find(MqttEntityCatalog.LastChangeTime)!.ReadState());
    }

    [Fact]
    public void TheLastChangePair_ReportsNothingBeforeAnythingHasHappened()
    {
        var set = MqttTestBed.Build(MqttTestBed.Live(), MqttTestBed.Surface());

        Assert.Equal(MqttPayload.None, set.Find(MqttEntityCatalog.LastChange)!.ReadState());
        Assert.Equal(MqttPayload.None, set.Find(MqttEntityCatalog.LastChangeTime)!.ReadState());
    }

    [Fact]
    public void TheTwoLidReadings_GoWithTheRestOfTheLidEntitiesOnAMachineWithNoLid()
    {
        // They describe a wait only a machine with a lid can have, so the same gate carries them —
        // even though they are filed under App diagnostics rather than with the lid settings.
        var published = MqttTestBed.Build(
            MqttTestBed.Live(), MqttTestBed.Surface(),
            PublishCapabilities.Full with { LidClose = false })
            .Published(null).Select(e => e.EntityId).ToList();

        Assert.DoesNotContain(MqttEntityCatalog.LidWait, published);
        Assert.DoesNotContain(MqttEntityCatalog.LidWaitRemaining, published);
        Assert.Contains(MqttEntityCatalog.KeepAwakeHoldRemaining, published);
    }

    /// <summary>One component entry as a receiver reads it, out of a composed document.</summary>
    private static JsonElement Component(string entityId)
    {
        string json = DiscoveryDocument.Build(
            MqttPublisher.TopicRoot,
            new MqttDeviceIdentity(DeviceId, "homeassistant", "ChargeKeeper (Office-X1)"),
            new DiscoveryDevice("ZeroZero Software", "ChargeKeeper", "1.22.0"),
            new DiscoveryOrigin("ChargeKeeper", "1.22.0"),
            MqttTestBed.Declared().All,
            [],
            []);

        return JsonDocument.Parse(json).RootElement.GetProperty("cmps").GetProperty(entityId).Clone();
    }
}
