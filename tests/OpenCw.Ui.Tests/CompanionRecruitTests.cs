using System.Linq;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionRecruitTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";

    [Fact]
    public void An_actor_without_the_companion_script_cannot_be_asked()
    {
        var roster = new CompanionRoster();

        Assert.False(CompanionRecruit.CanAsk(false, roster, Ann));
        Assert.False(CompanionRecruit.Ask(false, roster, Ann, "0A01:A.esm"));
        Assert.Null(roster.Current);
    }

    [Fact]
    public void A_disallowed_or_current_companion_or_empty_key_cannot_be_asked()
    {
        var roster = new CompanionRoster();
        roster.SetDisallowed(Bob, true);
        roster.Recruit(Ann, "0A01:A.esm");

        Assert.False(CompanionRecruit.CanAsk(true, roster, Bob));
        Assert.False(CompanionRecruit.CanAsk(true, roster, Ann));
        Assert.False(CompanionRecruit.CanAsk(true, roster, ""));
    }

    [Fact]
    public void Asking_a_possible_companion_fills_the_roster_and_the_companion_menu_has_no_come_with_me()
    {
        var roster = new CompanionRoster();

        Assert.True(CompanionRecruit.Ask(true, roster, Ann, "0A01:A.esm"));

        Assert.True(roster.IsCompanion(Ann));
        Assert.DoesNotContain(CompanionMenuEntry.ComeWithMe, CompanionCommandMenu.Entries(true, FollowerState.Follow, false, CompanionMenuLocks.None));
        Assert.Contains(CompanionMenuEntry.ComeWithMe, CompanionCommandMenu.Entries(false, FollowerState.Follow, CompanionRecruit.CanAsk(true, roster, Bob), CompanionMenuLocks.None));
    }

    [Fact]
    public void The_menu_offers_come_with_me_only_while_the_actor_can_be_asked()
    {
        var roster = new CompanionRoster();
        roster.SetDisallowed(Bob, true);

        var entries = CompanionCommandMenu.Entries(false, FollowerState.Follow, CompanionRecruit.CanAsk(true, roster, Bob), CompanionMenuLocks.None);

        Assert.Equal(new[] { CompanionMenuEntry.Talk }, entries.ToArray());
    }
}
