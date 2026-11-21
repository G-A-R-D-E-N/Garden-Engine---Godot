using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionChoiceTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";

    [Fact]
    public void A_choice_for_the_same_talkable_actor_in_reach_runs()
    {
        Assert.True(CompanionChoice.StillValid(Ann, Ann, NpcRoute.Talk, 1f));
        Assert.True(CompanionChoice.StillValid(Ann, Ann, NpcRoute.Talk, CompanionChoice.ReachMeters));
    }

    [Theory]
    [InlineData(NpcRoute.Loot)]
    [InlineData(NpcRoute.Pickpocket)]
    [InlineData(NpcRoute.Refused)]
    public void A_target_that_died_or_stopped_routing_to_talk_is_refused(NpcRoute route)
    {
        Assert.False(CompanionChoice.StillValid(Ann, Ann, route, 1f));
    }

    [Fact]
    public void A_target_that_left_reach_while_the_menu_was_open_is_refused()
    {
        Assert.False(CompanionChoice.StillValid(Ann, Ann, NpcRoute.Talk, CompanionChoice.ReachMeters + 0.1f));
        Assert.False(CompanionChoice.StillValid(Ann, Ann, NpcRoute.Talk, float.NaN));
        Assert.False(CompanionChoice.StillValid(Ann, Ann, NpcRoute.Talk, float.PositiveInfinity));
    }

    [Fact]
    public void A_node_that_is_now_a_different_actor_or_has_no_identity_is_refused()
    {
        Assert.False(CompanionChoice.StillValid(Ann, Bob, NpcRoute.Talk, 1f));
        Assert.False(CompanionChoice.StillValid(Ann, string.Empty, NpcRoute.Talk, 1f));
        Assert.False(CompanionChoice.StillValid(string.Empty, string.Empty, NpcRoute.Talk, 1f));
    }

    [Fact]
    public void A_recruit_choice_for_an_actor_that_died_leaves_the_roster_alone()
    {
        var roster = new CompanionRoster();
        var stillValid = CompanionChoice.StillValid(Ann, Ann, NpcRoute.Loot, 1f);

        var applied = stillValid && CompanionCommands.Apply(CompanionMenuEntry.ComeWithMe, roster, Ann, "0A01:A.esm", true);

        Assert.False(applied);
        Assert.Null(roster.Current);
    }
}
