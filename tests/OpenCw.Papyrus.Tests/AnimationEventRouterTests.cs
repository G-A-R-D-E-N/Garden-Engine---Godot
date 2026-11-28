using OpenCommonwealth.Services.Papyrus.Events;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class AnimationEventRouterTests
{
    private const string Walker = "000A00:Fallout4.esm";

    private sealed class Recorder : IAnimationEventConsumer
    {
        public List<ClassifiedAnimationEvent> Seen { get; } = new();

        public void Handle(ClassifiedAnimationEvent raised) => Seen.Add(raised);
    }

    private sealed class Faulty : IAnimationEventConsumer
    {
        public void Handle(ClassifiedAnimationEvent raised) => throw new InvalidOperationException("boom");
    }

    private sealed class Clips : IAnimationClips
    {
        public AnimationClip? Find(string refKey, string animation) => new(animation, 1f, Array.Empty<AnimationKey>());
    }

    [Theory]
    [InlineData("FootLeft", AnimationEventKind.Other, "")]
    [InlineData("weaponSwing", AnimationEventKind.AttackSwing, "")]
    [InlineData("weaponLeftSwing", AnimationEventKind.AttackSwing, "")]
    [InlineData("preHitFrame", AnimationEventKind.AttackPreHit, "")]
    [InlineData("HitFrame", AnimationEventKind.AttackHit, "")]
    [InlineData("attackWinStart", AnimationEventKind.AttackWindowStart, "")]
    [InlineData("attackWinEnd", AnimationEventKind.AttackWindowEnd, "")]
    [InlineData("SoundPlay.WPNHandPowerArmorAttackA", AnimationEventKind.SoundPlay, "WPNHandPowerArmorAttackA")]
    [InlineData("soundplay.Some_Id ", AnimationEventKind.SoundPlay, "Some_Id")]
    [InlineData("SoundPlay.", AnimationEventKind.Other, "")]
    [InlineData("Latch", AnimationEventKind.Other, "")]
    [InlineData("attackStop", AnimationEventKind.Other, "")]
    public void NamesAreClassifiedCaseInsensitivelyAndSoundPlayCarriesItsEditorId(string name, AnimationEventKind kind, string argument)
    {
        Assert.Equal(kind, AnimationEventNames.Classify(name, out var found));
        Assert.Equal(argument, found);
    }

    [Fact]
    public void OneEventReachesEveryConsumerInOrderWithItsClassification()
    {
        var first = new Recorder();
        var second = new Recorder();
        var router = new AnimationEventRouter();
        router.Add(first);
        router.Add(second);
        router.Add(first);

        Assert.True(router.Dispatch(Walker, "FootLeft"));

        Assert.Equal(2, router.Consumers);
        var expected = new ClassifiedAnimationEvent(Walker, "FootLeft", AnimationEventKind.Other, "");
        Assert.Equal(new[] { expected }, first.Seen.ToArray());
        Assert.Equal(new[] { expected }, second.Seen.ToArray());
        Assert.Equal(1, router.Dispatched);
    }

    [Fact]
    public void UnknownNamesStillReachEveryConsumerBecausePapyrusListensForAnyName()
    {
        var recorder = new Recorder();
        var router = new AnimationEventRouter();
        router.Add(recorder);

        router.Dispatch(Walker, "Latch");

        Assert.Equal(AnimationEventKind.Other, Assert.Single(recorder.Seen).Kind);
    }

    [Theory]
    [InlineData("", "FootLeft")]
    [InlineData(" ", "FootLeft")]
    [InlineData(Walker, "")]
    [InlineData(Walker, "  ")]
    public void AnEventWithoutASourceOrANameIsNotDispatched(string refKey, string name)
    {
        var recorder = new Recorder();
        var router = new AnimationEventRouter();
        router.Add(recorder);

        Assert.False(router.Dispatch(refKey, name));

        Assert.Empty(recorder.Seen);
        Assert.Equal(0, router.Dispatched);
    }

    [Fact]
    public void AThrowingConsumerIsCountedAndLoggedAndTheOthersStillRun()
    {
        var log = new List<string>();
        var recorder = new Recorder();
        var router = new AnimationEventRouter(log.Add);
        router.Add(new Faulty());
        router.Add(recorder);

        Assert.True(router.Dispatch(Walker, "HitFrame"));

        Assert.Equal(1, router.Faulted);
        Assert.Single(recorder.Seen);
        Assert.Contains("boom", Assert.Single(log));
    }

    [Fact]
    public void ARemovedConsumerHearsNothingMore()
    {
        var recorder = new Recorder();
        var router = new AnimationEventRouter();
        router.Add(recorder);
        router.Dispatch(Walker, "FootLeft");
        router.Remove(recorder);
        router.Dispatch(Walker, "FootRight");

        Assert.Single(recorder.Seen);
    }

    [Fact]
    public void ThePapyrusConsumerPostsOnlyWhatAScriptOrAWaitListensFor()
    {
        var table = new ReferenceAnimations(new Clips());
        long token = ReferenceAnimations.NextToken();
        Assert.True(table.Play(Walker, "Open", "Latch", token));
        var router = new AnimationEventRouter();
        router.Add(new PapyrusAnimationConsumer(table));

        router.Dispatch(Walker, "FootLeft");
        Assert.Empty(table.Advance(0.0).Events);
        router.Dispatch("000B00:Fallout4.esm", "Latch");
        Assert.Empty(table.Advance(0.0).Events);
        router.Dispatch(Walker, "latch");

        var step = table.Advance(0.0);
        Assert.Equal(new[] { new ReferenceAnimationEvent(Walker, "", "latch") }, step.Events.ToArray());
        Assert.Contains(step.Wakes, wake => wake.Token == token && wake.Completed);
    }

    [Fact]
    public void ThePapyrusConsumerWarnsOnceWhenThePostQueueOverflows()
    {
        var table = new ReferenceAnimations(new Clips());
        Assert.True(table.Play(Walker, "Open", "Latch", ReferenceAnimations.NextToken()));
        var warnings = new List<string>();
        var router = new AnimationEventRouter();
        router.Add(new PapyrusAnimationConsumer(table, warnings.Add));

        for (int i = 0; i < ReferenceAnimations.PostCapacity + 3; i++) router.Dispatch(Walker, "Latch");

        Assert.Equal(3, table.Dropped);
        Assert.Contains("post queue is full", Assert.Single(warnings));
    }

    [Fact]
    public void AttackTimingTracksTheOpenWindowPerActorAndRaisesEachPhase()
    {
        var timing = new AttackTiming();
        var phases = new List<(string Ref, AnimationEventKind Kind)>();
        timing.Phase += (refKey, kind) => phases.Add((refKey, kind));
        var router = new AnimationEventRouter();
        router.Add(timing);

        router.Dispatch(Walker, "attackWinStart");
        Assert.True(timing.IsWindowOpen(Walker));
        Assert.False(timing.IsWindowOpen("000B00:Fallout4.esm"));
        router.Dispatch(Walker, "preHitFrame");
        router.Dispatch(Walker, "HitFrame");
        router.Dispatch(Walker, "FootLeft");
        router.Dispatch(Walker, "attackWinEnd");

        Assert.False(timing.IsWindowOpen(Walker));
        Assert.Equal(
            new[] { AnimationEventKind.AttackWindowStart, AnimationEventKind.AttackPreHit, AnimationEventKind.AttackHit, AnimationEventKind.AttackWindowEnd },
            phases.Select(phase => phase.Kind).ToArray());
    }

    [Fact]
    public void ClearingTheAttackTimingClosesEveryWindow()
    {
        var timing = new AttackTiming();
        timing.Handle(new ClassifiedAnimationEvent(Walker, "attackWinStart", AnimationEventKind.AttackWindowStart, ""));

        timing.Clear();

        Assert.False(timing.IsWindowOpen(Walker));
    }
}
