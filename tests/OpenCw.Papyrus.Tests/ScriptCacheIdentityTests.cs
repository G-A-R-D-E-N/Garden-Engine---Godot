using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheIdentityTests
{
    [Theory]
    [InlineData(false, "Shared")]
    [InlineData(false, "sHaReD")]
    [InlineData(true, "Shared")]
    [InlineData(true, "sHaReD")]
    public void AnAmbiguousLibraryCannotReplaceTheHealthySnapshot(bool reuse, string otherClass)
    {
        using var cache = new TempCache();
        var first = Candidate("first", "Shared");
        ScriptCacheBuilder.Build(new[] { reuse ? first : Candidate("healthy", "Healthy") }, "project-authored", cache.Root);
        var before = Snapshot(cache.Root);
        var error = Assert.Throws<InvalidOperationException>(() => ScriptCacheBuilder.Build(
            new[] { first, Candidate("second", otherClass) }, "project-authored", cache.Root));
        Assert.Contains("already in the library", error.Message);
        Assert.Contains("first", error.Message);
        Assert.Contains("second", error.Message);
        Assert.Equal(before, Snapshot(cache.Root));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
        using var session = GameSession.Load(cache.Root);
        Assert.True(session.Host.HasScript(reuse ? "Shared" : "Healthy"));
    }

    [Fact]
    public void MalformedReusedPexCannotBeRepublished()
    {
        using var cache = new TempCache();
        var candidate = Candidate("first", "Shared");
        ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root);
        File.WriteAllText(ScriptCacheLayout.PexPath(cache.Root, "first"), "malformed PEX");
        var before = Snapshot(cache.Root);
        Assert.ThrowsAny<Exception>(() => ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", cache.Root));
        Assert.Equal(before, Snapshot(cache.Root));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void ACacheBuiltForOneProfileLoadOrderIsRefusedForAnother()
    {
        using var cache = new TempCache();
        var profileA = Path.Combine(cache.Root, "profile-a");
        var profileB = Path.Combine(cache.Root, "profile-b");
        var first = Candidate("first", "Shared");
        var built = ScriptCacheBuilder.Build(new[] { first }, "profile-a", cache.Root,
            loadOrderRoots: new[] { profileA });

        Assert.Equal(new[] { profileA }, built.Manifest.LoadOrderRoots);
        Assert.Equal(64, built.Manifest.SourceSetSha256.Length);
        using (var session = GameSession.Load(cache.Root, new[] { profileA })) Assert.True(session.Host.HasScript("Shared"));
        var warm = ScriptCacheBuilder.Build(new[] { first }, "profile-a", cache.Root, loadOrderRoots: new[] { profileA });
        Assert.Equal(built.Manifest.SourceSetSha256, warm.Manifest.SourceSetSha256);
        var changed = ScriptCacheBuilder.Build(new[] { Candidate("first", "Changed") }, "profile-a", cache.Root,
            loadOrderRoots: new[] { profileA });
        Assert.NotEqual(built.Manifest.SourceSetSha256, changed.Manifest.SourceSetSha256);

        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root, new[] { profileB }));

        Assert.Contains(profileA, error.Message, StringComparison.Ordinal);
        Assert.Contains(profileB, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACacheBuiltForOnePluginOrderIsRefusedForAnother()
    {
        using var cache = new TempCache();
        var profile = Path.Combine(cache.Root, "profile");
        var order = new[] { Path.Combine(profile, "Fallout4.esm"), Path.Combine(profile, "Mod.esp") };
        var built = ScriptCacheBuilder.Build(new[] { Candidate("first", "Shared") }, "profile", cache.Root,
            loadOrderRoots: new[] { profile }, loadOrderPlugins: order);

        Assert.Equal(new[] { "Fallout4.esm", "Mod.esp" }, built.Manifest.LoadOrderPlugins);
        using (var session = GameSession.Load(cache.Root, new[] { profile }, new[] { "fallout4.esm", "mod.esp" }))
            Assert.True(session.Host.HasScript("Shared"));

        var reordered = Assert.Throws<InvalidOperationException>(() =>
            GameSession.Load(cache.Root, new[] { profile }, new[] { "Mod.esp", "Fallout4.esm" }));
        Assert.Contains("plugin order", reordered.Message, StringComparison.Ordinal);
        var added = Assert.Throws<InvalidOperationException>(() =>
            GameSession.Load(cache.Root, new[] { profile }, new[] { "Fallout4.esm", "Mod.esp", "Other.esp" }));
        Assert.Contains("Other.esp", added.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACacheIsRefusedWhenAPluginFileChangedUnderTheSameName()
    {
        using var cache = new TempCache();
        var profile = Path.Combine(cache.Root, "profile");
        Directory.CreateDirectory(profile);
        var order = new[] { Path.Combine(profile, "Fallout4.esm"), Path.Combine(profile, "Mod.esp") };
        foreach (var plugin in order) File.WriteAllBytes(plugin, new byte[16]);
        var built = ScriptCacheBuilder.Build(new[] { Candidate("first", "Shared") }, "profile", cache.Root,
            loadOrderRoots: new[] { profile }, loadOrderPlugins: order);

        Assert.Equal(2, built.Manifest.LoadOrderPluginStats!.Count);
        Assert.StartsWith("fallout4.esm|16|", built.Manifest.LoadOrderPluginStats[0], StringComparison.Ordinal);
        Assert.True(built.Manifest.MatchesPluginStats(order));
        using (var session = GameSession.Load(cache.Root, new[] { profile }, order)) Assert.True(session.Host.HasScript("Shared"));

        File.WriteAllBytes(order[1], new byte[17]);

        Assert.False(built.Manifest.MatchesPluginStats(order));
        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root, new[] { profile }, order));
        Assert.Contains("plugin files", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACacheIsRefusedWhenAPluginChangesTimeAtTheSameSizeOrDisappears(bool deleted)
    {
        using var cache = new TempCache();
        var profile = Path.Combine(cache.Root, "profile");
        Directory.CreateDirectory(profile);
        var order = new[] { Path.Combine(profile, "Fallout4.esm"), Path.Combine(profile, "Mod.esp") };
        foreach (var plugin in order) File.WriteAllBytes(plugin, new byte[16]);
        ScriptCacheBuilder.Build(new[] { Candidate("first", "Shared") }, "profile", cache.Root,
            loadOrderRoots: new[] { profile }, loadOrderPlugins: order);
        using (var session = GameSession.Load(cache.Root, new[] { profile }, order)) Assert.True(session.Host.HasScript("Shared"));

        if (deleted) File.Delete(order[1]);
        else File.SetLastWriteTimeUtc(order[1], File.GetLastWriteTimeUtc(order[1]).AddSeconds(30));

        var error = Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root, new[] { profile }, order));
        Assert.Contains("plugin files", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABuildWithoutPluginsRecordsNoStats()
    {
        using var cache = new TempCache();
        var built = ScriptCacheBuilder.Build(new[] { Candidate("first", "Shared") }, "profile", cache.Root);
        Assert.Empty(built.Manifest.LoadOrderPluginStats!);
        Assert.Equal(9, ScriptCacheManifest.CurrentFormatVersion);
    }

    private static ScriptSourceCandidate Candidate(string name, string className) =>
        CacheFixture.Candidate(name, CacheFixture.Candidates(TestHosts.Stub(className)).Single().Read());

    private static KeyValuePair<string, string>[] Snapshot(string root)
    {
        var current = ScriptCacheLayout.CurrentDirectory(root);
        return Directory.EnumerateFiles(current, "*", SearchOption.AllDirectories)
            .Select(path => new KeyValuePair<string, string>(Path.GetRelativePath(current, path),
                ScriptCacheBuilder.Fingerprint(File.ReadAllBytes(path))))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
    }
}
