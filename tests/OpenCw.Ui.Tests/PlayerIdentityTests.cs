using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PlayerIdentityTests
{
    [Fact]
    public void BothKeysResolveFromTheRecordsByEditorId()
    {
        var keys = new System.Collections.Generic.Dictionary<(string, string), string>
        {
            [("PlayerRef", "REFR")] = "R:Fallout4.esm", [("Player", "NPC_")] = "B:Fallout4.esm",
        };
        Assert.Equal(("R:Fallout4.esm", "B:Fallout4.esm"), PlayerIdentity.Resolve((id, sig) => keys.GetValueOrDefault((id, sig))));
    }

    [Theory]
    [InlineData("REFR")]
    [InlineData("NPC_")]
    public void AMissingRecordGivesNoIdentity(string missing) =>
        Assert.Null(PlayerIdentity.Resolve((id, sig) => sig == missing ? null : "K:Fallout4.esm"));

    [Theory]
    [InlineData(false, -1, 0, false)]
    [InlineData(true, -1, 0, true)]
    [InlineData(true, 0, 0, false)]
    [InlineData(true, 0, 1, true)]
    [InlineData(false, 0, 1, false)]
    public void ResolvingWaitsForALoadOrderAndRunsOncePerSession(bool hasLoadOrder, int attempted, int session, bool expected) =>
        Assert.Equal(expected, PlayerIdentity.NeedsResolve(hasLoadOrder, attempted, session));

    [Fact]
    public void AnEmptyKeyGivesNoIdentity() => Assert.Null(PlayerIdentity.Resolve((_, _) => ""));
}
