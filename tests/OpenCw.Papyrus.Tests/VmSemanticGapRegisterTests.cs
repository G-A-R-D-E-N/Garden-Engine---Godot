using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Coverage;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class VmSemanticGapRegisterTests
{
    private static EvidenceGate Gate(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static string Registry(string key, string status, string probeStatus) =>
        $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{{\"key\":\"{key}\",\"status\":\"{status}\",\"probe_status\":\"{probeStatus}\"}}]}}";

    [Fact]
    public void AnUnprovenVmOnlyKeyIsOpenAndRefused()
    {
        var gate = Gate((EvidenceKeys.Assignment, "UNPROVEN"));

        var report = VmSemanticGapRegister.Run(
            Registry(EvidenceKeys.Assignment, "UNPROVEN", "unrun"), new[] { EvidenceKeys.Assignment }, gate);

        var row = Assert.Single(report.Rows);
        Assert.False(row.Closed);
        Assert.True(row.RefusedByGate);
        Assert.Equal(1, report.Open);
        Assert.Contains("refuses", row.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AProvenKeyWithARunProbeIsClosed()
    {
        var gate = Gate((EvidenceKeys.Assignment, "PROVEN"));

        var report = VmSemanticGapRegister.Run(
            Registry(EvidenceKeys.Assignment, "PROVEN", "run"), new[] { EvidenceKeys.Assignment }, gate);

        Assert.True(Assert.Single(report.Rows).Closed);
        Assert.Equal(1, report.Closed);
        Assert.True(report.Clean);
    }

    [Fact]
    public void AProvenKeyWithoutALoggedProbeIsReported()
    {
        var gate = Gate((EvidenceKeys.Assignment, "PROVEN"));

        var report = VmSemanticGapRegister.Run(
            Registry(EvidenceKeys.Assignment, "PROVEN", "unrun"), new[] { EvidenceKeys.Assignment }, gate);

        Assert.False(report.Clean);
        Assert.Equal(new[] { EvidenceKeys.Assignment }, report.PromotedWithoutEvidence);
    }

    [Fact]
    public void AnEnginePolicyKeyIsClosedAndAMissingRowFailsClosed()
    {
        var gate = Gate((EvidenceKeys.Frames, "ENGINE_POLICY"));

        var report = VmSemanticGapRegister.Run(
            Registry(EvidenceKeys.Frames, "ENGINE_POLICY", "not_applicable"),
            new[] { EvidenceKeys.Frames, "vm.not.a.row" }, gate);

        Assert.Equal(1, report.Closed);
        Assert.Equal(1, report.Open);
        Assert.Contains(report.Rows, row => row.Key == "vm.not.a.row" && row.Status == "MISSING" && row.RefusedByGate);
    }

    [Fact]
    public void TheRecordedRegistryPromotesNothingWithoutEvidence()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json");
        var json = File.ReadAllText(path);
        var registry = EvidenceRegistry.Load(json);
        var gate = new EvidenceGate(registry);
        var vmKeys = new[]
        {
            EvidenceKeys.Frames, EvidenceKeys.NativeTarget, EvidenceKeys.Assignment, EvidenceKeys.Arithmetic,
            EvidenceKeys.Types, EvidenceKeys.Dispatch, EvidenceKeys.Arrays, EvidenceKeys.Structs,
            EvidenceKeys.Waits, EvidenceKeys.OnInitBarrier, EvidenceKeys.EventDuringOnInit,
            EvidenceKeys.SchedulerBudget, EvidenceKeys.Host, EvidenceKeys.FailClosed,
        };

        var report = VmSemanticGapRegister.Run(json, vmKeys, gate);

        Assert.True(report.Clean, string.Join(", ", report.PromotedWithoutEvidence));
        Assert.Equal(vmKeys.Length, report.Rows.Count);
        Assert.Equal(report.Rows.Count, report.Closed + report.Open);
        Assert.Contains(report.Rows, row => row.Key == EvidenceKeys.Assignment && !row.Closed);
        Assert.Contains(report.Rows, row => row.Key == EvidenceKeys.Frames && row.Closed);
    }

    [Fact]
    public void TheRegisterIsDeterministic()
    {
        var gate = Gate((EvidenceKeys.Assignment, "UNPROVEN"), (EvidenceKeys.Frames, "ENGINE_POLICY"));
        var json = $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[" +
                   $"{{\"key\":\"{EvidenceKeys.Frames}\",\"status\":\"ENGINE_POLICY\",\"probe_status\":\"not_applicable\"}}," +
                   $"{{\"key\":\"{EvidenceKeys.Assignment}\",\"status\":\"UNPROVEN\",\"probe_status\":\"unrun\"}}]}}";

        var first = VmSemanticGapRegister.Run(json, new[] { EvidenceKeys.Assignment, EvidenceKeys.Frames }, gate);
        var second = VmSemanticGapRegister.Run(json, new[] { EvidenceKeys.Frames, EvidenceKeys.Assignment }, gate);

        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
    }
}
