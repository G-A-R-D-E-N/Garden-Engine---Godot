using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheIntegrityTests
{
    private static readonly ScriptCacheConverter Converter = new(1, 1, bytes => Encoding.UTF8.GetString(bytes));

    private static byte[] Ir(string function) => Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
    {
        new PapyrusScript("Probe", new[] { new PapyrusFunction(function, new[] { PapyrusInstruction.Return() }) },
            Array.Empty<string>()),
    }));

    private static ScriptSourceCandidate Candidate(byte[] payload) =>
        new("probe", ScriptSourceKind.Loose, "project-authored", 0, payload.Length, () => payload);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ChangedValidIrIsRefusedOnLoadAndWarmPublication(bool rebuild, bool empty)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Ir("Original"));
        ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
        var damaged = empty ? Encoding.UTF8.GetBytes(PapyrusIr.ToJson(Array.Empty<PapyrusScript>())) : Ir("Changed");
        File.WriteAllBytes(ScriptCacheLayout.ScriptPath(cache.Root, "probe"), damaged);
        var before = File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root));

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
            else GameSession.Load(cache.Root);
        });
        Assert.Contains("IR", error.Message);
        Assert.Contains("probe", error.Message);
        Assert.Equal(before, File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root)));
        Assert.Equal(damaged, File.ReadAllBytes(ScriptCacheLayout.ScriptPath(cache.Root, "probe")));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TamperedPexIsRefusedOnLoadAndWarmRebuild(bool rebuild)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Ir("Original"));
        ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
        var damaged = Encoding.UTF8.GetBytes("tampered payload");
        File.WriteAllBytes(ScriptCacheLayout.PexPath(cache.Root, "probe"), damaged);
        var before = File.ReadAllText(ScriptCacheLayout.ManifestPath(cache.Root));

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
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
    public void MissingOrWrongFingerprintsAreRefused(bool rebuild, string fingerprint)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Ir("Original"));
        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with
        {
            Entries = new[] { built.Manifest.Entries.Single() with { IrSha256 = fingerprint } },
        }).ToJson());
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
            else GameSession.Load(cache.Root);
        });
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "wrong")]
    [InlineData(true, "")]
    [InlineData(true, "wrong")]
    public void MissingOrWrongPexFingerprintIsRefused(bool rebuild, string fingerprint)
    {
        using var cache = new TempCache();
        var candidate = Candidate(Ir("Original"));
        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with
        {
            Entries = new[] { built.Manifest.Entries.Single() with { PexSha256 = fingerprint } },
        }).ToJson());
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            if (rebuild) ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
            else GameSession.Load(cache.Root);
        });
        Assert.Contains("PEX", error.Message);
    }

    [Fact]
    public void TamperedPexFileValueIsRefusedOnLoad()
    {
        using var cache = new TempCache();
        var candidate = Candidate(Ir("Original"));
        var built = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
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
        var built = ScriptCacheBuilder.Build(new[] { Candidate(Ir("Original")) }, "project-authored", Converter,
            cache.Root);
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with { FormatVersion = 4 }).ToJson());
        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
        Assert.Contains("format", error.Message);
    }

    [Fact]
    public void HealthyIrStillLoadsAndReusesWhileVersionThreeRequiresReconversion()
    {
        using var cache = new TempCache();
        var payload = Encoding.UTF8.GetBytes("project-authored source");
        var converter = new ScriptCacheConverter(1, 1, _ => Encoding.UTF8.GetString(Ir("Original")));
        var candidate = Candidate(payload);
        var cold = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", converter, cache.Root);
        var entry = Assert.Single(cold.Manifest.Entries);
        Assert.NotEqual(entry.Sha256, entry.IrSha256);
        Assert.Equal(ScriptCacheBuilder.Fingerprint(Ir("Original")), entry.IrSha256);
        Assert.True(GameSession.Load(cache.Root).Host.HasScript("Probe"));
        var warm = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", converter, cache.Root);
        Assert.Equal((0, 1), (warm.ConvertedCount, warm.ReusedCount));
        Assert.Equal(entry, Assert.Single(warm.Manifest.Entries));

        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (cold.Manifest with { FormatVersion = 3 }).ToJson());
        Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
        var migrated = ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", converter, cache.Root);
        Assert.Equal((1, 0), (migrated.ConvertedCount, migrated.ReusedCount));
        Assert.Equal(5, migrated.Manifest.FormatVersion);
        Assert.True(GameSession.Load(cache.Root).Host.HasScript("Probe"));
    }
}
