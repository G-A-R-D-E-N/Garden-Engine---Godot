using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Vats;
using Xunit;

namespace OpenCw.Ui.Tests;

public class VatsGap9Tests
{
    static VatsPart Part(string id, int cost, double x, double y) =>
        new(id, id, 70, 100, false, cost, new VatsPoint(x, y));

    static VatsTarget Target(string id, params VatsPart[] parts) =>
        new(id, id, 10, 100, true, new List<VatsResistance>(), parts);

    static FakeVatsTargets Fake()
    {
        var fake = new FakeVatsTargets();
        fake.Targets.Add(Target("A", Part("head", 20, 960, 300), Part("torso", 15, 960, 500)));
        fake.Targets.Add(Target("B", Part("torso", 15, 900, 500)));
        return fake;
    }

    static VatsMenuModel Open(FakeVatsTargets fake, bool arm = true)
    {
        var model = new VatsMenuModel(fake);
        model.Begin();
        if (arm)
            model.Arm();
        return model;
    }

    static VatsMenuModel Playback(FakeVatsTargets fake)
    {
        VatsMenuModel model = Open(fake);
        model.Handle(VatsCommand.Queue);
        model.Handle(VatsCommand.Accept);
        return model;
    }

    [Fact]
    public void Arming_is_off_until_Arm_and_stays_on_when_armed_twice()
    {
        var model = Open(Fake(), arm: false);
        Assert.False(model.IsArmed);

        model.Arm();
        model.Arm();

        Assert.True(model.IsArmed);
    }

    [Fact]
    public void Critical_is_only_enabled_in_playback_with_the_meter_ready()
    {
        var fake = Fake();
        fake.CriticalReady = true;
        var model = Open(fake);
        Assert.False(model.CriticalEnabled);

        model.Handle(VatsCommand.Queue);
        Assert.False(model.CriticalEnabled);

        model.Handle(VatsCommand.Accept);
        Assert.True(model.CriticalEnabled);

        fake.CriticalReady = false;
        model.Refresh();
        Assert.False(model.CriticalEnabled);
    }

    [Fact]
    public void Critical_is_disabled_after_it_was_requested_and_the_cue_and_event_fire_once()
    {
        var fake = Fake();
        fake.CriticalReady = true;
        var model = Playback(fake);
        var cues = new List<string>();
        int requests = 0;
        model.AudioCue += cues.Add;
        model.CriticalRequested += () => requests++;

        model.Handle(VatsCommand.Critical);
        model.Handle(VatsCommand.Critical);

        Assert.False(model.CriticalEnabled);
        Assert.Equal(1, requests);
        Assert.Equal(new[] { "critical_fire" }, cues);
    }

    [Fact]
    public void Critical_command_during_targeting_requests_nothing_even_when_ready()
    {
        var fake = Fake();
        fake.CriticalReady = true;
        var model = Open(fake);
        var cues = new List<string>();
        int requests = 0;
        model.AudioCue += cues.Add;
        model.CriticalRequested += () => requests++;

        Assert.True(model.Handle(VatsCommand.Critical));

        Assert.Equal(0, requests);
        Assert.Empty(cues);
        Assert.False(model.CriticalEnabled);
    }

    [Fact]
    public void Critical_command_in_playback_without_a_ready_meter_requests_nothing()
    {
        var fake = Fake();
        var model = Playback(fake);
        var cues = new List<string>();
        int requests = 0;
        model.AudioCue += cues.Add;
        model.CriticalRequested += () => requests++;

        model.Handle(VatsCommand.Critical);

        Assert.Equal(0, requests);
        Assert.Empty(cues);
    }

    [Fact]
    public void Abort_is_enabled_until_requested_and_the_cue_and_event_fire_once()
    {
        var model = Playback(Fake());
        var cues = new List<string>();
        int aborts = 0;
        model.AudioCue += cues.Add;
        model.PlaybackAbortRequested += () => aborts++;
        Assert.True(model.AbortEnabled);

        model.Handle(VatsCommand.Return);
        model.Handle(VatsCommand.Return);

        Assert.False(model.AbortEnabled);
        Assert.Equal(1, aborts);
        Assert.Equal(new[] { "abort" }, cues);
        Assert.Equal(VatsPhase.Playback, model.Phase);
    }

    [Fact]
    public void Abort_is_reported_enabled_while_targeting()
    {
        Assert.True(Open(Fake()).AbortEnabled);
    }

    [Fact]
    public void Playback_hints_are_critical_then_abort_and_follow_the_enabled_flags()
    {
        var fake = Fake();
        var model = Playback(fake);
        Assert.Equal(new[] { ("CRITICAL", false), ("ABORT", true) }, model.HintLabels);

        fake.CriticalReady = true;
        model.Refresh();
        Assert.Equal(new[] { ("CRITICAL", true), ("ABORT", true) }, model.HintLabels);

        model.Handle(VatsCommand.Critical);
        Assert.Equal(new[] { ("CRITICAL", false), ("ABORT", true) }, model.HintLabels);

        model.Handle(VatsCommand.Return);
        Assert.Equal(new[] { ("CRITICAL", false), ("ABORT", false) }, model.HintLabels);
    }

    [Fact]
    public void Targeting_hints_list_body_part_and_cycle_only_when_they_apply()
    {
        var fake = Fake();
        var model = Open(fake);
        Assert.Equal(
            new[] { ("SELECT", true), ("ACCEPT", false), ("RETURN", true), ("BODY PART", true), ("CYCLE TARGETS", true) },
            model.HintLabels);

        model.Handle(VatsCommand.CycleNext);
        Assert.Equal(
            new[] { ("SELECT", true), ("ACCEPT", false), ("RETURN", true), ("CYCLE TARGETS", true) },
            model.HintLabels);

        fake.Kill("A");
        model.Refresh();
        Assert.Equal(new[] { ("SELECT", true), ("ACCEPT", false), ("RETURN", true) }, model.HintLabels);
    }

    [Fact]
    public void Critical_notice_starts_empty_is_replaced_by_each_call_and_raises_Changed_once_per_call()
    {
        var model = Open(Fake());
        int changed = 0;
        model.Changed += () => changed++;
        Assert.Null(model.CriticalNotice);

        model.ShowCriticalNotice("CRITICAL HIT");
        Assert.Equal("CRITICAL HIT", model.CriticalNotice);
        Assert.Equal(1, changed);

        model.ShowCriticalNotice("GUARANTEED");
        Assert.Equal("GUARANTEED", model.CriticalNotice);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Playback_commands_keep_the_queue_and_accept_is_ignored()
    {
        var model = Playback(Fake());
        var accepted = new List<IReadOnlyList<VatsQueued>>();
        model.Accepted += accepted.Add;

        model.Handle(VatsCommand.Accept);

        Assert.Empty(accepted);
        Assert.Equal(new[] { new VatsQueued("A", "head", 20) }, model.Queue);
    }
}
