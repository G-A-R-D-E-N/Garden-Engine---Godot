using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class RestoredStateLoadTests : HostFixture
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
    public void LoadActorStageClearWithoutSavedEventSectionsDropsStaleState()
    {
        var host = NewHost();
        var instance = host.Attach("Plain", null, Owner);
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

        var transaction = new LoadTransaction()
            .Add(LoadStage.Actors, "runtime overlays", () => ScriptEventTables.Clear(timers, gameTimers, animations, attackTiming, registrations, custom));

        Assert.True(transaction.Run().Succeeded);
        Assert.Equal(0, timers.Count);
        Assert.Equal(0, gameTimers.Count);
        Assert.Equal(0, registrations.Count);
        Assert.Empty(custom.Receivers);
    }

    [Fact]
    public void AFailingActorsStageStepAfterClearRestoresTheCapturedScriptEventState()
    {
        var host = NewHost();
        var instance = host.Attach("Plain", null, Owner);
        var timers = new ScriptTimers();
        var registrations = new EventRegistrations();
        var custom = new CustomEventRegistry();
        Assert.True(timers.Start(instance, 7, 1));
        registrations.Add(instance, "HitEvent", "none");
        var captured = ScriptEventsSave.Capture(timers, registrations, custom);
        var idle = new ScriptTimers();
        var idleRegistrations = new EventRegistrations();
        var idleCustom = new CustomEventRegistry();

        void Undo(Exception error)
        {
            ScriptEventsSave.Restore(captured, host.Instances, idle, idleRegistrations, idleCustom);
        }

        var result = new LoadTransaction()
            .Add(LoadStage.Actors, "clear stale script state", () => ScriptEventTables.Clear(idle, new ScriptTimers(ScriptTimers.GameTimeHandlerName), new ReferenceAnimations(new Clips()), new AttackTiming(), idleRegistrations, idleCustom))
            .Add(LoadStage.Actors, "scene restore", () => throw new InvalidOperationException("boom"), Undo, LoadStage.Quests, LoadStage.Scripts)
            .Run();

        Assert.False(result.Succeeded);
        Assert.Equal(1, idle.Count);
        Assert.Equal(new[] { (instance, 1, 7.0) }, idle.Scheduled);
        Assert.Equal(new[] { (instance, "HitEvent", "none") }, idleRegistrations.All);
    }
}
