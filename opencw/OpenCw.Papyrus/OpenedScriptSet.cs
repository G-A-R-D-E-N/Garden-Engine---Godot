using System;
using System.Collections.Generic;
using System.Linq;
using InteropScriptSet = Radicle.Interop.ScriptSet;

namespace OpenCommonwealth.Services.Papyrus;

public sealed class OpenedScriptSet : IDisposable
{
    private readonly InteropScriptSet _set;

    private OpenedScriptSet(InteropScriptSet set, IReadOnlyList<(string Name, ScriptSourceCandidate Winner, int Replaced)> winners,
        IReadOnlyList<string> unavailable)
    {
        _set = set;
        Winners = winners;
        Unavailable = unavailable;
    }

    public IReadOnlyList<(string Name, ScriptSourceCandidate Winner, int Replaced)> Winners { get; }

    public IReadOnlyList<string> Unavailable { get; }

    // Requires: dataRoots ascend in priority and archives ascend in load order, as GameLoader hands them out; the set stays open until every winner has been read.
    public static OpenedScriptSet Open(IReadOnlyList<string> dataRoots, IReadOnlyList<string> archives)
    {
        ArgumentNullException.ThrowIfNull(dataRoots);
        ArgumentNullException.ThrowIfNull(archives);
        // Invariant: ScriptSet.Open takes roots high to low and numbers a loose script from the end, so reversing keeps each root's own index.
        var set = InteropScriptSet.Open(dataRoots.Reverse(), archives, Radicle.Interop.ArchiveFailurePolicy.Skip);
        try
        {
            var skipped = set.Failures.Select(failure => failure.ArchiveIndex).ToHashSet();
            var opened = Enumerable.Range(0, archives.Count).Where(index => !skipped.Contains(index)).ToArray();
            var winners = new List<(string, ScriptSourceCandidate, int)>(set.Count);
            for (var index = 0; index < set.Count; index++)
            {
                var wanted = index;
                var winner = set.Winner(wanted);
                var loose = winner.Origin == Radicle.Interop.Native.AssetOrigin.Loose;
                winners.Add((winner.Name, new ScriptSourceCandidate(winner.Name,
                    loose ? ScriptSourceKind.Loose : ScriptSourceKind.Archive, winner.Container,
                    loose ? winner.LoadIndex : opened[winner.LoadIndex], (long)winner.Size, () => set.Read(wanted)),
                    winner.Replaced));
            }

            var unavailable = set.Failures
                .Select(failure => $"{failure.Path}: Radicle could not open the archive: {failure.Message}").ToArray();
            return new OpenedScriptSet(set, winners, unavailable);
        }
        catch
        {
            set.Dispose();
            throw;
        }
    }

    public void Dispose() => _set.Dispose();
}
