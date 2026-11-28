using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

sealed class FakeVatsTargets : IVatsTargets
{
    public List<VatsTarget> Targets { get; } = new();
    public int ActionPoints { get; set; } = 50;
    public double Critical { get; set; }
    public bool CriticalReady { get; set; }
    public bool Valid { get; set; } = true;
    public string? InitialTargetId { get; set; }
    public int MaxQueue { get; set; } = 16;
    public int Reads { get; private set; }

    public VatsSnapshot Snapshot()
    {
        Reads++;
        return new VatsSnapshot(Targets.ToArray(), ActionPoints, 100, Critical, CriticalReady, Valid);
    }

    public void Kill(string id)
    {
        int index = Targets.FindIndex(t => t.Id == id);
        Targets[index] = Targets[index] with { Alive = false };
    }
}

public class VatsMenuTests
{
    static VatsPart Part(string id, int cost, double x, double y, int chance = 70) =>
        new(id, id, chance, 100, false, cost, new VatsPoint(x, y));

    static VatsTarget Target(string id, bool alive, params VatsPart[] parts) =>
        new(id, id, 10, 100, alive, new List<VatsResistance>(), parts);

    static FakeVatsTargets Fake()
    {
        var fake = new FakeVatsTargets();
        fake.Targets.Add(Target("A", true,
            Part("head", 20, 960, 300),
            Part("torso", 15, 960, 500),
            Part("legs", 10, 960, 700),
            Part("arm", 10, 1200, 520)));
        fake.Targets.Add(Target("B", true, Part("torso", 15, 900, 500)));
        return fake;
    }

    sealed class Harness
    {
        public Harness(FakeVatsTargets fake, bool arm = true)
        {
            Fake = fake;
            Model = new VatsMenuModel(fake);
            Model.AudioCue += Cues.Add;
            Model.SlowTimeRequested += Slow.Add;
            Model.FocusChanged += Focused.Add;
            Model.Accepted += queue => Accepted.Add(queue);
            Model.Closed += Closed.Add;
            Model.CriticalRequested += () => Criticals++;
            Model.PlaybackAbortRequested += () => Aborts++;
            Model.Begin();
            if (arm)
                Model.Arm();
        }

        public FakeVatsTargets Fake { get; }
        public VatsMenuModel Model { get; }
        public List<string> Cues { get; } = new();
        public List<bool> Slow { get; } = new();
        public List<string> Focused { get; } = new();
        public List<IReadOnlyList<VatsQueued>> Accepted { get; } = new();
        public List<VatsOutcome> Closed { get; } = new();
        public int Criticals { get; private set; }
        public int Aborts { get; private set; }
    }

    [Fact]
    public void Begin_focuses_the_first_living_target_and_asks_for_slow_time_once()
    {
        var h = new Harness(Fake());
        h.Model.Begin();

        Assert.Equal("A", h.Model.Focus?.Id);
        Assert.Equal("head", h.Model.SelectedPart?.Id);
        Assert.Equal(new[] { true }, h.Slow);
        Assert.Equal(new[] { "A" }, h.Focused);
    }

    [Fact]
    public void Initial_target_from_the_source_wins_when_alive_and_dead_ones_are_skipped()
    {
        var fake = Fake();
        fake.InitialTargetId = "B";
        Assert.Equal("B", new Harness(fake).Model.Focus?.Id);

        fake.InitialTargetId = null;
        fake.Kill("A");
        Assert.Equal("B", new Harness(fake).Model.Focus?.Id);
    }

    [Fact]
    public void No_living_target_cancels_without_ever_slowing_time()
    {
        var fake = Fake();
        fake.Kill("A");
        fake.Kill("B");

        var h = new Harness(fake);

        Assert.Equal(VatsPhase.Closed, h.Model.Phase);
        Assert.Equal(new[] { VatsOutcome.Cancelled }, h.Closed);
        Assert.Empty(h.Slow);
    }

    [Fact]
    public void Commands_do_nothing_until_armed()
    {
        var h = new Harness(Fake(), arm: false);

        Assert.True(h.Model.Handle(VatsCommand.Queue));
        Assert.False(h.Model.SelectPart("torso"));
        Assert.Empty(h.Model.Queue);

        h.Model.Arm();
        h.Model.Handle(VatsCommand.Queue);

        Assert.Single(h.Model.Queue);
    }

    [Fact]
    public void Queueing_spends_action_points_and_refuses_what_does_not_fit()
    {
        var h = new Harness(Fake());

        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Queue);
        Assert.Equal(40, h.Model.QueuedCost);
        Assert.Equal(10, h.Model.ActionPointPreview);
        Assert.Equal(2, h.Model.QueuedOn("A", "head"));

        h.Model.Handle(VatsCommand.Queue);

        Assert.Equal(2, h.Model.Queue.Count);
        Assert.Equal("denied", h.Cues[^1]);
    }

    [Fact]
    public void Queue_length_is_capped_by_the_source()
    {
        var fake = Fake();
        fake.MaxQueue = 2;
        var h = new Harness(fake);
        h.Model.SelectPart("legs");

        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Queue);

        Assert.Equal(2, h.Model.Queue.Count);
    }

    [Fact]
    public void Return_removes_the_last_queued_shot_then_cancels_and_restores_time()
    {
        var h = new Harness(Fake());
        h.Model.Handle(VatsCommand.Queue);
        h.Model.SelectPart("torso");
        h.Model.Handle(VatsCommand.Queue);

        h.Model.Handle(VatsCommand.Return);
        Assert.Equal(new[] { "head" }, h.Model.Queue.Select(q => q.PartId));
        Assert.Equal(VatsPhase.Targeting, h.Model.Phase);

        h.Model.Handle(VatsCommand.Return);
        Assert.Empty(h.Model.Queue);
        Assert.Equal(VatsPhase.Targeting, h.Model.Phase);

        h.Model.Handle(VatsCommand.Return);

        Assert.Equal(new[] { VatsOutcome.Cancelled }, h.Closed);
        Assert.Equal(new[] { true, false }, h.Slow);
    }

    [Fact]
    public void Accept_with_nothing_queued_does_nothing_and_otherwise_hands_the_queue_over_in_order()
    {
        var h = new Harness(Fake());

        h.Model.Handle(VatsCommand.Accept);
        Assert.Equal(VatsPhase.Targeting, h.Model.Phase);
        Assert.Empty(h.Accepted);

        h.Model.Handle(VatsCommand.Queue);
        h.Model.SelectPart("torso");
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Accept);

        Assert.Equal(VatsPhase.Playback, h.Model.Phase);
        Assert.Equal(new[] { "head", "torso" }, h.Accepted.Single().Select(q => q.PartId));
        Assert.Equal(new[] { 20, 15 }, h.Accepted.Single().Select(q => q.Cost));
    }

    [Fact]
    public void Targeting_commands_are_ignored_during_playback()
    {
        var h = new Harness(Fake());
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Accept);

        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.CycleNext);
        h.Model.Handle(VatsCommand.PartDown);

        Assert.Single(h.Model.Queue);
        Assert.Equal("A", h.Model.Focus?.Id);
        Assert.Equal("head", h.Model.SelectedPart?.Id);
        Assert.Equal(50, h.Model.ActionPointPreview);
    }

    [Fact]
    public void Playback_return_requests_one_abort_and_critical_fires_once_only_when_ready()
    {
        var fake = Fake();
        var h = new Harness(fake);
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Accept);

        h.Model.Handle(VatsCommand.Critical);
        Assert.Equal(0, h.Criticals);

        fake.CriticalReady = true;
        h.Model.Handle(VatsCommand.Critical);
        h.Model.Handle(VatsCommand.Critical);
        Assert.Equal(1, h.Criticals);

        h.Model.Handle(VatsCommand.Return);
        h.Model.Handle(VatsCommand.Return);
        Assert.Equal(1, h.Aborts);
        Assert.Equal(VatsPhase.Playback, h.Model.Phase);
    }

    [Fact]
    public void Finish_closes_as_executed_once_and_restores_time()
    {
        var h = new Harness(Fake());
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Accept);

        h.Model.Finish();
        h.Model.Finish();
        h.Model.Abort();

        Assert.Equal(new[] { VatsOutcome.Executed }, h.Closed);
        Assert.Equal(new[] { true, false }, h.Slow);
    }

    [Fact]
    public void Cancel_after_accept_closes_as_cancelled_once_and_ignores_later_finish()
    {
        var h = new Harness(Fake());
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Accept);

        h.Model.Cancel();
        h.Model.Finish();
        h.Model.Cancel();

        Assert.Equal(new[] { VatsOutcome.Cancelled }, h.Closed);
        Assert.Equal(VatsOutcome.Cancelled, h.Model.Outcome);
        Assert.Equal(new[] { true, false }, h.Slow);
    }

    [Fact]
    public void Abort_closes_as_aborted_once()
    {
        var h = new Harness(Fake());

        h.Model.Abort();
        h.Model.Abort();

        Assert.Equal(new[] { VatsOutcome.Aborted }, h.Closed);
        Assert.False(h.Model.Handle(VatsCommand.Queue));
    }

    [Fact]
    public void Cycling_visits_living_targets_wraps_and_resets_the_selected_part()
    {
        var fake = Fake();
        fake.Targets.Insert(1, Target("C", false, Part("torso", 15, 0, 0)));
        var h = new Harness(fake);
        h.Model.SelectPart("legs");

        h.Model.Handle(VatsCommand.CycleNext);
        Assert.Equal("B", h.Model.Focus?.Id);
        Assert.Equal("torso", h.Model.SelectedPart?.Id);

        h.Model.Handle(VatsCommand.CycleNext);
        Assert.Equal("A", h.Model.Focus?.Id);
        Assert.Equal("head", h.Model.SelectedPart?.Id);

        h.Model.Handle(VatsCommand.CyclePrevious);
        Assert.Equal("B", h.Model.Focus?.Id);
        Assert.Equal(new[] { "A", "B", "A", "B" }, h.Focused);
    }

    [Fact]
    public void Cycling_with_a_single_living_target_does_nothing()
    {
        var fake = Fake();
        fake.Kill("B");
        var h = new Harness(fake);

        h.Model.Handle(VatsCommand.CycleNext);

        Assert.Equal("A", h.Model.Focus?.Id);
        Assert.Equal(new[] { "A" }, h.Focused);
    }

    [Fact]
    public void Killing_the_focused_target_moves_focus_and_drops_its_queued_shots()
    {
        var fake = Fake();
        var h = new Harness(fake);
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.CycleNext);
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.CyclePrevious);
        Assert.Equal(2, h.Model.Queue.Count);

        fake.Kill("A");
        h.Model.Refresh();

        Assert.Equal("B", h.Model.Focus?.Id);
        Assert.Equal("torso", h.Model.SelectedPart?.Id);
        Assert.Equal(new[] { "B" }, h.Model.Queue.Select(q => q.TargetId));
        Assert.Contains("unqueue", h.Cues);
    }

    [Fact]
    public void Killing_every_target_cancels()
    {
        var fake = Fake();
        var h = new Harness(fake);

        fake.Kill("A");
        fake.Kill("B");
        h.Model.Refresh();

        Assert.Equal(new[] { VatsOutcome.Cancelled }, h.Closed);
        Assert.Equal(new[] { true, false }, h.Slow);
    }

    [Fact]
    public void Losing_action_points_trims_the_queue_from_the_end()
    {
        var fake = Fake();
        var h = new Harness(fake);
        h.Model.Handle(VatsCommand.Queue);
        h.Model.Handle(VatsCommand.Queue);
        Assert.Equal(40, h.Model.QueuedCost);

        fake.ActionPoints = 25;
        h.Model.Refresh();

        Assert.Single(h.Model.Queue);
        Assert.Equal(20, h.Model.QueuedCost);
        Assert.Equal(5, h.Model.ActionPointPreview);
    }

    [Fact]
    public void Invalid_snapshot_interrupts_and_every_frame_rereads_the_source()
    {
        var fake = Fake();
        var h = new Harness(fake);
        int reads = fake.Reads;

        h.Model.Refresh();
        Assert.Equal(reads + 1, fake.Reads);

        fake.Valid = false;
        h.Model.Refresh();
        h.Model.Refresh();

        Assert.Equal(new[] { VatsOutcome.Interrupted }, h.Closed);
        Assert.Equal(new[] { true, false }, h.Slow);
    }

    [Fact]
    public void Part_selection_follows_the_direction_inside_the_cone()
    {
        var h = new Harness(Fake());

        h.Model.Handle(VatsCommand.PartDown);
        Assert.Equal("torso", h.Model.SelectedPart?.Id);

        h.Model.Handle(VatsCommand.PartDown);
        Assert.Equal("legs", h.Model.SelectedPart?.Id);

        h.Model.Handle(VatsCommand.PartUp);
        h.Model.Handle(VatsCommand.PartUp);
        Assert.Equal("head", h.Model.SelectedPart?.Id);

        h.Model.Handle(VatsCommand.PartUp);
        Assert.Equal("head", h.Model.SelectedPart?.Id);
    }

    [Fact]
    public void Part_outside_the_cone_is_not_reachable_vertically()
    {
        var h = new Harness(Fake());
        h.Model.SelectPart("arm");

        h.Model.Handle(VatsCommand.PartUp);
        h.Model.Handle(VatsCommand.PartDown);

        Assert.Equal("arm", h.Model.SelectedPart?.Id);
    }

    [Fact]
    public void Queue_part_selects_then_queues_and_rejects_unknown_parts()
    {
        var h = new Harness(Fake());

        Assert.True(h.Model.QueuePart("legs"));
        Assert.False(h.Model.QueuePart("tail"));

        Assert.Equal("legs", h.Model.SelectedPart?.Id);
        Assert.Equal(1, h.Model.QueuedOn("A", "legs"));
        Assert.Equal(0, h.Model.QueuedOn("A", "head"));
    }

    [Fact]
    public void Hint_labels_gray_accept_until_something_is_queued()
    {
        var h = new Harness(Fake());

        Assert.False(h.Model.HintLabels.Single(x => x.Label == "ACCEPT").Enabled);
        Assert.Contains(h.Model.HintLabels, x => x.Label == "CYCLE TARGETS");

        h.Model.Handle(VatsCommand.Queue);

        Assert.True(h.Model.HintLabels.Single(x => x.Label == "ACCEPT").Enabled);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(64, 64)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Displayed_chance_is_clamped_to_zero_through_one_hundred(int raw, int shown)
    {
        Assert.Equal(shown, VatsMenuModel.DisplayChance(raw));
    }
}
