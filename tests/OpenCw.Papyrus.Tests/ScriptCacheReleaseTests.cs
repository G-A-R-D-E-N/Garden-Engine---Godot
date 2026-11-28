using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

/// <summary>
/// The release boundary: extracted script payloads are machine-local cache content, so neither the
/// repository nor a build produced from it may contain them.
/// </summary>
public class ScriptCacheReleaseTests
{
    [Fact]
    public void DefaultCacheRootIsOutsideTheRepository()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);

        var normalizedRoot = Path.GetFullPath(ScriptCacheLayout.DefaultRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedRepository = Path.GetFullPath(root!)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        Assert.False(normalizedRoot.StartsWith(normalizedRepository, StringComparison.OrdinalIgnoreCase),
            $"the cache root {normalizedRoot} must not live inside {normalizedRepository}");
        Assert.StartsWith(Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            ScriptCacheLayout.DefaultRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryHasNoExtractedScriptPayloads()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);

        var opencw = Path.Combine(root!, "opencw");
        var payloads = Directory
            .EnumerateFiles(opencw, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".pex", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".psc", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(payloads);

        var cacheDirectories = Directory
            .EnumerateDirectories(root!, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals("script-cache", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(cacheDirectories);
    }

    [Fact]
    public void CacheDirectoryNamesAreNotUnderTheGameProject()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);

        var opencw = Path.GetFullPath(Path.Combine(root!, "opencw"));
        foreach (var name in new[]
                 {
                     ScriptCacheLayout.CurrentDirectoryName,
                     ScriptCacheLayout.IncomingDirectoryName,
                     ScriptCacheLayout.ObsoleteDirectoryName,
                 })
        {
            Assert.False(Directory.Exists(Path.Combine(opencw, name)));
        }
    }
}
