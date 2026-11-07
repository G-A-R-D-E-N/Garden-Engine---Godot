using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Acceptance;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class RadiclePinGateTests
{
    private static PinProbe Probe(PinReachability reachability, int abi = 8, string commit = "cca4b080309053cff127b7202d388df00060388a") =>
        new("GARDEN/GARDEN-TOOLS", commit, abi, reachability);

    [Fact]
    public void AMainCommitWithTheExpectedAbiHasLanded()
    {
        var result = RadiclePinGate.Check(Probe(PinReachability.Main), 8);

        Assert.True(result.Landed);
        Assert.Equal(PinGateState.Landed, result.State);
        Assert.Contains("main", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANomadDevCommitIsAcceptedButNotLanded()
    {
        var result = RadiclePinGate.Check(Probe(PinReachability.NomadDev), 8);

        Assert.True(result.Accepted);
        Assert.False(result.Landed);
        Assert.Equal(PinGateState.NomadDev, result.State);
        Assert.Contains("nomad/dev", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFeatureBranchCommitIsRefused()
    {
        var result = RadiclePinGate.Check(Probe(PinReachability.FeatureBranch), 8);

        Assert.False(result.Landed);
        Assert.Equal(PinGateState.FeatureBranch, result.State);
        Assert.Contains("unmerged work", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingCommitIsRefused()
    {
        var result = RadiclePinGate.Check(Probe(PinReachability.Missing), 8);

        Assert.Equal(PinGateState.Missing, result.State);
        Assert.Contains("not in", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbiMismatchIsRefusedEvenOnMain()
    {
        var result = RadiclePinGate.Check(Probe(PinReachability.Main, abi: 7), 8);

        Assert.Equal(PinGateState.AbiMismatch, result.State);
        Assert.False(result.Landed);
    }

    [Fact]
    public void TheRecordedPinIsAcceptedOnItsBranch()
    {
        var probe = Probe(OpenCommonwealth.Services.Papyrus.RadiclePin.Reachability,
            abi: OpenCommonwealth.Services.Papyrus.RadiclePin.AbiVersion,
            commit: OpenCommonwealth.Services.Papyrus.RadiclePin.Commit);

        var result = RadiclePinGate.Check(probe, OpenCommonwealth.Services.Papyrus.RadiclePin.AbiVersion);

        Assert.Equal(PinGateState.NomadDev, result.State);
        Assert.True(result.Accepted);
        Assert.False(result.Landed);
        Assert.Contains("nomad/dev", result.Reason, StringComparison.Ordinal);
        Assert.Equal(OpenCommonwealth.Services.Papyrus.RadiclePin.Repository, result.Probe.Repository);
    }

    [Fact]
    public void EveryProbeIsReportedAndOrdered()
    {
        var results = RadiclePinGate.CheckAll(new[]
        {
            Probe(PinReachability.FeatureBranch, commit: "bbbb"),
            Probe(PinReachability.Main, commit: "aaaa"),
            Probe(PinReachability.Missing, commit: "cccc"),
            Probe(PinReachability.NomadDev, commit: "dddd"),
        }, 8);

        Assert.Equal(4, results.Count);
        Assert.Equal(new[] { "aaaa", "bbbb", "cccc", "dddd" },
            results.Select(result => result.Probe.Commit).ToArray());
        Assert.Equal(1, results.Count(result => result.Landed));
        Assert.Equal(2, results.Count(result => result.Accepted));
    }
}
