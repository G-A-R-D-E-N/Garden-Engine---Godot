using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Acceptance;

/// <summary>Where a Radicle commit can be reached from.</summary>
public enum PinReachability
{
    /// <summary>Reachable from GARDEN-TOOLS main.</summary>
    Main,

    /// <summary>Invariant: reachable from GARDEN-TOOLS nomad/dev, the integration branch, not main.</summary>
    NomadDev,

    /// <summary>Only reachable from a feature branch.</summary>
    FeatureBranch,

    /// <summary>Not present in the repository at all.</summary>
    Missing,
}

public sealed record PinProbe(string Repository, string Commit, int AbiVersion, PinReachability Reachability);

public enum PinGateState
{
    /// <summary>The pin is on main and the ABI matches.</summary>
    Landed,

    /// <summary>Invariant: the pin is on the nomad/dev integration branch and the ABI matches.</summary>
    NomadDev,

    /// <summary>The pin is still a feature-branch commit.</summary>
    FeatureBranch,

    /// <summary>The pin is not in the repository.</summary>
    Missing,

    /// <summary>The pinned ABI is not the ABI this build expects.</summary>
    AbiMismatch,
}

public sealed record PinGateResult(PinGateState State, string Reason, PinProbe Probe)
{
    public bool Landed => State == PinGateState.Landed;

    /// <summary>Invariant: true when the pin is on main or nomad/dev, with the expected ABI.</summary>
    public bool Accepted => State is PinGateState.Landed or PinGateState.NomadDev;
}

/// <summary>Invariant: the gate accepts a main or nomad/dev pin with the expected ABI and refuses every other state.</summary>
/// <remarks>Invariant: a feature-branch-only commit, a missing commit and an ABI mismatch are refused with their own reason; NomadDev is accepted but is not Landed, and the gate still proves the commit is on the recorded branch.</remarks>
public static class RadiclePinGate
{
    public static PinGateResult Check(PinProbe probe, int expectedAbiVersion)
    {
        if (probe.Reachability == PinReachability.Missing)
        {
            return new PinGateResult(PinGateState.Missing,
                $"the commit {probe.Commit} is not in {probe.Repository}", probe);
        }
        if (probe.AbiVersion != expectedAbiVersion)
        {
            return new PinGateResult(PinGateState.AbiMismatch,
                $"the pin carries C ABI {probe.AbiVersion} but this build expects {expectedAbiVersion}", probe);
        }
        if (probe.Reachability == PinReachability.NomadDev)
        {
            return new PinGateResult(PinGateState.NomadDev,
                $"the commit {probe.Commit} is on {probe.Repository} nomad/dev at C ABI {probe.AbiVersion}; the pin is intentionally on the integration branch, not main",
                probe);
        }
        if (probe.Reachability == PinReachability.FeatureBranch)
        {
            return new PinGateResult(PinGateState.FeatureBranch,
                $"the commit {probe.Commit} is reachable only from a feature branch, so Part B still depends on unmerged work",
                probe);
        }
        return new PinGateResult(PinGateState.Landed,
            $"the commit {probe.Commit} is on {probe.Repository} main at C ABI {probe.AbiVersion}", probe);
    }

    /// <summary>
    /// Reports every probe, so a repository with several pins cannot hide one that has not landed.
    /// </summary>
    public static IReadOnlyList<PinGateResult> CheckAll(IEnumerable<PinProbe> probes, int expectedAbiVersion) =>
        probes.OrderBy(probe => probe.Repository, StringComparer.Ordinal)
            .ThenBy(probe => probe.Commit, StringComparer.Ordinal)
            .Select(probe => Check(probe, expectedAbiVersion))
            .ToList();
}
