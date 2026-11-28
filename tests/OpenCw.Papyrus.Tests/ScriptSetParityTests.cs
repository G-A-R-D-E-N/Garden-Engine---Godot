using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus;
using Radicle.Interop.Native;
using InteropScriptSet = Radicle.Interop.ScriptSet;
using InteropAssetResolver = Radicle.Interop.AssetResolver;
using Xunit;

namespace OpenCw.Papyrus.Tests;

// Invariant: the precedence fixtures must resolve to the documented winners through Radicle's resolver.
public class ScriptSetParityTests
{
    private static ScriptSourceCandidate Candidate(string name, ScriptSourceKind kind, int loadIndex, string container,
        string payload) =>
        new(name, kind, container, loadIndex, payload.Length, () => Encoding.UTF8.GetBytes(payload));

    [Fact]
    public void NormalizeNameMatchesThePortedFixtures()
    {
        Assert.Equal("quests/qf_test", ScriptNames.NormalizeName("Quests\\QF_Test.PEX"));
        Assert.Equal("alias_script", ScriptNames.NormalizeName("alias_script"));
        Assert.Equal("quests/qf_test", ScriptNames.NormalizeName(@"Scripts\Quests\QF_Test.pex"));
    }

    [Fact]
    public void LooseBeatsArchiveEvenAtALowerLoadIndex()
    {
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Archive, 50, "archive", "archive"),
            Candidate("a", ScriptSourceKind.Loose, 0, "loose", "loose"),
        };

        var resolved = ScriptSelection.ResolveCandidates(candidates);

        var winner = Assert.Single(resolved);
        Assert.Equal("a", winner.Name);
        Assert.Same(candidates[1], winner.Winner);
        Assert.Equal(1, winner.Replaced);
        Assert.Equal(ScriptSourceKind.Loose, winner.Winner.Kind);
    }

    [Fact]
    public void HigherLoadIndexWinsAmongLooseScripts()
    {
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 2, "two", "two"),
            Candidate("a", ScriptSourceKind.Loose, 7, "seven", "seven"),
        };

        var resolved = ScriptSelection.ResolveCandidates(candidates);

        var winner = Assert.Single(resolved);
        Assert.Same(candidates[1], winner.Winner);
        Assert.Equal(7, winner.Winner.LoadIndex);
        Assert.Equal("seven", Encoding.UTF8.GetString(winner.Winner.Read()));
    }

    [Fact]
    public void ResolveCountsReplacedCandidatesAndOrdersByName()
    {
        var candidates = new[]
        {
            Candidate("b", ScriptSourceKind.Loose, 0, "b", "b"),
            Candidate("a", ScriptSourceKind.Archive, 3, "a3", "a3"),
            Candidate("a", ScriptSourceKind.Archive, 4, "a4", "a4"),
        };

        var resolved = ScriptSelection.ResolveCandidates(candidates);

        Assert.Equal(new[] { "a", "b" }, resolved.Select(entry => entry.Name).ToArray());
        Assert.Equal(1, resolved[0].Replaced);
        Assert.Equal(0, resolved[1].Replaced);
    }

    [Fact]
    public void AnOrdinallyGreaterContainerBreaksATieOfKindAndLoadIndex()
    {
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 3, "Z", "upper"),
            Candidate("a", ScriptSourceKind.Loose, 3, "a", "lower"),
        };

        var resolved = ScriptSelection.ResolveCandidates(candidates);

        var winner = Assert.Single(resolved);
        Assert.Same(candidates[1], winner.Winner);
        Assert.Equal(1, winner.Replaced);
    }

    [Fact]
    public void ANestedScriptsFolderKeepsItsOwnNameOnEveryPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "ge-script-set-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteScript(root, "foo", "top");
            Directory.CreateDirectory(Path.Combine(root, "Scripts", "Scripts"));
            File.WriteAllText(Path.Combine(root, "Scripts", "Scripts", "foo.pex"), "nested", new UTF8Encoding(false));

            using var looseSource = LooseScriptSource.Open(new[] { (root, 0) });
            var loose = looseSource.Candidates
                .Select(candidate => candidate.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            using var set = InteropScriptSet.Open(new[] { root }, Array.Empty<string>());

            Assert.Equal(new[] { "foo", "scripts/foo" }, loose);
            Assert.Equal(loose, set.Winners.Select(winner => winner.Name).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void TheAssetsResolverPicksTheHigherLoadIndexRootAndReadsItsPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "ge-script-set-" + Guid.NewGuid().ToString("N"));
        var low = Path.Combine(root, "low");
        var high = Path.Combine(root, "high");
        try
        {
            WriteScript(low, "qf_test", "low");
            WriteScript(high, "qf_test", "high");
            WriteScript(low, "only_low", "only-low");

            using var assets = InteropAssetResolver.OpenRoots(new[] { low, high }, Array.Empty<string>());
            using var set = InteropScriptSet.Resolve(assets, new[] { 0, 1 });

            var winners = set.Winners.ToList();
            Assert.Equal(2, winners.Count);
            Assert.Equal("only_low", winners[0].Name);
            Assert.Equal("qf_test", winners[1].Name);
            var winner = winners[1];
            Assert.Equal(AssetOrigin.Loose, winner.Origin);
            Assert.Equal(1, winner.LoadIndex);
            Assert.Equal("high", Encoding.UTF8.GetString(set.Read(1)));
            Assert.Equal(1, winner.Replaced);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void WriteScript(string dataRoot, string name, string payload)
    {
        var directory = Path.Combine(dataRoot, "Scripts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".pex"), payload, new UTF8Encoding(false));
    }
}
