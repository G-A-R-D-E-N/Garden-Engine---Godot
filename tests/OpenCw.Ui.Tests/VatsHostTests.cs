using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsHostTests
{
    static readonly VatsChanceSettings Esm = new(
        NonActorBodyTerm: 50f, WeaponPerceptionOffset: 0.05f, BodyPartMultiplier: 0.075f, PerceptionMultiplier: 0.004f,
        WeaponDistanceMultiplier: 1.25f, DistanceTermMin: 0.05f, DistanceOffset: 0.05f, DistanceMultiplier: 3f, WeaponBaseMultiplier: 0.03f);

    sealed class FakeBodyParts : IBodyPartSource
    {
        public int Calls { get; private set; }

        public IReadOnlyList<VatsBodyPartData>? Parts(string actorBaseKey)
        {
            Calls++;
            return actorBaseKey == "Human"
                ? [new("Torso", "COM", "Torso", 0, 1f, 14, 75, 60), new("Head", "Head", "Head1", 1, 2f, 27, 20, 30)]
                : null;
        }
    }

    sealed class FakeScene : IVatsScene
    {
        public VatsShooter? Shooter0 { get; set; } = new(6f, 0, 80, 100, 0.5, true, 8, 1000f, null, 0.5f);

        public List<VatsCandidate> Cands { get; } = new();

        public Dictionary<(string, byte), VatsPartState> States { get; } = new();

        public bool Valid { get; set; } = true;

        public string? InitialTargetId => Cands.FirstOrDefault()?.Id;

        public VatsShooter? Shooter() => Shooter0;

        public IReadOnlyList<VatsCandidate> Candidates() => Cands;

        public VatsPartState? PartState(VatsCandidate candidate, VatsBodyPart part) =>
            States.TryGetValue((candidate.Id, part.PartType), out var state) ? state : null;

        public VatsPoint? View { get; set; }

        public VatsPoint? ViewSize => View;

        public bool StillValid() => Valid;
    }

    static VatsCandidate Raider(string id = "r1", bool alive = true, string baseKey = "Human", float distance = 500f) =>
        new(id, "Raider", 8, 80, alive, baseKey, distance, []);

    static VatsPartState State(float visibility = 0.8f) => new(visibility, 20, 90, false, new VatsPoint(1, 2));

    static FakeScene SceneWith(params VatsCandidate[] candidates)
    {
        var scene = new FakeScene();
        scene.Cands.AddRange(candidates);
        foreach (var c in candidates)
        {
            scene.States[(c.Id, 0)] = State();
            scene.States[(c.Id, 1)] = State();
        }
        return scene;
    }

    [Fact]
    public void ListsTargetsWithComputedPartChancesAndShooterVitals()
    {
        var host = new VatsHost(SceneWith(Raider()), new FakeBodyParts(), Esm);

        var snapshot = host.Snapshot();

        Assert.Equal(80, snapshot.ActionPoints);
        Assert.Equal(100, snapshot.MaxActionPoints);
        Assert.True(snapshot.CriticalReady);
        Assert.True(snapshot.StillValid);
        var target = Assert.Single(snapshot.Targets);
        Assert.Equal("r1", target.Id);
        Assert.Equal(new[] { 74, 37 }, target.Parts.Select(p => p.HitChance));
        Assert.Equal("r1", host.InitialTargetId);
    }

    [Fact]
    public void NoShooterGivesAnInvalidEmptySnapshot()
    {
        var scene = SceneWith(Raider());
        scene.Shooter0 = null;

        var snapshot = new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot();

        Assert.Empty(snapshot.Targets);
        Assert.False(snapshot.StillValid);
    }

    [Fact]
    public void UnknownWeaponAccuracyListsNoParts()
    {
        var scene = SceneWith(Raider());
        scene.Shooter0 = scene.Shooter0! with { WeaponAccuracy = null };

        Assert.Empty(new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().Targets);
    }

    [Fact]
    public void ConservativeDefaultsStillListPartsWithLowerChances()
    {
        var scene = SceneWith(Raider());
        scene.Shooter0 = new(VatsAssumptions.ConservativePerception, 0, 80, 100, 0, false, 8, 1000f, null, VatsAssumptions.ConservativeWeaponAccuracy);

        var target = Assert.Single(new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().Targets);

        Assert.Equal(new[] { 45, 22 }, target.Parts.Select(p => p.HitChance));
        Assert.All(target.Parts, p => Assert.Equal(20, p.ApCost));
    }

    [Fact]
    public void ApCostRoundsTheWeaponCostUpAndRefusesAnUnusableOne()
    {
        Assert.Equal(20, VatsAssumptions.ApCost(20.0));
        Assert.Equal(21, VatsAssumptions.ApCost(20.2));
        Assert.Null(VatsAssumptions.ApCost(0));
        Assert.Null(VatsAssumptions.ApCost(-5));
        Assert.Null(VatsAssumptions.ApCost(double.NaN));
        Assert.Null(VatsAssumptions.ApCost(double.PositiveInfinity));
    }

    [Fact]
    public void SnapshotCarriesTheSceneViewSizeAndKeepsTheDefaultWithoutOne()
    {
        var scene = SceneWith(Raider());
        Assert.Equal(1920, new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().ViewWidth);

        scene.View = new VatsPoint(1280, 720);
        var snapshot = new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot();

        Assert.Equal(1280, snapshot.ViewWidth);
        Assert.Equal(720, snapshot.ViewHeight);
    }

    [Fact]
    public void MeleeWeaponListsNoParts()
    {
        var scene = SceneWith(Raider());
        scene.Shooter0 = scene.Shooter0! with { WeaponType = 3 };

        Assert.Empty(new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().Targets);
    }

    [Fact]
    public void DeadAndPartlessTargetsAreNotListed()
    {
        var scene = SceneWith(Raider("dead", alive: false), Raider("robot", baseKey: "Unknown"), Raider("live"));

        var snapshot = new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot();

        Assert.Equal("live", Assert.Single(snapshot.Targets).Id);
    }

    [Fact]
    public void PartsOutOfSightAreOmittedAndDistanceLowersTheChance()
    {
        var scene = SceneWith(Raider("near"), Raider("far", distance: 2000f));
        scene.States.Remove(("near", 1));

        var targets = new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().Targets;

        Assert.Equal(new[] { 74 }, targets.Single(t => t.Id == "near").Parts.Select(p => p.HitChance));
        Assert.Equal(new[] { 8, 4 }, targets.Single(t => t.Id == "far").Parts.Select(p => p.HitChance));
    }

    [Fact]
    public void CrippledArmsOfTheShooterLowerEveryChance()
    {
        var scene = SceneWith(Raider());
        scene.Shooter0 = scene.Shooter0! with { CrippledArms = 1 };

        var target = Assert.Single(new VatsHost(scene, new FakeBodyParts(), Esm).Snapshot().Targets);

        Assert.Equal(44, target.Parts[0].HitChance);
    }

    [Fact]
    public void BodyPartsAreReadOncePerBaseKey()
    {
        var source = new FakeBodyParts();
        var host = new VatsHost(SceneWith(Raider("a"), Raider("b")), source, Esm);

        host.Snapshot();
        host.Snapshot();

        Assert.Equal(1, source.Calls);
    }

    sealed class FakeSink : IVatsShotSink
    {
        public List<string> Log { get; } = new();

        public HashSet<string> Dead { get; } = new();

        public bool KillOnHit { get; set; }

        public int Spent { get; private set; }

        public int Rounds { get; set; } = 100;

        public int Consumed { get; private set; }

        public bool CanTarget(string targetId) => !Dead.Contains(targetId);

        public bool TryConsumeRound()
        {
            if (Rounds <= 0)
                return false;
            Rounds--;
            Consumed++;
            return true;
        }

        public void SpendActionPoints(int cost)
        {
            Spent += cost;
            Log.Add($"ap{cost}");
        }

        public void ApplyDamage(string targetId, string partId)
        {
            Log.Add($"dmg:{targetId}:{partId}");
            if (KillOnHit)
                Dead.Add(targetId);
        }
    }

    static Func<double> Rolls(params double[] values)
    {
        var next = 0;
        return () => values[next++];
    }

    static VatsSnapshot ExecSnapshot() => new VatsHost(SceneWith(Raider()), new FakeBodyParts(), Esm).Snapshot();

    // Invariant: catches a roll compared the wrong way round or off by one at the chance edge (torso 74, head 37), or AP spent only on hits.
    [Fact]
    public void ExecutionRollsEachShotAgainstItsPartChanceAndSpendsApOnMissesToo()
    {
        var sink = new FakeSink();
        var queue = new VatsQueued[] { new("r1", "0", 20), new("r1", "1", 20), new("r1", "0", 20) };

        var shots = VatsExecution.Run(queue, ExecSnapshot(), sink, Rolls(0.7399, 0.3701, 0.9));

        Assert.Equal(new[] { VatsShotResult.Hit, VatsShotResult.Miss, VatsShotResult.Miss }, shots.Select(s => s.Result));
        Assert.Equal(new[] { 74, 37, 74 }, shots.Select(s => s.Chance));
        Assert.Equal(60, sink.Spent);
        Assert.Equal(new[] { "ap20", "dmg:r1:0", "ap20", "ap20" }, sink.Log);
    }

    // Invariant: catches a 0 percent part landing a hit on a zero roll, or a 100 percent part missing on the highest roll.
    [Fact]
    public void ExecutionNeverHitsAtZeroPercentAndAlwaysHitsAtOneHundred()
    {
        var snapshot = new VatsSnapshot(
            [new VatsTarget("t", "T", 1, 50, true, [], [new VatsPart("a", "A", 0, 50, false, 5), new VatsPart("b", "B", 100, 50, false, 5)])],
            50, 50, 0, false, true);
        var sink = new FakeSink();

        var shots = VatsExecution.Run([new("t", "a", 5), new("t", "b", 5)], snapshot, sink, Rolls(0.0, 0.999999));

        Assert.Equal(new[] { VatsShotResult.Miss, VatsShotResult.Hit }, shots.Select(s => s.Result));
    }

    // Invariant: catches the queue spending AP, rolling or damaging a corpse once an earlier shot killed the target, or a part that left the snapshot.
    [Fact]
    public void ExecutionSkipsShotsAtADeadTargetOrAMissingPartWithoutAPOrARoll()
    {
        var sink = new FakeSink { KillOnHit = true };
        var rolls = 0;
        double Roll() { rolls++; return 0; }
        var queue = new VatsQueued[] { new("r1", "0", 20), new("r1", "1", 20), new("r1", "9", 20) };

        var shots = VatsExecution.Run(queue, ExecSnapshot(), sink, Roll);

        Assert.Equal(new[] { VatsShotResult.Hit, VatsShotResult.Skipped, VatsShotResult.Skipped }, shots.Select(s => s.Result));
        Assert.Equal(20, sink.Spent);
        Assert.Equal(1, rolls);
    }

    // Invariant: catches a VATS shot that costs no ammo, or one that consumes a round only on a hit.
    [Fact]
    public void ExecutionConsumesOneRoundPerShotHitOrMiss()
    {
        var sink = new FakeSink { Rounds = 5 };
        var queue = new VatsQueued[] { new("r1", "0", 20), new("r1", "1", 20), new("r1", "0", 20) };

        VatsExecution.Run(queue, ExecSnapshot(), sink, Rolls(0.0, 0.99, 0.0));

        Assert.Equal(3, sink.Consumed);
        Assert.Equal(2, sink.Rounds);
    }

    // Invariant: catches a shot taken from an empty clip: it must spend no AP, draw no roll and deal no damage, and later shots stay skipped.
    [Fact]
    public void ExecutionSkipsShotsOnceTheClipIsEmptyWithoutAPRollOrDamage()
    {
        var sink = new FakeSink { Rounds = 1 };
        var rolls = 0;
        double Roll() { rolls++; return 0; }
        var queue = new VatsQueued[] { new("r1", "0", 20), new("r1", "0", 20), new("r1", "1", 20) };

        var shots = VatsExecution.Run(queue, ExecSnapshot(), sink, Roll);

        Assert.Equal(new[] { VatsShotResult.Hit, VatsShotResult.Skipped, VatsShotResult.Skipped }, shots.Select(s => s.Result));
        Assert.Equal(20, sink.Spent);
        Assert.Equal(1, rolls);
        Assert.Equal(new[] { "ap20", "dmg:r1:0" }, sink.Log);
    }

    sealed class ThrowingSink : IVatsShotSink
    {
        public bool CanTarget(string targetId) => true;

        public bool TryConsumeRound() => true;

        public void SpendActionPoints(int cost) { }

        public void ApplyDamage(string targetId, string partId) => throw new InvalidOperationException("damage failed");
    }

    static VatsMenuModel QueuedModel(int shots)
    {
        var model = new VatsMenuModel(new VatsHost(SceneWith(Raider()), new FakeBodyParts(), Esm));
        model.Begin();
        model.Arm();
        for (var i = 0; i < shots; i++)
            model.Handle(VatsCommand.Queue);
        return model;
    }

    static void RunOut(VatsPlaybackRun run, VatsMenuModel model, double step = 1.0)
    {
        for (var i = 0; i < 50 && model.Phase != VatsPhase.Closed; i++)
            run.Tick(step);
    }

    // Invariant: catches Accept leaving the menu unresolved, an unfinished playback never closing, or a seeded source giving different outcomes on a rerun.
    [Fact]
    public void PlaybackResolvesTheQueueFinishesExecutedAndSeededRollsRepeat()
    {
        (List<string> Log, int Spent, int Reported) Play(int seed)
        {
            var model = QueuedModel(3);
            var rng = new Random(seed);
            var sink = new FakeSink();
            var run = new VatsPlaybackRun(model, sink, rng.NextDouble, 0.5);
            model.Handle(VatsCommand.Accept);
            Assert.Equal(VatsPhase.Playback, model.Phase);
            RunOut(run, model);
            Assert.Equal(VatsOutcome.Executed, model.Outcome);
            return (sink.Log, sink.Spent, model.Shots.Count);
        }

        var first = Play(1234);
        var again = Play(1234);

        Assert.Equal(60, first.Spent);
        Assert.Equal(3, first.Reported);
        Assert.Equal(3, first.Log.Count(l => l == "ap20"));
        Assert.Equal(first.Log, again.Log);
    }

    // Invariant: catches every shot resolving in the Accept frame again, or the pacing interval being ignored.
    [Fact]
    public void PlaybackResolvesOneShotPerIntervalAndClosesAfterTheLastInterval()
    {
        var model = QueuedModel(3);
        var run = new VatsPlaybackRun(model, new FakeSink(), () => 0.0, 1.0);
        model.Handle(VatsCommand.Accept);

        run.Tick(0.0);
        Assert.Single(model.Shots);
        run.Tick(0.5);
        Assert.Single(model.Shots);
        run.Tick(0.5);
        Assert.Equal(2, model.Shots.Count);
        run.Tick(1.0);
        Assert.Equal(3, model.Shots.Count);
        Assert.Equal(VatsPhase.Playback, model.Phase);
        run.Tick(1.0);
        Assert.Equal(VatsOutcome.Executed, model.Outcome);
    }

    // Invariant: catches abort not stopping later shots (AP and ammo spent after the request) or closing as Executed.
    [Fact]
    public void AbortDuringPlaybackStopsLaterShotsAndClosesAborted()
    {
        var model = QueuedModel(3);
        var sink = new FakeSink();
        var run = new VatsPlaybackRun(model, sink, () => 0.0, 1.0);
        model.Handle(VatsCommand.Accept);
        run.Tick(0.0);

        model.Handle(VatsCommand.Return);
        RunOut(run, model);

        Assert.Equal(VatsOutcome.Aborted, model.Outcome);
        Assert.Equal(1, sink.Consumed);
        Assert.Equal(20, sink.Spent);
    }

    // Invariant: catches a throwing shot leaving the modal menu stuck in Playback.
    [Fact]
    public void PlaybackStillFinishesWhenAShotThrows()
    {
        var model = QueuedModel(1);
        var run = new VatsPlaybackRun(model, new ThrowingSink(), () => 0, 1.0);
        model.Handle(VatsCommand.Accept);

        Assert.Throws<InvalidOperationException>(() => run.Tick(0.1));

        Assert.Equal(VatsPhase.Closed, model.Phase);
        Assert.Equal(VatsOutcome.Executed, model.Outcome);
    }

    // Invariant: catches a skipped shot costing the pacing interval or not being reported.
    [Fact]
    public void SkippedShotsAddNoWaitAndAreReported()
    {
        var model = QueuedModel(2);
        var sink = new FakeSink { Rounds = 0 };
        var run = new VatsPlaybackRun(model, sink, () => 0.0, 5.0);
        model.Handle(VatsCommand.Accept);

        run.Tick(0.0);
        run.Tick(0.0);
        run.Tick(0.0);

        Assert.Equal(2, model.Shots.Count);
        Assert.All(model.Shots, s => Assert.Equal(VatsShotResult.Skipped, s.Result));
        Assert.Equal(VatsOutcome.Executed, model.Outcome);
    }
}
