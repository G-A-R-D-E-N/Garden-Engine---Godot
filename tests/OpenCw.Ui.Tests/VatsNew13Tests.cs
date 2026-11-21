using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsNew13Tests
{
    static readonly VatsChanceSettings Esm = new(
        NonActorBodyTerm: 50f, WeaponPerceptionOffset: 0.05f, BodyPartMultiplier: 0.075f, PerceptionMultiplier: 0.004f,
        WeaponDistanceMultiplier: 1.25f, DistanceTermMin: 0.05f, DistanceOffset: 0.05f, DistanceMultiplier: 3f, WeaponBaseMultiplier: 0.03f);

    sealed class Targets : IVatsTargets
    {
        public int Rounds { get; set; } = int.MaxValue;

        public int ActionPoints { get; set; } = 100;

        public VatsSnapshot Snapshot()
        {
            var parts = new[]
            {
                new VatsPart("head", "Head", 70, 1, false, 10, new VatsPoint(960, 300)),
                new VatsPart("torso", "Torso", 70, 1, false, 10, new VatsPoint(960, 500)),
            };
            var target = new VatsTarget("A", "A", 1, 1, true, new List<VatsResistance>(), parts);
            return new VatsSnapshot(new[] { target }, ActionPoints, 100, 0, true, true, Rounds: Rounds);
        }
    }

    static VatsMenuModel Begun(Targets targets)
    {
        var model = new VatsMenuModel(targets);
        model.Begin();
        model.Arm();
        return model;
    }

    // Invariant: catches a queue that grows past the loaded clip, or an empty clip accepting a queue.
    [Fact]
    public void TheQueueStopsAtTheLoadedRoundsAndDeniesTheRest()
    {
        var targets = new Targets { Rounds = 2 };
        var model = Begun(targets);
        var cues = new List<string>();
        model.AudioCue += cues.Add;

        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);

        Assert.Equal(2, model.Queue.Count);
        Assert.Equal("denied", cues.Last());
        Assert.Equal(0, model.RoundsPreview);

        targets.Rounds = 0;
        var empty = Begun(targets);
        empty.Handle(VatsCommand.Queue);
        Assert.Empty(empty.Queue);
    }

    // Invariant: catches a clip that shrinks before playback leaving more queued shots than rounds.
    [Fact]
    public void AShrinkingClipTrimsTheQueueFromTheEnd()
    {
        var targets = new Targets { Rounds = 3 };
        var model = Begun(targets);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);

        targets.Rounds = 1;
        model.Refresh();

        Assert.Single(model.Queue);
        Assert.Equal(0, model.RoundsPreview);
    }

    [Fact]
    public void AnUnlimitedSnapshotShowsNoRoundsPreview()
    {
        Assert.Null(Begun(new Targets()).RoundsPreview);
    }

    sealed class FlakyParts : IBodyPartSource
    {
        public bool Available { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyList<VatsBodyPartData>? Parts(string actorBaseKey)
        {
            Calls++;
            return Available ? [new("Torso", "COM", "Torso", 0, 1f, 14, 75, 60)] : null;
        }
    }

    sealed class Clock : TimeProvider
    {
        public long Millis { get; set; }

        public override long GetTimestamp() => Millis;

        public override long TimestampFrequency => 1000;
    }

    sealed class OneActorScene : IVatsScene
    {
        public int Rounds { get; set; } = int.MaxValue;

        public string? InitialTargetId => "r1";

        public VatsShooter? Shooter() => new(6f, 0, 80, 100, 0.5, true, 8, 1000f, null, 0.5f, Rounds);

        public IReadOnlyList<VatsCandidate> Candidates() => [new("r1", "Raider", 8, 0.8, true, "Human", 500f, [])];

        public VatsPartState? PartState(VatsCandidate candidate, VatsBodyPart part) => new(0.8f, 20, 0.9, false, new VatsPoint(1, 2));

        public bool StillValid() => true;
    }

    // Invariant: catches a failed body part read cached as a confirmed empty list for the host lifetime, or retried on every snapshot.
    [Fact]
    public void AFailedBodyPartReadIsRetriedAfterTheWindowAndThenConfirmed()
    {
        var source = new FlakyParts();
        var clock = new Clock();
        var host = new VatsHost(new OneActorScene(), source, Esm, clock);

        Assert.Empty(host.Snapshot().Targets);
        Assert.Empty(host.Snapshot().Targets);
        Assert.Equal(1, source.Calls);

        source.Available = true;
        clock.Millis = 500;
        Assert.Empty(host.Snapshot().Targets);
        Assert.Equal(1, source.Calls);

        clock.Millis = 1100;
        Assert.Single(host.Snapshot().Targets);
        Assert.Equal(2, source.Calls);

        Assert.Single(host.Snapshot().Targets);
        Assert.Equal(2, source.Calls);
    }

    // Invariant: catches an unbounded retry of a base that never answers.
    [Fact]
    public void RetriesOfABodyPartReadThatNeverAnswersAreCapped()
    {
        var source = new FlakyParts();
        var clock = new Clock();
        var host = new VatsHost(new OneActorScene(), source, Esm, clock);

        for (var i = 0; i < VatsHost.MaxAttempts * 3; i++)
        {
            host.Snapshot();
            clock.Millis += 2000;
        }

        Assert.Equal(VatsHost.MaxAttempts, source.Calls);
    }

    [Fact]
    public void TheHostCarriesTheLoadedRoundsIntoTheSnapshot()
    {
        var source = new FlakyParts { Available = true };

        Assert.Equal(3, new VatsHost(new OneActorScene { Rounds = 3 }, source, Esm).Snapshot().Rounds);
        Assert.Equal(int.MaxValue, new VatsHost(new OneActorScene(), source, Esm).Snapshot().Rounds);
    }

    [Theory]
    [InlineData("select", "UIVATSSelectPart")]
    public void TheEvidencedVatsCueHasASound(string cue, string editorId) => Assert.Equal(editorId, VatsSoundMap.EditorId(cue));

    [Theory]
    [InlineData("enter")]
    [InlineData("exit")]
    [InlineData("denied")]
    [InlineData("queue")]
    [InlineData("shot_hit")]
    [InlineData("no.such.cue")]
    public void OtherVatsCuesStaySilent(string cue) => Assert.Null(VatsSoundMap.EditorId(cue));

    // Invariant: catches a hint label with no control text for either device, in targeting or in playback.
    [Fact]
    public void EveryHintShowsItsKeyAndItsButton()
    {
        var model = Begun(new Targets());
        model.Handle(VatsCommand.Queue);
        var hints = new List<(string Label, string Control, bool Enabled)>();
        hints.AddRange(model.HintRows(false));
        hints.AddRange(model.HintRows(true));
        model.Handle(VatsCommand.Accept);
        hints.AddRange(model.HintRows(false));
        hints.AddRange(model.HintRows(true));

        Assert.All(hints, h => Assert.NotEmpty(h.Control));
        Assert.Contains(model.HintRows(false), h => h.Label == "ABORT");
        Assert.Equal("Enter / E", VatsControls.For("ACCEPT", false));
        Assert.Equal("A", VatsControls.For("ACCEPT", true));
        Assert.Equal(string.Empty, VatsControls.For("NO SUCH LABEL", false));
        Assert.All(VatsControls.Labels, l => Assert.NotEmpty(VatsControls.For(l, false)));
        Assert.All(VatsControls.Labels, l => Assert.NotEmpty(VatsControls.For(l, true)));
    }

    // Invariant: catches the preview subtracting the queue again while playback has already spent the rounds.
    [Fact]
    public void TheRoundsPreviewFollowsTheClipDuringPlayback()
    {
        var targets = new Targets { Rounds = 5 };
        var model = Begun(targets);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);
        Assert.Equal(2, model.RoundsPreview);

        model.Handle(VatsCommand.Accept);
        targets.Rounds = 4;
        model.Refresh();

        Assert.Equal(4, model.RoundsPreview);
    }

    sealed class SecondShotThrows : IVatsShotSink
    {
        int _damage;

        public bool CanTarget(string targetId) => true;

        public bool TryConsumeRound() => true;

        public void SpendActionPoints(int cost) { }

        public void ApplyDamage(string targetId, string partId)
        {
            if (++_damage == 2)
                throw new InvalidOperationException("damage failed");
        }
    }

    // Invariant: catches a failed playback reported as Executed, or the shots resolved before the failure being dropped.
    [Fact]
    public void APlaybackThatThrowsClosesAsFailedAndKeepsTheShotsThatResolved()
    {
        var model = Begun(new Targets());
        var closed = new List<VatsOutcome>();
        model.Closed += closed.Add;
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Queue);
        var run = new VatsPlaybackRun(model, new SecondShotThrows(), () => 0.0, 1.0);
        model.Handle(VatsCommand.Accept);

        run.Tick(0.0);
        Assert.Throws<InvalidOperationException>(() => run.Tick(1.0));

        Assert.Equal(new[] { VatsOutcome.Failed }, closed);
        Assert.Equal(VatsPhase.Closed, model.Phase);
        Assert.Single(model.Shots);
        Assert.Equal(VatsShotResult.Hit, model.Shots[0].Result);
    }

    // Invariant: catches an external stack removal that never reaches the finished callback, which leaves the VATS camera on.
    [Fact]
    public void AnExternalRemovalReportsAbortedOnce()
    {
        var model = Begun(new Targets());
        var finished = new List<VatsOutcome>();
        var removeCalls = 0;
        var lifecycle = new VatsMenuLifecycle(model, () => removeCalls++, finished.Add);
        lifecycle.Attach();

        lifecycle.StackRemoved();
        lifecycle.StackRemoved();

        Assert.Equal(new[] { VatsOutcome.Aborted }, finished);
        Assert.Equal(VatsPhase.Closed, model.Phase);
        Assert.Equal(1, removeCalls);
    }

    // Invariant: catches a model-initiated close reporting twice, or reporting before the menu left the stack.
    [Fact]
    public void AModelClosedSessionRemovesTheMenuAndReportsItsOwnOutcomeOnce()
    {
        var model = Begun(new Targets());
        var finished = new List<VatsOutcome>();
        var log = new List<string>();
        VatsMenuLifecycle? lifecycle = null;
        lifecycle = new VatsMenuLifecycle(model, () => { log.Add("remove"); lifecycle!.StackRemoved(); }, o => { log.Add("finished"); finished.Add(o); });
        lifecycle.Attach();

        model.Cancel();

        Assert.Equal(new[] { VatsOutcome.Cancelled }, finished);
        Assert.Equal(new[] { "remove", "finished" }, log);
    }

    // Invariant: catches a message opened over VATS letting VATS restore capture under it, so gameplay resumes with a visible cursor.
    [Fact]
    public void TheModeBeforeTheFirstOwnerIsRestoredOnlyWhenTheLastOwnerReleases()
    {
        var owners = new CursorOwnership<string>();
        var vats = new object();
        var message = new object();

        owners.Acquire(vats, "captured");
        owners.Acquire(message, "visible");

        Assert.False(owners.Release(vats, out _));
        Assert.True(owners.Release(message, out var prior));
        Assert.Equal("captured", prior);
        Assert.Equal(0, owners.Count);
    }

    [Fact]
    public void AnOwnerThatReleasesTwiceOrNeverAcquiredRestoresNothing()
    {
        var owners = new CursorOwnership<string>();
        var owner = new object();
        owners.Acquire(owner, "captured");
        owners.Acquire(owner, "visible");

        Assert.False(owners.Release(new object(), out _));
        Assert.True(owners.Release(owner, out var prior));
        Assert.Equal("captured", prior);
        Assert.False(owners.Release(owner, out _));
    }
}
