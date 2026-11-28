using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EvidenceGateDecisionTests
{
    private static EvidenceGate Gate(params (string Key, string Status)[] rows)
    {
        var entries = new List<string>();
        foreach (var (key, status) in rows)
        {
            entries.Add($"{{\"key\":\"{key}\",\"status\":\"{status}\"}}");
        }
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    [Fact]
    public void ProvenAndPolicyKeysAreAllowedWithoutCounting()
    {
        var gate = Gate(("p", "PROVEN"), ("e", "ENGINE_POLICY"));

        Assert.Equal(new GateDecision(GateOutcome.Allowed, "p", EvidenceStatus.Proven, "proven"), gate.Gate("p"));
        Assert.Equal(new GateDecision(GateOutcome.Allowed, "e", EvidenceStatus.EnginePolicy, "a deliberate Garden decision"), gate.Gate("e"));
        Assert.Equal(0, gate.StopCount);
        Assert.Equal(0, gate.ApproximationCount);
    }

    [Fact]
    public void AnUnprovenKeyIsAllowedAndCountedAsAStopPerCall()
    {
        var gate = Gate(("u", "UNPROVEN"));

        var first = gate.Gate("u");
        gate.Gate("u");

        Assert.Equal(new GateDecision(GateOutcome.Allowed, "u", EvidenceStatus.Unproven, "the behavior is unproven; allowed and counted"), first);
        Assert.True(first.Allowed);
        Assert.Equal(2, gate.StopCount);
        Assert.Equal(2, gate.Stops["u"]);
        Assert.Equal(0, gate.ApproximationCount);
    }

    [Fact]
    public void AContradictedKeyIsAllowedAndCountedWithItsOwnReason()
    {
        var gate = Gate(("c", "CONTRADICTED"));

        var decision = gate.Gate("c");

        Assert.Equal(new GateDecision(GateOutcome.Allowed, "c", EvidenceStatus.Contradicted,
            "the measurement contradicts the hypothesis this relies on; allowed and counted"), decision);
        Assert.Equal(1, gate.StopCount);
        Assert.Equal(1, gate.Stops["c"]);
    }

    [Fact]
    public void AKeyWithNoRowIsAllowedAsUnprovenAndCountedUnderItsName()
    {
        var gate = Gate(("p", "PROVEN"));

        var decision = gate.Gate("missing");

        Assert.Equal(new GateDecision(GateOutcome.Allowed, "missing", EvidenceStatus.Unproven,
            "the evidence registry has no row for this key; allowed and counted"), decision);
        Assert.Equal(1, gate.StopCount);
        Assert.Equal(new Dictionary<string, int> { ["missing"] = 1 }, gate.Stops);
    }

    [Fact]
    public void ApproximationsAreCountedPerKeyAndNeverAsStops()
    {
        var gate = Gate(("a", "APPROXIMATED"), ("b", "APPROXIMATED"));

        var decision = gate.Gate("a");
        gate.Gate("a");
        gate.Gate("b");

        Assert.Equal(new GateDecision(GateOutcome.Approximated, "a", EvidenceStatus.Approximated,
            "Garden implements this only in part and reports every reliance"), decision);
        Assert.True(decision.Allowed);
        Assert.Equal(3, gate.ApproximationCount);
        Assert.Equal(2, gate.Approximations["a"]);
        Assert.Equal(1, gate.Approximations["b"]);
        Assert.Equal(0, gate.StopCount);
        Assert.Empty(gate.Stops);
    }

    [Fact]
    public void AnInternalProbeCanReadAnApproximationWithoutCountingIt()
    {
        var gate = Gate(("a", "APPROXIMATED"));

        var decision = gate.Gate("a", countApproximation: false);

        Assert.Equal(GateOutcome.Approximated, decision.Outcome);
        Assert.Equal(0, gate.ApproximationCount);
        Assert.Empty(gate.Approximations);
    }

    [Fact]
    public void OnlyAStoppedDecisionIsNotAllowed()
    {
        Assert.True(new GateDecision(GateOutcome.Allowed, "k", EvidenceStatus.Proven, "r").Allowed);
        Assert.True(new GateDecision(GateOutcome.Approximated, "k", EvidenceStatus.Approximated, "r").Allowed);
        Assert.False(new GateDecision(GateOutcome.Stopped, "k", EvidenceStatus.Contradicted, "r").Allowed);
    }
}
