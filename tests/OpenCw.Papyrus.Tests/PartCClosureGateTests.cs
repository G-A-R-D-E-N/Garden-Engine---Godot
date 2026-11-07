using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Coverage;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PartCClosureGateTests
{
    private static string Registry(params string[] rows) =>
        $"{{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{string.Join(",", rows)}]}}";

    private static string Row(string key, string status, string probe, params string[] consumers) =>
        $"{{\"key\":\"{key}\",\"status\":\"{status}\",\"probe_status\":\"{probe}\",\"allowed_consumers\":[{string.Join(",", consumers.Select(consumer => $"\"{consumer}\""))}]}}";

    private static PartCTask[] Tasks(params (string Id, bool Done)[] tasks) =>
        tasks.Select(task => new PartCTask(task.Id, task.Id, task.Done)).ToArray();

    [Fact]
    public void AClosedRegistryWithFinishedTasksIsClean()
    {
        var json = Registry(
            Row("vm.frames.explicit_stack", "ENGINE_POLICY", "not_applicable", "opencw/Vm.cs"),
            Row("vm.assignment.defaults", "UNPROVEN", "unrun"),
            Row("magic.system", "UNPROVEN", "unrun"),
            Row("vm.latent.waits", "APPROXIMATED", "unrun"));

        var report = PartCClosureGate.Run(json, Tasks(("C0", true), ("C23", true)));

        Assert.True(report.Clean);
        Assert.Equal(1, report.Closed);
        Assert.Equal(1, report.Approximated);
        Assert.Equal(1, report.OpenKeys);
        Assert.Equal(1, report.MissingSystems);
        Assert.Empty(report.Gaps);
    }

    [Fact]
    public void AnUnprovenRowWithAConsumerIsAGap()
    {
        var json = Registry(Row("vm.assignment.defaults", "UNPROVEN", "unrun", "opencw/Vm.cs"));

        var report = PartCClosureGate.Run(json, Tasks(("C0", true)));

        Assert.False(report.Clean);
        var gap = Assert.Single(report.Gaps);
        Assert.Equal("vm.assignment.defaults", gap.Subject);
        Assert.Contains("may not stay UNPROVEN", gap.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclassifiedRowIsAGap()
    {
        var json = Registry(Row("vm.something.unclassified", "UNPROVEN", "run"));

        var report = PartCClosureGate.Run(json, Tasks(("C0", true)));

        Assert.False(report.Clean);
        Assert.Contains(report.Rows, row => row.Reason.StartsWith("unclassified", StringComparison.Ordinal));
        Assert.Single(report.Gaps);
    }

    [Fact]
    public void AContradictedRowIsClosed()
    {
        var json = Registry(Row("pex.backend.mutagen_pex", "CONTRADICTED", "run"));

        var report = PartCClosureGate.Run(json, Tasks(("C0", true)));

        Assert.True(report.Clean);
        Assert.Equal(1, report.Closed);
    }

    [Fact]
    public void AnOpenTaskIsAGap()
    {
        var json = Registry(Row("magic.system", "UNPROVEN", "unrun"));

        var report = PartCClosureGate.Run(json, Tasks(("C0", true), ("C23b", false)));

        Assert.False(report.Clean);
        var gap = Assert.Single(report.Gaps);
        Assert.Equal("C23b", gap.Subject);
        Assert.Contains("still open", gap.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingSystemIsNamedRatherThanUnclassified()
    {
        Assert.True(PartCClosureGate.NamesAMissingSystem("magic.system"));
        Assert.True(PartCClosureGate.NamesAMissingSystem("system.inventory"));
        Assert.True(PartCClosureGate.NamesAMissingSystem("ai.package.system"));
        Assert.False(PartCClosureGate.NamesAMissingSystem("vm.assignment.defaults"));
    }

    [Fact]
    public void TheReportStatesItIsNotACompatibilityClaimAndIsDeterministic()
    {
        var json = Registry(Row("vm.assignment.defaults", "UNPROVEN", "unrun"));

        var first = PartCClosureGate.Run(json, Tasks(("C0", true)));
        var second = PartCClosureGate.Run(json, Tasks(("C0", true)));

        Assert.Contains("not a compatibility claim", first.ToReport(), StringComparison.Ordinal);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
    }

    [Fact]
    public void TheRecordedRegistryHasNoUnclassifiedRow()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var json = File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"));
        var tasks = new List<PartCTask>();
        for (var index = 0; index <= 22; ++index)
        {
            tasks.Add(new PartCTask($"C{index}", $"C{index}", true));
        }
        tasks.Add(new PartCTask("C23", "close the framework coverage", true));
        tasks.Add(new PartCTask("C23b", "final coverage gate", false));

        var report = PartCClosureGate.Run(json, tasks);

        Assert.DoesNotContain(report.Rows, row => row.Reason.StartsWith("unclassified", StringComparison.Ordinal));
        Assert.All(report.Gaps, gap => Assert.True(
            gap.Subject == "C23b" || gap.Reason.Contains("may not stay", StringComparison.Ordinal),
            $"unexpected gap {gap.Subject}: {gap.Reason}"));
        Assert.Contains(report.Gaps, gap => gap.Subject == "C23b");
    }
}
