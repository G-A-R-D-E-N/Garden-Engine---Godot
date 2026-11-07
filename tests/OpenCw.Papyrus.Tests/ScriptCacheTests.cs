using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static ScriptSourceCandidate Candidate(
        string name,
        ScriptSourceKind kind,
        int loadIndex,
        string payload,
        string? container = null) =>
        new(name, kind, container ?? $"{kind}-{loadIndex}-{name}", loadIndex,
            Encoding.UTF8.GetByteCount(payload), () => Encoding.UTF8.GetBytes(payload));

    private static readonly ScriptCacheConverter Text = new(1, 1, payload => Ir(Encoding.UTF8.GetString(payload)));

    private static string Ir(string name) => PapyrusIr.ToJson(new[]
        { new PapyrusScript(name, Array.Empty<PapyrusFunction>(), Array.Empty<string>()) });

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
        Assert.Equal("quests/qf_test", ScriptCachePrecedence.NormalizeName("Quests\\QF_Test.PEX"));
        Assert.Equal("alias_script", ScriptCachePrecedence.NormalizeName("alias_script"));
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
            var winner = ScriptCachePrecedence.PickWinner(permutation);
            Assert.NotNull(winner);
            Assert.Equal(ScriptSourceKind.Loose, winner!.Kind);
            Assert.Equal(1, winner.LoadIndex);
        }
    }

    [Fact]
    public void LooseBeatsArchiveEvenAtALowerLoadIndex()
    {
        var winner = ScriptCachePrecedence.PickWinner(new[]
        {
            Candidate("a", ScriptSourceKind.Archive, 50, "archive"),
            Candidate("a", ScriptSourceKind.Loose, 0, "loose"),
        });

        Assert.Equal(ScriptSourceKind.Loose, winner!.Kind);
        Assert.Equal("loose", Encoding.UTF8.GetString(winner.Read()));
    }

    [Fact]
    public void HigherLoadIndexWinsAmongLooseScripts()
    {
        var winner = ScriptCachePrecedence.PickWinner(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 2, "two"),
            Candidate("a", ScriptSourceKind.Loose, 7, "seven"),
        });

        Assert.Equal(7, winner!.LoadIndex);
        Assert.Equal("seven", Encoding.UTF8.GetString(winner.Read()));
    }

    [Fact]
    public void ResolveCountsReplacedCandidatesAndOrdersByName()
    {
        var resolved = ScriptCachePrecedence.Resolve(new[]
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
        var result = ScriptCacheBuilder.Build(new[]
        {
            Candidate("quests/qf_one", ScriptSourceKind.Loose, 1, "loose-payload"),
            Candidate("quests/qf_one", ScriptSourceKind.Archive, 4, "archive-payload"),
            Candidate("alias_script", ScriptSourceKind.Archive, 4, "archive-only"),
        }, @"C:\Data", Text, cache.Root);

        Assert.Equal(2, result.Manifest.ScriptCount);
        Assert.Equal(1, result.Manifest.LooseCount);
        Assert.Equal(1, result.Manifest.ArchiveCount);
        Assert.Equal(1, result.ReplacedCount);

        var loose = ScriptCacheLayout.ScriptPath(cache.Root, "quests/qf_one");
        Assert.True(File.Exists(loose));
        Assert.Equal(Ir("loose-payload"), File.ReadAllText(loose));

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
        }, @"C:\Data", Text, cache.Root);
        Assert.True(File.Exists(ScriptCacheLayout.ScriptPath(cache.Root, "b")));

        var rebuilt = ScriptCacheBuilder.Build(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
        }, @"C:\Data", Text, cache.Root);

        Assert.Equal(1, rebuilt.Manifest.ScriptCount);
        Assert.False(File.Exists(ScriptCacheLayout.ScriptPath(cache.Root, "b")));
        Assert.Null(ScriptCacheBuilder.Load(cache.Root)!.Find("b"));
        Assert.False(Directory.Exists(ScriptCacheLayout.ObsoleteDirectory(cache.Root)));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void BuildRecoversFromAnInterruptedSwapBeforeTheFirstRename()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[] { Candidate("a", ScriptSourceKind.Loose, 0, "first") }, @"C:\Data", Text,
            cache.Root);

        var incoming = ScriptCacheLayout.IncomingDirectory(cache.Root);
        Directory.CreateDirectory(Path.Combine(incoming, ScriptCacheLayout.ScriptsDirectoryName));
        File.WriteAllText(Path.Combine(incoming, ScriptCacheLayout.ManifestFileName), "{ partial");
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "swapping");
        Assert.Equal(Ir("first"), File.ReadAllText(ScriptCacheLayout.ScriptPath(cache.Root, "a")));

        var rebuilt = ScriptCacheBuilder.Build(new[] { Candidate("a", ScriptSourceKind.Loose, 0, "second") },
            @"C:\Data", Text, cache.Root);

        Assert.Equal(1, rebuilt.Manifest.ScriptCount);
        Assert.Equal(Ir("second"), File.ReadAllText(ScriptCacheLayout.ScriptPath(cache.Root, "a")));
        Assert.False(Directory.Exists(incoming));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void RecoverRestoresThePreviousCacheWhenInterruptedBetweenRenames()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[] { Candidate("a", ScriptSourceKind.Loose, 0, "previous") }, @"C:\Data", Text,
            cache.Root);

        Directory.Move(ScriptCacheLayout.CurrentDirectory(cache.Root),
            ScriptCacheLayout.ObsoleteDirectory(cache.Root));
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "swapping");

        ScriptCacheBuilder.Recover(cache.Root);

        Assert.True(Directory.Exists(ScriptCacheLayout.CurrentDirectory(cache.Root)));
        Assert.False(Directory.Exists(ScriptCacheLayout.ObsoleteDirectory(cache.Root)));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
        Assert.Equal(Ir("previous"), File.ReadAllText(ScriptCacheLayout.ScriptPath(cache.Root, "a")));
        Assert.NotNull(ScriptCacheBuilder.Load(cache.Root));
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
        var payload = "payload-that-must-not-appear-in-the-manifest";
        var result = ScriptCacheBuilder.Build(new[] { Candidate("a", ScriptSourceKind.Loose, 0, payload) },
            @"C:\Data", Text, cache.Root);

        var json = File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root));
        var expected = ScriptCacheBuilder.Fingerprint(Encoding.UTF8.GetBytes(payload));

        Assert.Contains(expected, json);
        Assert.DoesNotContain(payload, json);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), json);
        Assert.Equal(64, result.Manifest.Entries[0].Sha256.Length);
        Assert.Equal(Encoding.UTF8.GetByteCount(payload), result.Manifest.Entries[0].ByteSize);
    }

    [Fact]
    public void ManifestRecordsTheRadiclePin()
    {
        using var cache = new TempCache();
        var result = ScriptCacheBuilder.Build(Array.Empty<ScriptSourceCandidate>(), @"C:\Data", Text, cache.Root);

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
            @"C:\Data", Text, cache.Root, new[] { reason });

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

        var found = LooseScriptSource.Enumerate(new[] { (data.Root, 4) })
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { "quest/qf_test", "topscript" }, found.Select(c => c.Name).ToArray());
        Assert.All(found, candidate =>
        {
            Assert.Equal(ScriptSourceKind.Loose, candidate.Kind);
            Assert.Equal(4, candidate.LoadIndex);
            Assert.True(candidate.ByteSize > 0);
        });
        Assert.Equal("one", Encoding.UTF8.GetString(found[0].Read()));
    }

    [Fact]
    public void LooseSourceSkipsMissingDataTrees()
    {
        using var data = new TempCache();
        Assert.Empty(LooseScriptSource.Enumerate(new[] { (data.Root, 0) }));
    }

    private sealed class CountingConverter
    {
        public CountingConverter(int irFormat = 1, int lowering = 1) =>
            Converter = new ScriptCacheConverter(irFormat, lowering, payload =>
            {
                Converted.Add(Encoding.UTF8.GetString(payload));
                return Ir(Encoding.UTF8.GetString(payload));
            });

        public List<string> Converted { get; } = new();

        public ScriptCacheConverter Converter { get; }
    }

    [Fact]
    public void AWarmRebuildReusesEveryUnchangedScript()
    {
        using var cache = new TempCache();
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
            Candidate("b", ScriptSourceKind.Archive, 1, "b"),
        };
        var first = new CountingConverter();
        var cold = ScriptCacheBuilder.Build(candidates, @"C:\Data", first.Converter, cache.Root);
        var second = new CountingConverter();
        var warm = ScriptCacheBuilder.Build(candidates, @"C:\Data", second.Converter, cache.Root);

        Assert.Equal((2, 0), (cold.ConvertedCount, cold.ReusedCount));
        Assert.Equal((0, 2), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Empty(second.Converted);
        Assert.Equal(Ir("b"), File.ReadAllText(ScriptCacheLayout.ScriptPath(cache.Root, "b")));
        Assert.Equal(Encoding.UTF8.GetBytes("b"), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "b")));
    }

    [Fact]
    public void AMissingPexFileIsReconvertedNotReused()
    {
        using var cache = new TempCache();
        var candidates = new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
            Candidate("b", ScriptSourceKind.Loose, 0, "b"),
        };
        ScriptCacheBuilder.Build(candidates, @"C:\Data", new CountingConverter().Converter, cache.Root);
        File.Delete(ScriptCacheLayout.PexPath(cache.Root, "b"));

        var converter = new CountingConverter();
        var warm = ScriptCacheBuilder.Build(candidates, @"C:\Data", converter.Converter, cache.Root);

        Assert.Equal(new[] { "b" }, converter.Converted.ToArray());
        Assert.Equal((1, 1), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Equal(Encoding.UTF8.GetBytes("b"), File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "b")));
    }

    [Fact]
    public void OnlyAChangedOrNewScriptIsReconverted()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
            Candidate("b", ScriptSourceKind.Loose, 0, "b"),
        }, @"C:\Data", new CountingConverter().Converter, cache.Root);

        var converter = new CountingConverter();
        var result = ScriptCacheBuilder.Build(new[]
        {
            Candidate("a", ScriptSourceKind.Loose, 0, "a"),
            Candidate("b", ScriptSourceKind.Loose, 0, "b2"),
            Candidate("c", ScriptSourceKind.Loose, 0, "c"),
        }, @"C:\Data", converter.Converter, cache.Root);

        Assert.Equal(new[] { "b2", "c" }, converter.Converted.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal((2, 1), (result.ConvertedCount, result.ReusedCount));
        Assert.Equal(Ir("b2"), File.ReadAllText(ScriptCacheLayout.ScriptPath(cache.Root, "b")));
    }

    [Fact]
    public void AnIrOrLoweringVersionChangeReconvertsEverything()
    {
        using var cache = new TempCache();
        var candidates = new[] { Candidate("a", ScriptSourceKind.Loose, 0, "a") };
        ScriptCacheBuilder.Build(candidates, @"C:\Data", new CountingConverter().Converter, cache.Root);

        var newIr = new CountingConverter(irFormat: 2);
        Assert.Equal(1, ScriptCacheBuilder.Build(candidates, @"C:\Data", newIr.Converter, cache.Root).ConvertedCount);
        var newLowering = new CountingConverter(irFormat: 2, lowering: 2);
        Assert.Equal(1, ScriptCacheBuilder.Build(candidates, @"C:\Data", newLowering.Converter, cache.Root).ConvertedCount);
        var same = new CountingConverter(irFormat: 2, lowering: 2);
        Assert.Equal(1, ScriptCacheBuilder.Build(candidates, @"C:\Data", same.Converter, cache.Root).ReusedCount);

        var manifest = ScriptCacheBuilder.Load(cache.Root)!;
        Assert.Equal((2, 2), (manifest.IrFormatVersion, manifest.LoweringVersion));
    }

    [Fact]
    public void AFailedConversionIsCountedAndNotCached()
    {
        using var cache = new TempCache();
        var failing = new ScriptCacheConverter(1, 1, payload =>
            Encoding.UTF8.GetString(payload) == "bad" ? throw new FormatException("not lowerable") : Ir("ok"));

        var result = ScriptCacheBuilder.Build(new[]
        {
            Candidate("good", ScriptSourceKind.Loose, 0, "good"),
            Candidate("bad", ScriptSourceKind.Loose, 0, "bad"),
        }, @"C:\Data", failing, cache.Root);

        Assert.Equal(1, result.Manifest.ScriptCount);
        Assert.Contains(result.Unsupported, line => line.StartsWith("bad: not lowerable", StringComparison.Ordinal));
        Assert.False(File.Exists(ScriptCacheLayout.ScriptPath(cache.Root, "bad")));
    }

    [Fact]
    public void TheCacheHoldsEachWinnersIrAndItsPexPayload()
    {
        using var cache = new TempCache();
        ScriptCacheBuilder.Build(new[] { Candidate("quest/qf_a", ScriptSourceKind.Loose, 0, "a") }, @"C:\Data",
            new CountingConverter().Converter, cache.Root);

        var files = Directory.EnumerateFiles(cache.Root, "*", SearchOption.AllDirectories).ToList();
        Assert.Contains(files, path => path.EndsWith(ScriptCacheLayout.IrExtension, StringComparison.Ordinal));
        Assert.Contains(files, path => path.EndsWith(ScriptCacheLayout.PexExtension, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("scripts/quest/qf_a.pex", ScriptCacheBuilder.Load(cache.Root)!.Find("quest/qf_a")!.PexFile);
    }

    [Fact]
    public void PexPayloadRoundTripsThroughBuildAndLoad()
    {
        using var cache = new TempCache();
        var payload = new byte[] { 0xDE, 0xC0, 0x57, 0xFA, 0x00, 0xFF, 0x80 };
        var candidate = new ScriptSourceCandidate("probe", ScriptSourceKind.Loose, "project-authored", 0,
            payload.Length, () => payload);

        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored",
            new ScriptCacheConverter(1, 1, _ => Ir("Probe")), cache.Root);

        var entry = Assert.Single(built.Manifest.Entries);
        Assert.Equal(ScriptCacheBuilder.Fingerprint(payload), entry.PexSha256);
        Assert.Equal(payload, File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "probe")));
        Assert.Equal(payload, GameSession.Load(cache.Root).PexScripts["probe"]);
    }
}
