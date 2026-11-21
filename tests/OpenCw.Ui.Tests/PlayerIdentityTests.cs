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

    [Theory]
    [InlineData(true, -1, 0, 0, 0.0, false)]
    [InlineData(true, -1, 0, 0, 4.9, false)]
    [InlineData(true, -1, 0, 0, 5.0, true)]
    [InlineData(true, -1, 1, 0, 0.0, true)]
    [InlineData(true, 0, 0, 0, 60.0, false)]
    [InlineData(false, -1, 0, 0, 60.0, false)]
    public void AFailedLookupIsRetriedAfterTheDelayAndASuccessfulOneIsNot(bool hasLoadOrder, int resolved, int session, int attempted, double since, bool expected) =>
        Assert.Equal(expected, PlayerIdentity.NeedsResolve(hasLoadOrder, resolved, session, attempted, since));

    [Fact]
    public void AnEmptyKeyGivesNoIdentity() => Assert.Null(PlayerIdentity.Resolve((_, _) => ""));
}
