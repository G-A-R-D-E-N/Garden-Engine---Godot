using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionTeammateTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";
    const uint Race = 1u << NpcInteraction.RaceAllowPickpocketBit | 1u << NpcInteraction.RaceAllowDialogueBit;

    static NpcSnapshot Living(bool teammate) => new(true, true, false, false, () => false, Race, false, false, teammate);

    [Fact]
    public void A_roster_member_reads_as_a_teammate_so_a_sneaking_player_talks_to_it_and_is_never_offered_pickpocket()
    {
        var roster = new CompanionRoster();
        var teammates = new PlayerTeammates();
        roster.Recruit(Ann, "0A01:A.esm");
        var companion = Living(roster.IsTeammate(Ann, teammates.IsTeammate(Ann)));

        Assert.True(companion.PlayerTeammate);
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(companion, true));
        Assert.Equal(NpcRoute.Talk, NpcInteraction.Route(companion, true));
        Assert.Equal(PickpocketOffer.NotOffered, PickpocketRules.Offer(NpcInteraction.Target(companion), true));
    }

    [Fact]
    public void An_actor_off_the_roster_and_unflagged_still_gets_the_pickpocket_prompt_while_sneaking()
    {
        var roster = new CompanionRoster();
        var teammates = new PlayerTeammates();
        var stranger = Living(roster.IsTeammate(Ann, teammates.IsTeammate(Ann)));

        Assert.False(stranger.PlayerTeammate);
        Assert.Equal(NpcPromptKind.Pickpocket, NpcInteraction.Prompt(stranger, true));
        Assert.Equal(PickpocketOffer.Open, PickpocketRules.Offer(NpcInteraction.Target(stranger), true));
    }

    [Fact]
    public void A_teammate_set_by_a_script_still_reads_as_a_teammate_without_the_roster()
    {
        var roster = new CompanionRoster();
        var teammates = new PlayerTeammates();
        teammates.Set(Bob, true);

        Assert.True(roster.IsTeammate(Bob, teammates.IsTeammate(Bob)));
        Assert.Equal(NpcPromptKind.Talk, NpcInteraction.Prompt(Living(roster.IsTeammate(Bob, teammates.IsTeammate(Bob))), true));
    }

    [Fact]
    public void A_dismissed_companion_reads_as_a_stranger_again()
    {
        var roster = new CompanionRoster();
        var teammates = new PlayerTeammates();
        roster.Recruit(Ann, "0A01:A.esm");
        roster.Dismiss(Ann);

        var actor = Living(roster.IsTeammate(Ann, teammates.IsTeammate(Ann)));

        Assert.False(actor.PlayerTeammate);
        Assert.Equal(NpcPromptKind.Pickpocket, NpcInteraction.Prompt(actor, true));
    }
}
