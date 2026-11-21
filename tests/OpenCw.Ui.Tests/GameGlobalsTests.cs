using System;
using OpenCommonwealth.Services.Ui.Character;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class GameGlobalsTests
{
    private const string Key = "000ABC:Base.esm";

    [Fact]
    public void AnUnsetGlobalIsUnknownHereSoTheRecordValueApplies()
    {
        Assert.Null(new GameGlobals().Value(Key));
    }

    [Fact]
    public void ASetValueIsReadBackUnderAnyKeyCasingAndOnlyARealChangeBumpsTheVersion()
    {
        var globals = new GameGlobals();
        Assert.True(globals.Set(Key, 4));
        var version = globals.Version;
        Assert.Equal(4, globals.Value(Key.ToLowerInvariant()));
        Assert.True(globals.Set(Key, 4));
        Assert.Equal(version, globals.Version);
        Assert.True(globals.Set(Key, 5));
        Assert.NotEqual(version, globals.Version);
        Assert.False(globals.Set(Key, double.NaN));
        Assert.False(globals.Set("  ", 1));
    }

    [Fact]
    public void ValuesSurviveASaveAndAMalformedSectionLeavesTheCurrentOnes()
    {
        var globals = new GameGlobals();
        globals.Set(Key, 2.5);
        var saved = globals.Sections();
        var loaded = new GameGlobals();
        loaded.Apply(saved);
        Assert.Equal(2.5, loaded.Value(Key));
        var bad = new System.Collections.Generic.Dictionary<string, byte[]> { [GameGlobals.SectionName] = System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"globals\":[{\"global\":\"x\"}]}") };
        Assert.Throws<FormatException>(() => loaded.Apply(bad));
        Assert.Equal(2.5, loaded.Value(Key));
        loaded.Apply(new System.Collections.Generic.Dictionary<string, byte[]>());
        Assert.True(loaded.IsEmpty);
        Assert.Empty(new GameGlobals().Sections());
    }
}
