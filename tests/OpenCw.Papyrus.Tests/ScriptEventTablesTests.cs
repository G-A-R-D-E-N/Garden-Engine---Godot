using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptEventTablesTests : HostFixture
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("00002A:Test.esm", "0000A1:Test.esm");

    private sealed class Clips : IAnimationClips
    {
        public AnimationClip? Find(string refKey, string animation) => new("Idle", 2f, Array.Empty<AnimationKey>());
    }

    private IScriptHost NewHost() => Track(TestHosts.Create(HostKind.Radicle, TestHosts.Gate(("system.event_registrations", "APPROXIMATED")),
        TestHosts.Stub("ScriptObject", null),
        new ScriptSpec("Plain", "Scriptname Plain extends ScriptObject\n")));

    [Fact]
    public void ClearingTheScriptEventTablesDropsStaleTimersRegistrationsReceiversAndAnimationPlayback()
    {
        var instance = NewHost().Attach("Plain", null, Owner);
        var timers = new ScriptTimers();
        var gameTimers = new ScriptTimers(ScriptTimers.GameTimeHandlerName);
        var registrations = new EventRegistrations();
        var custom = new CustomEventRegistry();
        var animations = new ReferenceAnimations(new Clips());
        var attackTiming = new AttackTiming();
        Assert.True(timers.Start(instance, 5, 1));
        Assert.True(gameTimers.Start(instance, 3, 2));
        registrations.Add(instance, "HitEvent", "none");
        custom.Register(instance, "sender", "Boom");
        Assert.True(animations.Play("0000A1:Test.esm", "Idle"));
        Assert.True(animations.Register(instance, PapyrusValue.None, "sender", "Boom"));

        ScriptEventTables.Clear(timers, gameTimers, animations, attackTiming, registrations, custom);

        Assert.Equal(0, timers.Count);
        Assert.Equal(0, gameTimers.Count);
        Assert.Equal(0, registrations.Count);
        Assert.Empty(custom.Receivers);
        Assert.Equal(0, animations.Playing);
        Assert.Equal(0, animations.Registered);
    }
}
