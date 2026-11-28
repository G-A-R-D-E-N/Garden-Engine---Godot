using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class TempCache : IDisposable
{
    public TempCache()
    {
        Root = Path.Combine(Path.GetTempPath(), "opencw_papyrus_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
        catch (IOException)
        {
        }
    }
}

public class ScriptCacheTests
{
    private static byte[] Pex(string payload)
    {
        var token = System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16];
        return CacheFixture.Candidates(TestHosts.Stub("Pex" + token)).Single().Read();
    }

    private static ScriptSourceCandidate Candidate(
        string name,
        ScriptSourceKind kind,
        int loadIndex,
        string payload,
        string? container = null)
    {
        var pex = Pex(payload);
        return new(name, kind, container ?? $"{kind}-{loadIndex}-{name}", loadIndex, pex.Length, () => pex);
    }

    private static IEnumerable<ScriptSourceCandidate[]> Permutations(
        IReadOnlyList<ScriptSourceCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            yield return Array.Empty<ScriptSourceCandidate>();
            yield break;
        }
        for (var index = 0; index < candidates.Count; ++index)
        {
            var rest = candidates.Where((_, position) => position != index).ToList();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { candidates[index] }.Concat(tail).ToArray();
            }
        }
    }

    [Fact]
    public void NormalizeNameLowercasesAndStripsTheExtension()
    {
        Assert.Equal("quests/qf_test", ScriptNames.NormalizeName("Quests\\QF_Test.PEX"));
        Assert.Equal("alias_script", ScriptNames.NormalizeName("alias_script"));
    }

    [Fact]
    public void NormalizeNameRefusesAParentComponentAsAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ScriptNames.NormalizeName("Scripts/../escape.pex"));
    }

    [Fact]
    public void WinnerIsIndependentOfEnumerationOrder()
    {
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 1, "loose-one"),
            Candidate("a", ScriptSourceKind.Archive, 9, "archive-nine"),
            Candidate("a", ScriptSourceKind.Loose, 0, "loose-zero"),
        };

        foreach (var permutation in Permutations(candidates))
        {
            var winner = ScriptSelection.ResolveCandidates(permutation).Single().Winner;
            Assert.NotNull(winner);
            Assert.Equal(ScriptSourceKind.Loose, winner!.Kind);
            Assert.Equal(1, winner.LoadIndex);
        }
    }

    [Fact]
    public void LooseBeatsArchiveEvenAtALowerLoadIndex()
    {
        var loose = Candidate("a", ScriptSourceKind.Loose, 0, "loose");
        var winner = ScriptSelection.ResolveCandidates(new[]
        {
            Candidate("a", ScriptSourceKind.Archive, 50, "archive"),
            loose,
        }).Single().Winner;

        Assert.Equal(ScriptSourceKind.Loose, winner!.Kind);
        Assert.Equal(loose.Read(), winner.Read());
    }

    [Fact]
    public void HigherLoadIndexWinsAmongLooseScripts()
    {
        var seven = Candidate("a", ScriptSourceKind.Loose, 7, "seven");
        var winner = ScriptSelection.ResolveCandidates(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 2, "two"),
            seven,
        }).Single().Winner;

        Assert.Equal(7, winner!.LoadIndex);
        Assert.Equal(seven.Read(), winner.Read());
    }

    [Fact]
    public void ResolveCountsReplacedCandidatesAndOrdersByName()
    {
        var resolved = ScriptSelection.ResolveCandidates(new[]
        {
            Candidate("b", ScriptSourceKind.Loose, 0, "b"),
            Candidate("a", ScriptSourceKind.Archive, 3, "a3"),
            Candidate("a", ScriptSourceKind.Archive, 4, "a4"),
        });

        Assert.Equal(new[] { "a", "b" }, resolved.Select(entry => entry.Name).ToArray());
        Assert.Equal(1, resolved[0].Replaced);
        Assert.Equal(0, resolved[1].Replaced);
    }

    [Fact]
    public void BuildWritesWinnersAndManifest()
    {
        using var cache = new TempCache();
        var loose = Candidate("quests/qf_one", ScriptSourceKind.Loose, 1, "loose-payload");
        var result = ScriptCacheBuilder.Build(new[]
        {
            loose,
            Candidate("quests/qf_one", ScriptSourceKind.Archive, 4, "archive-payload"),
            Candidate("alias_script", ScriptSourceKind.Archive, 4, "archive-only"),
        }, @"C:\Data", cache.Root);

        Assert.Equal(2, result.Manifest.ScriptCount);
        Assert.Equal(1, result.Manifest.LooseCount);
        Assert.Equal(1, result.Manifest.ArchiveCount);
        Assert.Equal(1, result.ReplacedCount);

        var pex = ScriptCacheLayout.PexPath(cache.Root, "quests/qf_one");
        Assert.True(File.Exists(pex));
        Assert.Equal(loose.Read(), File.ReadAllBytes(pex));

        var manifest = ScriptCacheBuilder.Load(cache.Root);
        Assert.NotNull(manifest);
        Assert.Equal("loose", manifest!.Find("quests/qf_one")!.Kind);
        Assert.Equal("archive", manifest.Find("alias_script")!.Kind);
    }

    [Fact]
    public void RebuildDropsRemovedScripts()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
            Candidate("b", ScriptSourceKind.Loose, 0, "b"),
        }, @"C:\Data", cache.Root);
        Assert.True(File.Exists(ScriptCacheLayout.PexPath(cache.Root, "b")));

        var rebuilt = ScriptCacheBuilder.Build(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
        }, @"C:\Data", cache.Root);

        Assert.Equal(1, rebuilt.Manifest.ScriptCount);
        Assert.False(File.Exists(ScriptCacheLayout.PexPath(cache.Root, "b")));
        Assert.Null(ScriptCacheBuilder.Load(cache.Root)!.Find("b"));
        Assert.False(Directory.Exists(ScriptCacheLayout.ObsoleteDirectory(cache.Root)));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void BuildRecoversFromAnInterruptedSwapBeforeTheFirstRename()
    {
        using var cache = new TempCache();
        var first = Candidate("a", ScriptSourceKind.Loose, 0, "first");
        ScriptCacheBuilder.Build(new[] { first }, @"C:\Data", cache.Root);

        var incoming = ScriptCacheLayout.IncomingDirectory(cache.Root);
        Directory.CreateDirectory(Path.Combine(incoming, ScriptCacheLayout.ScriptsDirectoryName));
        File.WriteAllText(Path.Combine(incoming, ScriptCacheLayout.ManifestFileName), "{ partial");
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "swapping");
        Assert.Equal(first.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "a")));

        var second = Candidate("a", ScriptSourceKind.Loose, 0, "second");
        var rebuilt = ScriptCacheBuilder.Build(new[] { second }, @"C:\Data", cache.Root);

        Assert.Equal(1, rebuilt.Manifest.ScriptCount);
        Assert.Equal(second.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "a")));
        Assert.False(Directory.Exists(incoming));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void RecoverRestoresThePreviousCacheWhenInterruptedBetweenRenames()
    {
        using var cache = new TempCache();
        var previous = Candidate("a", ScriptSourceKind.Loose, 0, "previous");
        ScriptCacheBuilder.Build(new[] { previous }, @"C:\Data", cache.Root);

        Directory.Move(ScriptCacheLayout.CurrentDirectory(cache.Root),
            ScriptCacheLayout.ObsoleteDirectory(cache.Root));
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "swapping");

        ScriptCacheBuilder.Recover(cache.Root);

        Assert.True(Directory.Exists(ScriptCacheLayout.CurrentDirectory(cache.Root)));
        Assert.False(Directory.Exists(ScriptCacheLayout.ObsoleteDirectory(cache.Root)));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
        Assert.Equal(previous.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "a")));
        Assert.Equal(1, ScriptCacheBuilder.Load(cache.Root)!.ScriptCount);
    }

    [Fact]
    public void RecoverIsSafeWithNoCacheAndNoMarker()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Recover(cache.Root);

        Assert.Null(ScriptCacheBuilder.Load(cache.Root));
        Assert.False(Directory.Exists(ScriptCacheLayout.IncomingDirectory(cache.Root)));
    }

    [Fact]
    public void ManifestCarriesFingerprintsAndNotPayloads()
    {
        using var cache = new TempCache();
        const string payload = "payload-that-must-not-appear-in-the-manifest";
        var candidate = Candidate("a", ScriptSourceKind.Loose, 0, payload);
        var result = ScriptCacheBuilder.Build(new[] { candidate }, @"C:\Data", cache.Root);

        var json = File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root));
        var pex = candidate.Read();

        Assert.Contains(ScriptCacheBuilder.Fingerprint(pex), json);
        Assert.DoesNotContain(payload, json);
        Assert.DoesNotContain(Convert.ToBase64String(pex), json);
        Assert.Equal(64, result.Manifest.Entries[0].Sha256.Length);
        Assert.Equal(pex.Length, result.Manifest.Entries[0].ByteSize);
    }

    [Fact]
    public void ManifestRecordsTheRadiclePin()
    {
        using var cache = new TempCache();
        var result = ScriptCacheBuilder.Build(Array.Empty<ScriptSourceCandidate>(), @"C:\Data", cache.Root);

        Assert.True(result.Manifest.MatchesRadiclePin());
        Assert.Equal(RadiclePin.Repository, result.Manifest.RadicleRepository);
        Assert.Equal(RadiclePin.Commit, result.Manifest.RadicleCommit);
        Assert.Equal(RadiclePin.AbiVersion, result.Manifest.RadicleAbiVersion);
    }

    [Fact]
    public void UnavailableSourcesAreCountedNotIgnored()
    {
        using var cache = new TempCache();
        const string reason = "a stand-in unavailable source";
        var result = ScriptCacheBuilder.Build(new[] { Candidate("a", ScriptSourceKind.Loose, 0, "a") },
            @"C:\Data", cache.Root, new[] { reason });

        Assert.Contains(reason, result.Unsupported);
        Assert.Contains(reason, result.Manifest.Unsupported);
        Assert.Equal(0, result.Manifest.ArchiveCount);
    }

    [Fact]
    public void LooseSourceEnumeratesNestedScriptsWithNormalizedNames()
    {
        using var data = new TempCache();
        var scripts = Path.Combine(data.Root, LooseScriptSource.ScriptsFolderName);
        Directory.CreateDirectory(Path.Combine(scripts, "Quest"));
        File.WriteAllText(Path.Combine(scripts, "Quest", "QF_Test.PEX"), "one");
        File.WriteAllText(Path.Combine(scripts, "TopScript.pex"), "two");
        File.WriteAllText(Path.Combine(scripts, "notes.txt"), "not a script");

        using var source = LooseScriptSource.Open(new[] { (data.Root, 4) });
        var found = source.Candidates
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { "quest/qf_test", "topscript" }, found.Select(c => c.Name).ToArray());
        Assert.All(found, candidate =>
        {
            Assert.Equal(ScriptSourceKind.Loose, candidate.Kind);
            Assert.Equal(4, candidate.LoadIndex);
            Assert.Equal(3L, candidate.ByteSize);
        });
        Assert.Equal("one", Encoding.UTF8.GetString(found[0].Read()));
    }

    [Fact]
    public void LooseSourceSkipsMissingDataTrees()
    {
        using var data = new TempCache();
        using var source = LooseScriptSource.Open(new[] { (data.Root, 0) });
        Assert.Empty(source.Candidates);
    }

    [Fact]
    public void LooseSourceShadowsAcrossRootsAndCountsReplacedLikeTheDirectoryWalk()
    {
        using var data = new TempCache();
        var low = Path.Combine(data.Root, "low", LooseScriptSource.ScriptsFolderName);
        var high = Path.Combine(data.Root, "high", LooseScriptSource.ScriptsFolderName);
        Directory.CreateDirectory(Path.Combine(low, "Quest"));
        Directory.CreateDirectory(Path.Combine(high, "Quest"));
        File.WriteAllText(Path.Combine(low, "Quest", "QF_Test.pex"), "low");
        File.WriteAllText(Path.Combine(high, "Quest", "QF_Test.pex"), "high");
        File.WriteAllText(Path.Combine(low, "OnlyLow.pex"), "only-low");
        var roots = new[] { (Path.GetDirectoryName(low)!, 0), (Path.GetDirectoryName(high)!, 1) };

        var expected = roots
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(root.Item1, LooseScriptSource.ScriptsFolderName), "*.pex", SearchOption.AllDirectories)
                .Select(path => Candidate(ScriptNames.NormalizeName(Path.GetRelativePath(root.Item1, path)),
                    ScriptSourceKind.Loose, root.Item2, File.ReadAllText(path), path)))
            .ToList();
        var oldPath = ScriptSelection.ResolveCandidates(expected);

        using var source = LooseScriptSource.Open(roots);
        var newPath = ScriptSelection.ResolveCandidates(source.Candidates);

        Assert.Equal(oldPath.Select(r => (r.Name, r.Winner.LoadIndex, r.Replaced)).ToArray(),
            newPath.Select(r => (r.Name, r.Winner.LoadIndex, r.Replaced)).ToArray());
        var qf = Assert.Single(newPath, r => r.Name == "quest/qf_test");
        Assert.Equal(1, qf.Replaced);
        Assert.Equal(1, qf.Winner.LoadIndex);
        Assert.Equal("high", Encoding.UTF8.GetString(qf.Winner.Read()));
        Assert.Equal(0, Assert.Single(newPath, r => r.Name == "onlylow").Replaced);
    }

    [Fact]
    public void AWarmRebuildReusesEveryUnchangedScript()
    {
        using var cache = new TempCache();
        var a = Candidate("a", ScriptSourceKind.Loose, 0, "a");
        var b = Candidate("b", ScriptSourceKind.Archive, 1, "b");
        var cold = ScriptCacheBuilder.Build(new[] { a, b }, @"C:\Data", cache.Root);
        var warm = ScriptCacheBuilder.Build(new[] { a, b }, @"C:\Data", cache.Root);

        Assert.Equal((2, 0), (cold.ConvertedCount, cold.ReusedCount));
        Assert.Equal((0, 2), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Equal(b.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "b")));
    }

    [Fact]
    public void AMissingPexFileIsReconvertedNotReused()
    {
        using var cache = new TempCache();
        var a = Candidate("a", ScriptSourceKind.Loose, 0, "a");
        var b = Candidate("b", ScriptSourceKind.Loose, 0, "b");
        ScriptCacheBuilder.Build(new[] { a, b }, @"C:\Data", cache.Root);
        File.Delete(ScriptCacheLayout.PexPath(cache.Root, "b"));

        var warm = ScriptCacheBuilder.Build(new[] { a, b }, @"C:\Data", cache.Root);

        Assert.Equal((1, 1), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Equal(b.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "b")));
    }

    [Fact]
    public void OnlyAChangedOrNewScriptIsReconverted()
    {
        using var cache = new TempCache();
        var a = Candidate("a", ScriptSourceKind.Loose, 0, "a");
        ScriptCacheBuilder.Build(new[]
        {
            a,
            Candidate("b", ScriptSourceKind.Loose, 0, "b"),
        }, @"C:\Data", cache.Root);

        var b2 = Candidate("b", ScriptSourceKind.Loose, 0, "b2");
        var result = ScriptCacheBuilder.Build(new[]
        {
            a,
            b2,
            Candidate("c", ScriptSourceKind.Loose, 0, "c"),
        }, @"C:\Data", cache.Root);

        Assert.Equal((2, 1), (result.ConvertedCount, result.ReusedCount));
        Assert.Equal(b2.Read(), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "b")));
    }

    [Fact]
    public void AFailedConversionIsCountedAndNotCached()
    {
        using var cache = new TempCache();

        var result = ScriptCacheBuilder.Build(new[]
        {
            Candidate("good", ScriptSourceKind.Loose, 0, "good"),
            CacheFixture.Candidate("bad", Encoding.UTF8.GetBytes("bad")),
        }, @"C:\Data", cache.Root);

        Assert.Equal(1, result.Manifest.ScriptCount);
        Assert.Contains(result.Unsupported, line => line.StartsWith("bad: ", StringComparison.Ordinal));
        Assert.False(File.Exists(ScriptCacheLayout.PexPath(cache.Root, "bad")));
    }

    [Fact]
    public void TheCacheHoldsEachWinnersPexPayloadAndNoIr()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[] { Candidate("quest/qf_a", ScriptSourceKind.Loose, 0, "a") }, @"C:\Data",
            cache.Root);

        var files = Directory.EnumerateFiles(cache.Root, "*", SearchOption.AllDirectories).ToList();
        Assert.Empty(files.Where(path => path.EndsWith(".gir", StringComparison.Ordinal)));
        Assert.Contains(files, path => path.EndsWith(ScriptCacheLayout.PexExtension, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("scripts/quest/qf_a.pex", ScriptCacheBuilder.Load(cache.Root)!.Find("quest/qf_a")!.PexFile);
    }

    [Fact]
    public void PexPayloadRoundTripsThroughBuildAndLoad()
    {
        using var cache = new TempCache();
        var candidate = Assert.Single(CacheFixture.Candidates(TestHosts.Stub("Probe")));
        var payload = candidate.Read();

        var built = CacheFixture.Build(cache.Root, new[] { candidate });

        var entry = Assert.Single(built.Manifest.Entries);
        Assert.Equal(ScriptCacheBuilder.Fingerprint(payload), entry.PexSha256);
        Assert.Equal(payload, File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "Probe")));
        using var session = GameSession.Load(cache.Root);
        Assert.Equal(payload, session.PexScripts["Probe"]);
        Assert.True(session.Host.HasScript("Probe"));
    }
}
