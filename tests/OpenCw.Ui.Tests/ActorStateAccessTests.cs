using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth;
using OpenCommonwealth.Services.Papyrus.Natives;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ActorStateAccessTests
{
    private static ActorState Spawn(ActorProtection protection = ActorProtection.None)
    {
        var state = new ActorState(10, null) { Protection = protection };
        state.SetHealth(20f);
        return state;
    }

    private static ActorStateAccess Access(Dictionary<string, ActorState> actors) =>
        new(key => actors.GetValueOrDefault(key), () => actors.Values.ToList());

    [Fact]
    public void TheWritesLandOnTheActorsState()
    {
        var actor = Spawn();
        var access = Access(new() { ["000A00"] = actor });

        Assert.True(access.TrySet("000A00", ActorStateFlag.CanSpeakWhenDown, true));
        Assert.True(access.TrySet("000A00", ActorStateFlag.NoBleedoutRecovery, true));

        Assert.True(actor.CanSpeakWhenDown);
        Assert.True(actor.NoBleedoutRecovery);
        access.TrySet("000A00", ActorStateFlag.NoBleedoutRecovery, false);
        Assert.False(actor.NoBleedoutRecovery);
        Assert.True(access.TryGet("000A00", ActorStateQuery.NoBleedoutRecovery, out var read));
        Assert.False(read);
    }

    [Fact]
    public void IgnoreFriendlyHitsLandsOnItsOwnFlagOnly()
    {
        var actor = Spawn();
        var access = Access(new() { ["000A00"] = actor });

        Assert.True(access.TrySet("000A00", ActorStateFlag.IgnoreFriendlyHits, true));

        Assert.True(access.TrySet("000A00", ActorStateFlag.NotShowOnStealthMeter, true));

        Assert.True(actor.IgnoreFriendlyHits);
        Assert.True(actor.NotShowOnStealthMeter);
        Assert.False(actor.NoBleedoutRecovery);
    }

    [Fact]
    public void IsBleedingOutIsTrueOnlyWhileEssentialDownAndFalseOnceTheActorRecoversOrDies()
    {
        var actor = Spawn(ActorProtection.Essential);
        var access = Access(new() { ["000A00"] = actor });

        access.TryGet("000A00", ActorStateQuery.BleedingOut, out var before);
        actor.Damage(1000f, "p");
        access.TryGet("000A00", ActorStateQuery.BleedingOut, out var down);
        actor.Kill("p");
        access.TryGet("000A00", ActorStateQuery.BleedingOut, out var dead);

        Assert.False(before);
        Assert.True(down);
        Assert.False(dead);
    }

    [Fact]
    public void NoBleedoutRecoveryWrittenByTheNativeStopsTheRecoveryTick()
    {
        var actor = Spawn(ActorProtection.Essential);
        var access = Access(new() { ["000A00"] = actor });
        actor.Damage(1000f, "p");

        access.TrySet("000A00", ActorStateFlag.NoBleedoutRecovery, true);
        actor.Tick(1000f, 10f, 0.5f);

        Assert.True(actor.IsEssentialDown);
    }

    [Fact]
    public void IsBeingRiddenFollowsTheRider()
    {
        var mount = Spawn();
        var access = Access(new() { ["000B00"] = mount });

        access.TryGet("000B00", ActorStateQuery.BeingRidden, out var free);
        mount.Rider = "000A00";
        access.TryGet("000B00", ActorStateQuery.BeingRidden, out var ridden);

        Assert.False(free);
        Assert.True(ridden);
    }

    [Fact]
    public void IsOnMountIsTrueForTheRiderOfAMountAndFalseForAnyoneElse()
    {
        var mount = Spawn();
        mount.IsMount = true;
        mount.Rider = "000A00";
        var bystander = Spawn();
        var access = Access(new() { ["000B00"] = mount, ["000C00"] = bystander });

        access.TryGet("000A00", ActorStateQuery.OnMount, out var rider);
        access.TryGet("000C00", ActorStateQuery.OnMount, out var other);
        mount.Rider = null;
        access.TryGet("000A00", ActorStateQuery.OnMount, out var dismounted);

        Assert.True(rider);
        Assert.False(other);
        Assert.False(dismounted);
    }

    [Fact]
    public void AnActorWithNoStateIsNotWrittenAndNotRead()
    {
        var access = Access(new());

        Assert.False(access.TrySet("000A00", ActorStateFlag.CanSpeakWhenDown, true));
        Assert.False(access.TryGet("000A00", ActorStateQuery.BleedingOut, out var value));
        Assert.False(value);
    }
}
