using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class PlayerIdentityTests
{
    [Fact]
    public void TheReferenceIsTheFixedPlayerKeyAndTheBaseComesFromTheRecordsByEditorId()
    {
        var keys = new System.Collections.Generic.Dictionary<(string, string), string> { [("Player", "NPC_")] = "B:Fallout4.esm" };
        Assert.Equal(("000014:Fallout4.esm", "B:Fallout4.esm"), PlayerIdentity.Resolve((id, sig) => keys.GetValueOrDefault((id, sig))));
    }

    [Fact]
    public void TheReferenceIsNeverLookedUpByEditorId()
    {
        var asked = new System.Collections.Generic.List<string>();
        PlayerIdentity.Resolve((id, sig) => { asked.Add(sig); return "B:Fallout4.esm"; });
        Assert.Equal(["NPC_"], asked);
    }

    [Fact]
    public void AMissingBaseRecordGivesNoIdentity() =>
        Assert.Null(PlayerIdentity.Resolve((id, sig) => null));

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
