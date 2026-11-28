using OpenCommonwealth.Services.Papyrus.LiveState;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReadinessGateTests
{
    [Fact]
    public void Every_subsystem_starts_not_started_and_the_gate_is_not_ready()
    {
        var gate = new ReadinessGate();
        foreach (var subsystem in Enum.GetValues<Subsystem>())
            Assert.Equal(ReadyState.NotStarted, gate.StatusOf(subsystem).State);
        Assert.False(gate.AllReady);
        Assert.Equal(Enum.GetValues<Subsystem>(), gate.NotReady.ToArray());
    }

    [Fact]
    public void The_subsystems_report_separately()
    {
        var gate = new ReadinessGate();
        gate.Ready(Subsystem.Vm);
        gate.Pending(Subsystem.Quests);
        gate.Fail(Subsystem.Nav, new FileNotFoundException("navmesh"));

        Assert.Equal(ReadyState.Ready, gate.StatusOf(Subsystem.Vm).State);
        Assert.Equal(ReadyState.Pending, gate.StatusOf(Subsystem.Quests).State);
        Assert.Equal(ReadyState.Failed, gate.StatusOf(Subsystem.Nav).State);
        Assert.Equal(ReadyState.NotStarted, gate.StatusOf(Subsystem.Actors).State);
        Assert.Equal(ReadyState.NotStarted, gate.StatusOf(Subsystem.Audio).State);
        Assert.DoesNotContain(Subsystem.Vm, gate.NotReady);
        Assert.Equal(4, gate.NotReady.Count);
    }

    [Fact]
    public void The_gate_is_ready_only_when_all_five_are()
    {
        var gate = new ReadinessGate();
        foreach (var subsystem in Enum.GetValues<Subsystem>())
        {
            Assert.False(gate.AllReady);
            gate.Ready(subsystem);
        }
        Assert.True(gate.AllReady);
        Assert.Empty(gate.NotReady);
    }

    [Fact]
    public void A_failure_keeps_the_original_exception_object_and_describe_names_it()
    {
        var gate = new ReadinessGate();
        var error = new InvalidDataException("navmesh version 3");
        gate.Fail(Subsystem.Nav, error);

        Assert.Same(error, gate.StatusOf(Subsystem.Nav).Error);
        Assert.Contains("Nav=Failed (InvalidDataException: navmesh version 3)", gate.Describe());
        Assert.Contains("Vm=NotStarted", gate.Describe());
    }

    [Fact]
    public void A_repeated_failure_counts_and_keeps_the_latest_error_and_a_retry_clears_it_but_not_the_count()
    {
        var gate = new ReadinessGate();
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        gate.Fail(Subsystem.Audio, first);
        gate.Fail(Subsystem.Audio, second);
        Assert.Same(second, gate.StatusOf(Subsystem.Audio).Error);
        Assert.Equal(2, gate.StatusOf(Subsystem.Audio).Failures);

        gate.Pending(Subsystem.Audio);
        Assert.Null(gate.StatusOf(Subsystem.Audio).Error);
        Assert.Equal(2, gate.StatusOf(Subsystem.Audio).Failures);
        gate.Ready(Subsystem.Audio);
        Assert.Equal(ReadyState.Ready, gate.StatusOf(Subsystem.Audio).State);
        Assert.Equal(2, gate.StatusOf(Subsystem.Audio).Failures);
    }

    [Fact]
    public void A_null_exception_is_refused_rather_than_recorded_as_a_failure_without_a_cause()
    {
        var gate = new ReadinessGate();
        Assert.Throws<ArgumentNullException>(() => gate.Fail(Subsystem.Vm, null!));
        Assert.Equal(ReadyState.NotStarted, gate.StatusOf(Subsystem.Vm).State);
    }

    [Fact]
    public void Changed_fires_once_per_report_with_the_new_status()
    {
        var gate = new ReadinessGate();
        var seen = new List<(Subsystem, ReadyState)>();
        gate.Changed += (subsystem, status) => seen.Add((subsystem, status.State));

        gate.Pending(Subsystem.Quests);
        gate.Ready(Subsystem.Quests);
        gate.Fail(Subsystem.Actors, new Exception("x"));

        Assert.Equal(new[] { (Subsystem.Quests, ReadyState.Pending), (Subsystem.Quests, ReadyState.Ready), (Subsystem.Actors, ReadyState.Failed) }, seen);
    }

    [Fact]
    public void The_trace_records_each_transition_only_while_a_sink_is_set()
    {
        var trace = new RuntimeTrace();
        var gate = new ReadinessGate(trace);
        gate.Pending(Subsystem.Vm);

        var lines = new List<(string, string)>();
        trace.Sink = (category, message) => lines.Add((category, message));
        gate.Ready(Subsystem.Vm);
        gate.Fail(Subsystem.Nav, new FileNotFoundException("navmesh"));

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal("Readiness", line.Item1));
        Assert.Equal("Vm -> Ready", lines[0].Item2);
        Assert.Equal("Nav -> Failed (FileNotFoundException: navmesh)", lines[1].Item2);
    }

    [Fact]
    public void A_throwing_subscriber_does_not_block_the_others_or_mask_the_report_and_is_kept()
    {
        var gate = new ReadinessGate();
        var seen = new List<ReadyState>();
        var boom = new InvalidOperationException("subscriber");
        gate.Changed += (_, _) => throw boom;
        gate.Changed += (_, status) => seen.Add(status.State);
        var original = new FileNotFoundException("navmesh");

        gate.Fail(Subsystem.Nav, original);

        Assert.Same(original, gate.StatusOf(Subsystem.Nav).Error);
        Assert.Equal(new[] { ReadyState.Failed }, seen);
        Assert.Same(boom, Assert.Single(gate.ObserverErrors));
    }

    [Fact]
    public void A_throwing_trace_sink_does_not_stop_the_report()
    {
        var trace = new RuntimeTrace { Sink = (_, _) => throw new InvalidOperationException("sink") };
        var gate = new ReadinessGate(trace);
        var seen = new List<ReadyState>();
        gate.Changed += (_, status) => seen.Add(status.State);

        gate.Fail(Subsystem.Audio, new Exception("x"));

        Assert.Equal(ReadyState.Failed, gate.StatusOf(Subsystem.Audio).State);
        Assert.Equal(new[] { ReadyState.Failed }, seen);
        Assert.Equal("sink", trace.SinkFailure!.Message);
    }

    [Fact]
    public void Concurrent_reports_lose_no_failure_count()
    {
        var gate = new ReadinessGate();
        Parallel.For(0, 200, _ => gate.Fail(Subsystem.Actors, new Exception("x")));
        Assert.Equal(200, gate.StatusOf(Subsystem.Actors).Failures);
    }
}
