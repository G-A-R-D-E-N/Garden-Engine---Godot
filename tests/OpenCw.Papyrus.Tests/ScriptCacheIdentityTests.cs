using System.Text;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheIdentityTests
{
    private static readonly ScriptCacheConverter Converter = new(1, 1, bytes => Encoding.UTF8.GetString(bytes));

    [Theory]
    [InlineData(false, "Shared")]
    [InlineData(false, "sHaReD")]
    [InlineData(true, "Shared")]
    [InlineData(true, "sHaReD")]
    public void AnAmbiguousLibraryCannotReplaceTheHealthySnapshot(bool reuse, string otherClass)
    {
        using var cache = new TempCache();
        var first = Candidate("first", "Shared");
        ScriptCacheBuilder.Build(new[] { reuse ? first : Candidate("healthy", "Healthy") }, "project-authored", Converter, cache.Root);
        var before = Snapshot(cache.Root);
        var error = Assert.Throws<InvalidOperationException>(() => ScriptCacheBuilder.Build(
            new[] { first, Candidate("second", otherClass) }, "project-authored", Converter, cache.Root));
        Assert.Contains("already in the library", error.Message);
        Assert.Contains("first", error.Message);
        Assert.Contains("second", error.Message);
        Assert.Equal(before, Snapshot(cache.Root));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
        Assert.True(GameSession.Load(cache.Root).Host.HasScript(reuse ? "Shared" : "Healthy"));
    }

    [Fact]
    public void MalformedReusedIrCannotBeRepublished()
    {
        using var cache = new TempCache();
        var candidate = Candidate("first", "Shared");
        ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root);
        File.WriteAllText(ScriptCacheLayout.ScriptPath(cache.Root, "first"), "malformed IR");
        var before = Snapshot(cache.Root);
        Assert.ThrowsAny<Exception>(() => ScriptCacheBuilder.Build(new[] { candidate }, "project-authored", Converter, cache.Root));
        Assert.Equal(before, Snapshot(cache.Root));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    private static ScriptSourceCandidate Candidate(string name, string className)
    {
        var bytes = Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
            { new PapyrusScript(className, Array.Empty<PapyrusFunction>(), Array.Empty<string>()) }));
        return new(name, ScriptSourceKind.Loose, "project-authored", 0, bytes.Length, () => bytes);
    }

    private static KeyValuePair<string, string>[] Snapshot(string root)
    {
        var current = ScriptCacheLayout.CurrentDirectory(root);
        return Directory.EnumerateFiles(current, "*", SearchOption.AllDirectories)
            .Select(path => new KeyValuePair<string, string>(Path.GetRelativePath(current, path),
                ScriptCacheBuilder.Fingerprint(File.ReadAllBytes(path))))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
    }
}
