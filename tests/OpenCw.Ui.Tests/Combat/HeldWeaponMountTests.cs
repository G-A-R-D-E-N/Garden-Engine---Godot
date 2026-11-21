using OpenCommonwealth.Services.Combat;
using Xunit;

namespace OpenCw.Ui.Tests.Combat;

public sealed class HeldWeaponMountTests
{
    [Fact]
    public void TheFirstCandidateBoneThatTheRigHasIsPickedIgnoringCase()
    {
        Assert.Equal("weapon", HeldWeaponMount.HoldBone(new[] { "Root", "RArm_Hand", "weapon" }));
        Assert.Equal("RArm_Hand", HeldWeaponMount.HoldBone(new[] { "Root", "RArm_Hand" }));
        Assert.Null(HeldWeaponMount.HoldBone(new[] { "Root", "Spine" }));
        Assert.Null(HeldWeaponMount.HoldBone(System.Array.Empty<string>()));
    }

    [Theory]
    [InlineData("ap_Gun_Barrel", "Barrel")]
    [InlineData("Barrel", "Barrel")]
    [InlineData("ap_", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheSuffixIsWhatFollowsTheLastUnderscore(string? editorId, string? expected) =>
        Assert.Equal(expected, HeldWeaponMount.SuffixOf(editorId));

    [Fact]
    public void AModMountsOnTheExactPointThenOnAPointEndingWithTheSuffixThenNowhere()
    {
        var nodes = new[] { "Receiver", "P-Grip", "P-Barrel", "P-LongBarrel", "P-Scope" };
        Assert.Equal("P-Barrel", HeldWeaponMount.ModMount(nodes, "barrel"));
        Assert.Equal("P-LongBarrel", HeldWeaponMount.ModMount(new[] { "P-Grip", "P-LongBarrel" }, "Barrel"));
        Assert.Null(HeldWeaponMount.ModMount(nodes, "Muzzle"));
        Assert.Null(HeldWeaponMount.ModMount(nodes, null));
        Assert.Null(HeldWeaponMount.ModMount(new[] { "Barrel" }, "Barrel"));
    }

    [Fact]
    public void ChangingTheModsRebuildsTheVisualOfTheSameDrawnWeapon()
    {
        var tracker = new HeldWeaponTracker();
        Assert.True(tracker.Update("000001:Test.esm", true, "a"));
        Assert.False(tracker.Update("000001:Test.esm", true, "a"));
        Assert.True(tracker.Update("000001:Test.esm", true, "a,b"));
        Assert.True(tracker.Update("000001:Test.esm", false, "a,b"));
        Assert.False(tracker.Update("000001:Test.esm", false, "c"));
    }
}
