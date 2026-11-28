using System;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptCacheManifestPluginOrderTests
{
    [Fact]
    public void PluginOrderMatchesFullPathsByFileName()
    {
        var manifest = new ScriptCacheManifest(ScriptCacheManifest.CurrentFormatVersion,
            RadiclePin.Repository, RadiclePin.Commit, RadiclePin.AbiVersion, "data", 0, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<ScriptCacheEntry>(),
            LoadOrderPlugins: new[] { "Fallout4.esm", "Mod.esp" });
        Assert.True(manifest.MatchesPluginOrder(new[] { "/Game/Data/Fallout4.esm", "/Mods/Mod.esp" }));
        Assert.False(manifest.MatchesPluginOrder(new[] { "/Mods/Mod.esp", "/Game/Data/Fallout4.esm" }));
    }
}
