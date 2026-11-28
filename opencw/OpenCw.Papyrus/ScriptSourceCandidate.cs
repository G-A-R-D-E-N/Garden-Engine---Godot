using System;
using System.Collections.Generic;
using InteropScriptSet = Radicle.Interop.ScriptSet;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>Where a script payload came from.</summary>
public enum ScriptSourceKind
{
    /// <summary>Inside an archive. Loses to a loose file with the same name.</summary>
    Archive = 0,

    /// <summary>A loose file under a Data tree. Wins over an archive with the same name.</summary>
    Loose = 1,
}

/// <summary>One place a script payload was found, with a reader for its bytes.</summary>
public sealed record ScriptSourceCandidate(
    string Name,
    ScriptSourceKind Kind,
    string ContainerPath,
    int LoadIndex,
    long ByteSize,
    Func<byte[]> Read);

/// <summary>
/// Loose scripts under a Data tree: Data/Scripts/**/*.pex. A higher load index wins.
/// </summary>
public sealed class LooseScriptSource : IDisposable
{
    public const string ScriptsFolderName = "Scripts";

    private readonly List<InteropScriptSet> _sets = new();
    private readonly List<ScriptSourceCandidate> _candidates = new();

    private LooseScriptSource()
    {
    }

    public IReadOnlyList<ScriptSourceCandidate> Candidates => _candidates;

    // Requires the batch to stay open until every candidate has been read: each candidate reads lazily from its root's set.
    public static LooseScriptSource Open(IEnumerable<(string DataRoot, int LoadIndex)> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var batch = new LooseScriptSource();
        try
        {
            foreach (var (dataRoot, loadIndex) in roots)
            {
                var set = InteropScriptSet.Open(new[] { dataRoot }, Array.Empty<string>());
                batch._sets.Add(set);
                for (var index = 0; index < set.Count; index++)
                {
                    var wanted = index;
                    var winner = set.Winner(wanted);
                    batch._candidates.Add(new ScriptSourceCandidate(winner.Name, ScriptSourceKind.Loose,
                        winner.Container, loadIndex, (long)winner.Size, () => set.Read(wanted)));
                }
            }
        }
        catch
        {
            batch.Dispose();
            throw;
        }
        return batch;
    }

    public void Dispose()
    {
        foreach (var set in _sets)
        {
            set.Dispose();
        }
        _sets.Clear();
    }
}
