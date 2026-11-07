using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptRegistrationRegistryTests
{
    [Fact]
    public void OneShotAndRepeatingTimersShareTheSplitBoundary()
    {
        var registry = new ScriptRegistrationRegistry();
        var once = registry.Register(OwnerA, ScriptRegistration.ForTimer(0.8, false));
        var repeat = registry.Register(OwnerB, ScriptRegistration.ForTimer(0.8, true));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.7, ClockSource.RealTime));
        Assert.Equal(2, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.1, ClockSource.RealTime));
        Assert.Equal(1, once.Fires);
        Assert.Equal(1, repeat.Fires);
        Assert.Equal(repeat, Assert.Single(registry.Entries));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0, ClockSource.RealTime));
        Assert.Equal(2, registry.Fired);
    }

    [Theory]
    [InlineData(1e-300)]
    [InlineData(double.Epsilon)]
    public void OneShotHugePeriodCountFiresOnce(double interval)
    {
        var registry = new ScriptRegistrationRegistry();
        var once = registry.Register(OwnerA, ScriptRegistration.ForTimer(interval, false));
        Assert.Equal(1, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1, ClockSource.RealTime));
        Assert.Equal(1, once.Fires);
        Assert.Equal(1, registry.Fired);
        Assert.Empty(registry.Entries);
    }

    [Theory]
    [InlineData(0.3, 3)]
    [InlineData(0.6, 6)]
    public void ReviewDecimalBoundaryFiresExactlyOnce(double elapsed, int expected)
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(0.1, true));
        Assert.Equal(expected, registry.Advance(ScriptRegistrationRegistry.SessionOwner, elapsed,
            ClockSource.RealTime));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0, ClockSource.RealTime));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.001, ClockSource.RealTime));
        Assert.Equal(expected, timer.Fires);
        Assert.Equal(expected, registry.Fired);
        Assert.Equal(1, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.099, ClockSource.RealTime));
        Assert.Equal(expected + 1, timer.Fires);
    }

    [Fact]
    public void DecimalBoundaryKeepsPartialIntervals()
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(0.1, true));
        Assert.Equal(2, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.299,
            ClockSource.RealTime));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0, ClockSource.RealTime));
        Assert.Equal(1, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.001, ClockSource.RealTime));
        Assert.Equal(3, timer.Fires);
    }

    [Fact]
    public void ReviewTimerRemainderCannotCreateAnExtraFire()
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(0.1, true));
        Assert.Equal(10, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1, ClockSource.RealTime));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.001, ClockSource.RealTime));
        Assert.Equal(10, timer.Fires);
    }

    [Fact]
    public void TimerCatchupRetainsTheRemainderAndOneShotBehavior()
    {
        var registry = new ScriptRegistrationRegistry();
        var repeat = registry.Register(OwnerA, ScriptRegistration.ForTimer(0.25, true));
        var once = registry.Register(OwnerB, ScriptRegistration.ForTimer(1, false));
        Assert.Equal(4_000_001, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1_000_000.125,
            ClockSource.RealTime));
        Assert.Equal(4_000_000, repeat.Fires);
        Assert.Equal(0.125, repeat.ElapsedSeconds);
        Assert.Equal(1, once.Fires);
        Assert.Single(registry.Entries);
        Assert.Equal(1, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.125, ClockSource.RealTime));
        Assert.Equal(4_000_001, repeat.Fires);
    }

    [Fact]
    public void TimerCounterOverflowDoesNotMutateTheTimer()
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(1, true));
        Assert.Equal(int.MaxValue, registry.Advance(ScriptRegistrationRegistry.SessionOwner, int.MaxValue,
            ClockSource.RealTime));
        Assert.Throws<OverflowException>(() =>
            registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1, ClockSource.RealTime));
        Assert.Equal(int.MaxValue, timer.Fires);
        Assert.Equal(int.MaxValue, registry.Fired);
        Assert.Equal(0, timer.ElapsedSeconds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReviewTimerFactoryRejectsNonFiniteIntervals(double interval)
    {
        Assert.Throws<ArgumentException>(() => ScriptRegistration.ForTimer(interval, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReviewTimerRegistryValidatesDirectPayloads(double interval)
    {
        var registry = new ScriptRegistrationRegistry();
        var payload = new ScriptRegistration(RegistrationKind.Timer, "", interval, true, ClockSource.RealTime);
        Assert.Throws<ArgumentException>(() => registry.Register(OwnerA, payload));
        Assert.Equal(0, registry.Count);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReviewTimerRejectsNonFiniteAdvance(double delta)
    {
        var registry = new ScriptRegistrationRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            registry.Advance(ScriptRegistrationRegistry.SessionOwner, delta, ClockSource.RealTime));
    }

    [Fact]
    public async System.Threading.Tasks.Task ReviewTimerCatchupCannotLoopWithoutProgress()
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(double.Epsilon, true));
        await System.Threading.Tasks.Task.Run(() => Assert.Throws<OverflowException>(() =>
            registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1, ClockSource.RealTime)))
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, timer.Fires);
        Assert.Equal(0, timer.ElapsedSeconds);
        Assert.Equal(0, registry.Fired);
    }

    private static readonly PapyrusOwnerId OwnerA = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A00");
    private static readonly PapyrusOwnerId OwnerB = PapyrusOwnerId.ForPlacedReference("000800:Quests.esp", "000A01");

    [Fact]
    public void EveryKindRetainsItsFullPayload()
    {
        var registry = new ScriptRegistrationRegistry();
        var custom = registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("OnCustomThing"));
        var timer = registry.Register(OwnerB, ScriptRegistration.ForTimer(1.5, repeats: true, ClockSource.GameTime));

        Assert.Equal(RegistrationKind.CustomEvent, custom.Registration.Kind);
        Assert.Equal("OnCustomThing", custom.Registration.EventName);
        Assert.Equal(RegistrationKind.Timer, timer.Registration.Kind);
        Assert.Equal(1.5, timer.Registration.IntervalSeconds);
        Assert.True(timer.Registration.Repeats);
        Assert.Equal(ClockSource.GameTime, timer.Registration.Clock);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void MatchingDispatchReturnsOnlyMatchingRegistrations()
    {
        var registry = new ScriptRegistrationRegistry();
        registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("OnCustomThing"));
        registry.Register(OwnerB, ScriptRegistration.ForCustomEvent("OnOtherThing"));
        registry.Register(OwnerB, ScriptRegistration.ForTimer(1, false));

        var matched = registry.MatchCustomEvent("OnCustomThing");

        Assert.Single(matched);
        Assert.Equal(OwnerA, matched[0].Owner);
        Assert.Empty(registry.MatchCustomEvent("NothingRegistersForThis"));
    }

    [Fact]
    public void ARealTimeTimerFiresAndRepeats()
    {
        var registry = new ScriptRegistrationRegistry();
        var repeating = registry.Register(OwnerA, ScriptRegistration.ForTimer(1.0, repeats: true));
        var once = registry.Register(OwnerB, ScriptRegistration.ForTimer(1.0, repeats: false));

        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.5, ClockSource.RealTime));
        Assert.Equal(2, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 0.5, ClockSource.RealTime));
        Assert.Equal(2, registry.Fired);
        Assert.Equal(1, repeating.Fires);
        Assert.Equal(1, once.Fires);
        Assert.Equal(1, registry.Count);

        Assert.Equal(1, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 1.0, ClockSource.RealTime));
        Assert.Equal(2, repeating.Fires);
    }

    [Fact]
    public void AGameTimeTimerStaysUnboundAndIsCounted()
    {
        var registry = new ScriptRegistrationRegistry();
        var timer = registry.Register(OwnerA, ScriptRegistration.ForTimer(1.0, repeats: true, ClockSource.GameTime));

        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 5.0, ClockSource.GameTime));
        Assert.Equal(0, timer.Fires);
        Assert.Equal(1, registry.UnboundGameTime);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void OnlyTheSessionAdvancesTheRuntime()
    {
        var registry = new ScriptRegistrationRegistry();
        registry.Register(OwnerA, ScriptRegistration.ForTimer(1.0, repeats: true));

        Assert.Throws<InvalidOperationException>(() => registry.Advance("SomeOtherSystem", 1.0, ClockSource.RealTime));
        Assert.Equal(1, registry.RefusedForeignAdvance);
        Assert.Equal(0, registry.Fired);
    }

    [Fact]
    public void UnregisterRemovesOneOrAllOfAnOwner()
    {
        var registry = new ScriptRegistrationRegistry();
        var first = registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("One"));
        registry.Register(OwnerA, ScriptRegistration.ForCustomEvent("Two"));
        registry.Register(OwnerB, ScriptRegistration.ForCustomEvent("Three"));

        Assert.True(registry.Unregister(first.Handle));
        Assert.False(registry.Unregister(first.Handle));
        Assert.Equal(2, registry.Count);
        Assert.Equal(1, registry.UnregisterAll(OwnerA));
        Assert.Equal(1, registry.Count);
        Assert.Equal(OwnerB, registry.Entries[0].Owner);
    }

    [Fact]
    public void APayloadWithoutItsRequiredFieldIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ScriptRegistration.ForCustomEvent("  "));
        Assert.Throws<ArgumentException>(() => ScriptRegistration.ForTimer(0, repeats: true));
        var registry = new ScriptRegistrationRegistry();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            registry.Advance(ScriptRegistrationRegistry.SessionOwner, -1, ClockSource.RealTime));
    }
}
