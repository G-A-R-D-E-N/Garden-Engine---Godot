using OpenCommonwealth.Services;
using OpenCommonwealth.Services.GameDataConversion;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class FreshnessRulesTests
{
    private static ScriptCacheManifest Manifest(IReadOnlyList<string>? stats) =>
        new(ScriptCacheManifest.CurrentFormatVersion, RadiclePin.Repository, RadiclePin.Commit, RadiclePin.AbiVersion, "data", 0, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<ScriptCacheEntry>(), LoadOrderPluginStats: stats);

    [Fact]
    public void TheThreeRulesKeepTheirOwnMissingValueDirections()
    {
        Assert.True(PluginStamp.Mismatch("abc", null));
        Assert.True(PluginStamp.Mismatch("abc", ""));
        Assert.False(PluginStamp.Mismatch(null, "abc"));
        Assert.False(PluginStamp.Mismatch("", "abc"));
        Assert.False(PluginStamp.Mismatch("ABC", "abc"));
        Assert.True(PluginStamp.Mismatch("abd", "abc"));

        var done = new StageProgressData(10, 10, 0, false, Fingerprint: "fp");
        Assert.True(StageProgress.IsComplete(done, "fp"));
        Assert.False(StageProgress.IsComplete(done, null));
        Assert.False(StageProgress.IsComplete(done, ""));
        Assert.False(StageProgress.IsComplete(done with { Fingerprint = "" }, "fp"));
        Assert.False(StageProgress.IsComplete(done, "FP"));
        Assert.False(StageProgress.IsComplete(done with { Running = true }, "fp"));
        Assert.False(StageProgress.IsComplete(done with { Failed = 1 }, "fp"));
        Assert.False(StageProgress.IsComplete(done with { Scope = "one" }, "fp"));
        Assert.False(StageProgress.IsComplete(done with { Done = 9 }, "fp"));
        Assert.False(StageProgress.IsComplete(null, "fp"));

        Assert.False(Manifest(null).MatchesPluginStats(Array.Empty<string>()));
        Assert.True(Manifest(Array.Empty<string>()).MatchesPluginStats(Array.Empty<string>()));
    }

    [Fact]
    public void TheScriptManifestStatLineIsThePluginStampLine()
    {
        var dir = Directory.CreateTempSubdirectory("freshness-rules-");
        try
        {
            var present = Path.Combine(dir.FullName, "Mod.ESP");
            File.WriteAllBytes(present, new byte[7]);
            var missing = Path.Combine(dir.FullName, "Gone.esp");
            foreach (var path in new[] { present, missing })
                Assert.Equal(PluginStamp.Line(PluginStamp.Stat(Path.GetFileName(path), path)),
                    Assert.Single(ScriptCacheBuilder.PluginStatLines(new[] { path })));
            Assert.Equal("gone.esp|-1|0", ScriptCacheBuilder.PluginStatLines(new[] { missing })[0]);
        }
        finally { dir.Delete(true); }
    }
}
