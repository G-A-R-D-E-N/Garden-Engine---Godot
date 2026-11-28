using System;
using System.Collections.Generic;
using System.Linq;
using InteropScriptSelection = Radicle.Interop.ScriptSelection;

namespace OpenCommonwealth.Services.Papyrus;

// Invariant: a script name is the lower-case Data-relative path under Scripts/ without the .pex extension.
public static class ScriptNames
{
    public static string NormalizeName(string relativePath) => Radicle.Interop.ScriptNames.Normalize(relativePath);
}

// Invariant: Radicle's rule over an explicit candidate list: loose beats archive, then higher load index, then ordinal container.
public static class ScriptSelection
{
    public static IReadOnlyList<(string Name, ScriptSourceCandidate Winner, int Replaced)> ResolveCandidates(
        IReadOnlyList<ScriptSourceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var rows = candidates.Select(candidate => new Radicle.Interop.ScriptCandidateRow(
            candidate.Name,
            candidate.Kind == ScriptSourceKind.Loose
                ? Radicle.Interop.ScriptCandidateKind.Loose
                : Radicle.Interop.ScriptCandidateKind.Archive,
            candidate.ContainerPath, candidate.LoadIndex, candidate.ByteSize)).ToList();
        return InteropScriptSelection.Resolve(rows)
            .Select(winner => (winner.Name, candidates[winner.CandidateIndex], winner.Replaced))
            .ToList();
    }
}
