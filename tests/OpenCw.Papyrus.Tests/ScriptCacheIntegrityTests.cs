using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheIntegrityTests
{
    private static byte[] Pex(string function) =>
        ConformanceScripts.Compile(new[] { ("Probe", TestHosts.Stub("Probe", null, $"Function {function}()\nEndFunction").Source) })["Probe"];

    private static ScriptSourceCandidate Candidate(byte[] payload) => CacheFixture.Candidate("probe", payload);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TamperedPexIsRefusedOnLoadAndWarmRebuild(bool rebuild)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Pex("Original"));
        ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        var damaged = Encoding.UTF8.GetBytes("tampered payload");
        File.WriteAllBytes(ScriptCacheLayout.PexPath(cache.Root, "probe"), damaged);
        var before = File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root));

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
            else GameSession.Load(cache.Root);
        });
        Assert.Contains("PEX", error.Message);
        Assert.Contains("probe", error.Message);
        Assert.Equal(before, File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root)));
        Assert.Equal(damaged, File.ReadAllBytes(ScriptCacheLayout.PexPath(cache.Root, "probe")));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "wrong")]
    [InlineData(true, "")]
    [InlineData(true, "wrong")]
    public void MissingOrWrongPexFingerprintIsRefused(bool rebuild, string fingerprint)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Pex("Original"));
        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with
        {
            Entries = new[] { built.Manifest.Entries.Single() with { PexSha256 = fingerprint } },
        }).ToJson());
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
            else GameSession.Load(cache.Root);
        });
        Assert.Contains("PEX", error.Message);
    }

    [Fact]
    public void TamperedPexFileValueIsRefusedOnLoad()
    {
        using var cache = new TempCache();
        var candidate = Candidate(Pex("Original"));
        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with
        {
            Entries = new[] { built.Manifest.Entries.Single() with { PexFile = "scripts/elsewhere.pex" } },
        }).ToJson());
        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
        Assert.Contains("PEX", error.Message);
    }

    [Fact]
    public void FormatFourManifestIsRejectedOnLoad()
    {
        using var cache = new TempCache();
        var built = ScriptCacheBuilder.Build(new[] { Candidate(Pex("Original")) }, "project-authored", cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with { FormatVersion = 4 }).ToJson());
        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
        Assert.Contains("format", error.Message);
    }

    [Fact]
    public void HealthyCacheStillLoadsAndReusesWhileFormatFiveRequiresReconversion()
    {
        using var cache = new TempCache();
        var candidate = Candidate(Pex("Original"));
        var cold = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        var entry = Assert.Single(cold.Manifest.Entries);
        using (var loaded = GameSession.Load(cache.Root)) Assert.True(loaded.Host.HasScript("Probe"));
        var warm = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        Assert.Equal((0, 1), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Equal(entry, Assert.Single(warm.Manifest.Entries));

        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (cold.Manifest with { FormatVersion = 5 }).ToJson());
        Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
        var migrated = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        Assert.Equal((1, 0), (migrated.ConvertedCount, migrated.ReusedCount));
        Assert.Equal(ScriptCacheManifest.CurrentFormatVersion, migrated.Manifest.FormatVersion);
        using (var loaded = GameSession.Load(cache.Root)) Assert.True(loaded.Host.HasScript("Probe"));
    }
}
