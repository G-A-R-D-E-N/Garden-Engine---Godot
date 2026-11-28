using OpenCommonwealth;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NpcClipRolesTests
{
    private const string Human = "meshes/actors/character/characterassets/skeleton.hkx";

    [Fact]
    public void HumanSkeletonsGetTheLocomotionGaitTableAndTheMoodTurnTable()
    {
        var sources = NpcClipRoles.BehaviorsFor(Human.ToUpperInvariant(), Human);

        Assert.Equal(2, sources.Count);
        Assert.Equal("meshes/actors/character/behaviors/locomotion_8wayblend.hkx", sources[0].Entry);
        Assert.False(sources[0].Mood);
        Assert.Equal("animations/1hm", sources[0].Directory);
        Assert.Equal(new[] { "walk", "run" }, sources[0].Roles);
        Assert.Equal(NpcClipRoles.HumanRoot + "/behaviors/mtbehavior.hkx", sources[1].Entry);
        Assert.True(sources[1].Mood);
        Assert.False(sources[1].Supplement);
        Assert.Equal(new[] { NpcClipRoles.TurnLeftRole, NpcClipRoles.TurnRightRole }, sources[1].Roles);
    }

    [Fact]
    public void ARobotSkeletonGetsOnlyTheSupplementBehaviorAndAnUnknownSkeletonGetsNothing()
    {
        var robot = Assert.Single(NpcClipRoles.BehaviorsFor(NpcClipRoles.RobotSkeletonEntry, Human));

        Assert.Equal(NpcClipRoles.RobotRoot + "/behaviors/handybehavior.hkx", robot.Entry);
        Assert.Equal(string.Empty, robot.Directory);
        Assert.True(robot.Supplement);
        Assert.False(robot.Mood);
        Assert.Equal(new[] { "walk", "run", NpcClipRoles.TurnLeftRole, NpcClipRoles.TurnRightRole }, robot.Roles);
        Assert.Empty(NpcClipRoles.BehaviorsFor("meshes/actors/other/characterassets/skeleton.hkx", Human));
    }

    [Theory]
    [InlineData("meshes/actors/character/animations/turninplaceleft90.hkx", false, "meshes/actors/character/animations/mt/neutral/turninplaceleft90.hkx")]
    [InlineData("Meshes\\Actors\\Character\\Animations\\TurnInPlaceRight90.HKX", true, "meshes/actors/character/animations/mt/neutral/female/turninplaceright90.hkx")]
    public void AnEntryDirectlyInsideTheAnimationsFolderIsRebasedOntoTheNeutralMoodFolder(string entry, bool female, string expected) =>
        Assert.Equal(expected, NpcClipRoles.MoodEntry(entry, female));

    [Theory]
    [InlineData("meshes/actors/character/animations/1hm/walkforward.hkx")]
    [InlineData("meshes/actors/character/animations/")]
    [InlineData("meshes/actors/robot/animations/turnleft.hkx")]
    [InlineData("")]
    public void ADeeperOrForeignEntryKeepsItsOwnLocation(string entry) => Assert.Null(NpcClipRoles.MoodEntry(entry, false));

    [Theory]
    [InlineData("Animations\\TurnLeft", NpcClipRoles.TurnLeftRole)]
    [InlineData("turnright.hkx", NpcClipRoles.TurnRightRole)]
    [InlineData("TurnLeft180", null)]
    [InlineData("RunForward", null)]
    [InlineData("", null)]
    public void OnlyTheBareRobotTurnNamesBecomeTurnRoles(string animation, string? role) =>
        Assert.Equal(role, NpcClipRoles.RobotRole(animation));

    [Fact]
    public void SupplementRolesTakeTheFirstClipPerRoleAndNameTheirArchiveEntry()
    {
        string[] animations = ["Animations\\Idle", "Animations\\TurnLeft", "Animations\\TurnLeft", "Animations\\TurnLeft180"];

        var (role, entry) = Assert.Single(NpcClipRoles.SupplementRoles(animations, NpcClipRoles.RobotRoot));

        Assert.Equal(NpcClipRoles.TurnLeftRole, role);
        Assert.Equal("meshes/actors/robot/animations/turnleft.hkx", entry);
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("a/../b")]
    [InlineData("C:/clips/walk")]
    [InlineData("")]
    public void AnUnsafeBehaviorNameYieldsNoEntry(string animation) => Assert.Null(NpcClipRoles.Entry(animation, NpcClipRoles.RobotRoot));

    [Fact]
    public void AnEntryIsTheRootPlusTheLowercasedExtensionlessName()
    {
        Assert.Equal("meshes/actors/robot/animations/turnleft.hkx", NpcClipRoles.Entry("Animations\\TurnLeft.HKX", NpcClipRoles.RobotRoot + "/"));
        Assert.Equal("meshes/actors/character/animations/1hm/walk.hkx", NpcClipRoles.Entry("/animations/1hm/Walk.hkx", NpcClipRoles.HumanRoot));
    }

    [Fact]
    public void AnEntryIsClaimedOncePerSkeletonAndAnotherSkeletonClaimsItAgain()
    {
        var ledger = new RetryLedger();

        Assert.True(ledger.HasUnclaimed(Human, ["a.hkx", "b.hkx"]));
        Assert.Equal(new[] { "a.hkx", "b.hkx" }, ledger.Claim(Human, ["a.hkx", "b.hkx"]));
        Assert.Empty(ledger.Claim(Human, ["A.HKX"]));
        Assert.False(ledger.HasUnclaimed(Human, ["A.hkx", "b.hkx"]));
        Assert.True(ledger.HasUnclaimed(NpcClipRoles.RobotSkeletonEntry, ["a.hkx"]));
        Assert.Equal(new[] { "a.hkx" }, ledger.Claim(NpcClipRoles.RobotSkeletonEntry, ["a.hkx"]));
    }
}
