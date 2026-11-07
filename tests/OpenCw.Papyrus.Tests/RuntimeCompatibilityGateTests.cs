using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Coverage;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class RuntimeCompatibilityGateTests
{
    private static string Registry(params string[] rows) =>
        $"{{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{string.Join(",", rows)}]}}";

    private static string Row(string key, string status, string probe, params string[] consumers) =>
        $"{{\"key\":\"{key}\",\"status\":\"{status}\",\"probe_status\":\"{probe}\",\"allowed_consumers\":[{string.Join(",", consumers.Select(consumer => $"\"{consumer}\""))}]}}";

    private static PartCTask[] Tasks(params (string Id, bool Done)[] tasks) =>
        tasks.Select(task => new PartCTask(task.Id, task.Id, task.Done)).ToArray();

    [Theory]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("{\"status\":\"UNPROVEN\",\"probe_status\":\"unrun\"}")]
    public void MalformedRowsCannotDisappearFromTheCompatibilityGate(string malformed)
    {
        var json = Registry(malformed);

        Assert.Throws<FormatException>(() => RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true))));
        Assert.Throws<FormatException>(() => PartCClosureGate.Run(json, Tasks(("C23b", true))));
    }

    [Fact]
    public void AnEmptyRegistryCannotClaimCompatibility()
    {
        Assert.Throws<FormatException>(() => RuntimeCompatibilityGate.Run(Registry(), Tasks(("C23b", true))));
        Assert.Throws<FormatException>(() => PartCClosureGate.Run(Registry(), Tasks(("C23b", true))));
    }

    [Fact]
    public void AMissingSystemBlocksTheCompatibilityClaim()
    {
        var json = Registry(
            Row("vm.assignment.defaults", "UNPROVEN", "unrun"),
            Row("magic.system", "UNPROVEN", "unrun"));

        var report = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));

        Assert.False(report.Compatible);
        Assert.Equal(new[] { "magic.system" }, report.MissingSystemBlockers);
        Assert.Equal(new[] { "vm.assignment.defaults" }, report.OpenEvidenceKeys);
        Assert.Contains("no compatibility claim", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenVmSemanticBlocksTheClaimEvenWithNoMissingSystems()
    {
        var json = Registry(Row("vm.assignment.defaults", "UNPROVEN", "unrun"));

        var report = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));

        Assert.False(report.Compatible);
        Assert.Empty(report.MissingSystemBlockers);
        Assert.Equal(new[] { "vm.assignment.defaults" }, report.OpenEvidenceKeys);
        Assert.Contains("no compatibility claim", report.Verdict, StringComparison.Ordinal);
        Assert.Contains("1 open evidence key", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpenTaskBlocksTheClaim()
    {
        var json = Registry(Row("vm.assignment.defaults", "UNPROVEN", "unrun"));

        var report = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", false)));

        Assert.False(report.Compatible);
        Assert.Empty(report.MissingSystemBlockers);
        Assert.Contains(report.Closure.Gaps, gap => gap.Subject == "C23b");
    }

    [Fact]
    public void AConsumedUnprovenRowBlocksTheClaim()
    {
        var json = Registry(Row("vm.assignment.defaults", "UNPROVEN", "unrun", "opencw/Vm.cs"));

        var report = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));

        Assert.False(report.Compatible);
        Assert.Contains(report.Closure.Gaps, gap => gap.Subject == "vm.assignment.defaults");
    }

    [Fact]
    public void AnApproximatedRowDoesNotBlockTheClaim()
    {
        var json = Registry(Row("vm.latent.waits", "APPROXIMATED", "unrun"));

        var report = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));

        Assert.True(report.Compatible);
        Assert.Equal(1, report.Closure.Approximated);
    }

    [Fact]
    public void TheRecordedRegistryNeverClaimsCompatibilityWithABlocker()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var json = File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json"));
        var tasks = new List<PartCTask>();
        for (var index = 0; index <= 23; ++index)
        {
            tasks.Add(new PartCTask($"C{index}", $"C{index}", true));
        }
        tasks.Add(new PartCTask("C23b", "close full runtime compatibility", true));

        var report = RuntimeCompatibilityGate.Run(json, tasks);

        Assert.Equal(report.MissingSystemBlockers.Count == 0 && report.OpenEvidenceKeys.Count == 0 && report.Closure.Clean,
            report.Compatible);
        Assert.All(report.MissingSystemBlockers, key => Assert.True(PartCClosureGate.NamesAMissingSystem(key)));
        if (!report.Compatible)
        {
            Assert.Contains("no compatibility claim", report.Verdict, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheReportIsDeterministicAndCarriesTheClosureView()
    {
        var json = Registry(Row("magic.system", "UNPROVEN", "unrun"));

        var first = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));
        var second = RuntimeCompatibilityGate.Run(json, Tasks(("C23b", true)));

        Assert.Contains("verdict:", first.ToReport(), StringComparison.Ordinal);
        Assert.Contains("closure gate, not a compatibility claim", first.ToReport(), StringComparison.Ordinal);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
    }
}
