using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionRelationsTests
{
    [Fact]
    public void A_companion_and_the_player_are_allies_in_either_order()
    {
        Assert.True(CompanionRelations.AlliedWithPlayer(true, false, false, true));
        Assert.True(CompanionRelations.AlliedWithPlayer(false, true, true, false));
    }

    [Fact]
    public void Two_non_players_or_a_companion_and_an_npc_are_left_to_the_engine()
    {
        Assert.False(CompanionRelations.AlliedWithPlayer(true, false, false, false));
        Assert.False(CompanionRelations.AlliedWithPlayer(false, false, true, false));
        Assert.False(CompanionRelations.AlliedWithPlayer(false, true, false, false));
    }

    [Fact]
    public void The_player_is_not_an_ally_of_itself()
    {
        Assert.False(CompanionRelations.AlliedWithPlayer(false, true, false, true));
    }
}
