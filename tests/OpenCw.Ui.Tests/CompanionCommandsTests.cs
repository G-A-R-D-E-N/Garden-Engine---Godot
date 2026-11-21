using System.Numerics;
using System.Linq;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionCommandsTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";
    const string Base = "0A01:A.esm";

    static CompanionRoster Recruited()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, Base);
        return roster;
    }

    [Fact]
    public void Come_with_me_recruits_a_possible_companion_and_refuses_others()
    {
        var roster = new CompanionRoster();

        Assert.False(CompanionCommands.Apply(CompanionMenuEntry.ComeWithMe, roster, Ann, Base, false));
        Assert.Null(roster.Current);
        Assert.True(CompanionCommands.Apply(CompanionMenuEntry.ComeWithMe, roster, Ann, Base, true));
        Assert.Equal(new CompanionEntry(Ann, Base, CompanionState.Follow, CompanionDistance.Medium, CompanionStance.Defensive), roster.Current);
    }

    [Fact]
    public void Wait_then_follow_flip_the_state_and_keep_distance_and_stance()
    {
        var roster = Recruited();

        Assert.True(CompanionCommands.Apply(CompanionMenuEntry.Wait, roster, Ann, Base, true));
        Assert.Equal(CompanionState.Wait, roster.Current!.State);
        Assert.True(CompanionCommands.Apply(CompanionMenuEntry.Follow, roster, Ann, Base, true));
        Assert.Equal(CompanionState.Follow, roster.Current!.State);
        Assert.Equal(CompanionDistance.Medium, roster.Current.Distance);
        Assert.Equal(CompanionStance.Defensive, roster.Current.Stance);
    }

    [Fact]
    public void Distance_cycles_near_medium_far_and_stance_toggles()
    {
        var roster = Recruited();

        CompanionCommands.Apply(CompanionMenuEntry.Distance, roster, Ann, Base, true);
        Assert.Equal(CompanionDistance.Far, roster.Current!.Distance);
        CompanionCommands.Apply(CompanionMenuEntry.Distance, roster, Ann, Base, true);
        Assert.Equal(CompanionDistance.Near, roster.Current!.Distance);
        CompanionCommands.Apply(CompanionMenuEntry.Distance, roster, Ann, Base, true);
        Assert.Equal(CompanionDistance.Medium, roster.Current!.Distance);
        CompanionCommands.Apply(CompanionMenuEntry.Stance, roster, Ann, Base, true);
        Assert.Equal(CompanionStance.Aggressive, roster.Current!.Stance);
        CompanionCommands.Apply(CompanionMenuEntry.Stance, roster, Ann, Base, true);
        Assert.Equal(CompanionStance.Defensive, roster.Current!.Stance);
    }

    [Fact]
    public void Dismiss_empties_the_roster_and_a_second_dismiss_is_refused()
    {
        var roster = Recruited();

        Assert.True(CompanionCommands.Apply(CompanionMenuEntry.Dismiss, roster, Ann, Base, true));
        Assert.Null(roster.Current);
        Assert.False(CompanionCommands.Apply(CompanionMenuEntry.Dismiss, roster, Ann, Base, true));
    }

    [Fact]
    public void Commands_for_an_actor_that_is_not_the_companion_change_nothing()
    {
        var roster = Recruited();
        var before = roster.Current;

        foreach (var entry in new[] { CompanionMenuEntry.Wait, CompanionMenuEntry.Follow, CompanionMenuEntry.Distance, CompanionMenuEntry.Stance, CompanionMenuEntry.Dismiss })
            Assert.False(CompanionCommands.Apply(entry, roster, Bob, Base, true));
        Assert.Equal(before, roster.Current);
    }

    [Fact]
    public void Talk_and_trade_are_not_roster_commands()
    {
        var roster = Recruited();
        var before = roster.Current;

        Assert.False(CompanionCommands.Apply(CompanionMenuEntry.Talk, roster, Ann, Base, true));
        Assert.False(CompanionCommands.Apply(CompanionMenuEntry.Trade, roster, Ann, Base, true));
        Assert.Equal(before, roster.Current);
    }

    [Fact]
    public void Recruiting_another_actor_replaces_the_companion()
    {
        var roster = Recruited();

        Assert.True(CompanionCommands.Apply(CompanionMenuEntry.ComeWithMe, roster, Bob, Base, true));
        Assert.False(roster.IsCompanion(Ann));
        Assert.True(roster.IsCompanion(Bob));
    }

    [Fact]
    public void Labels_show_the_current_distance_and_stance()
    {
        var held = new CompanionEntry(Ann, Base, CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive);

        Assert.Equal("Distance: Far", CompanionCommands.Label(CompanionMenuEntry.Distance, held));
        Assert.Equal("Stance: Aggressive", CompanionCommands.Label(CompanionMenuEntry.Stance, held));
        Assert.Equal("Come with me", CompanionCommands.Label(CompanionMenuEntry.ComeWithMe, null));
        Assert.Equal("Distance", CompanionCommands.Label(CompanionMenuEntry.Distance, null));
    }

    [Fact]
    public void Come_with_me_without_a_possible_companion_flag_never_reaches_the_roster_with_an_empty_key()
    {
        var roster = new CompanionRoster();

        Assert.False(CompanionCommands.Apply(CompanionMenuEntry.ComeWithMe, roster, Ann, "", false));
        Assert.Null(roster.Current);
    }

    static CompanionMenuEntry[] Offered(CompanionRoster roster, string refKey, bool canRecruit, CompanionMenuLocks locks = CompanionMenuLocks.None) =>
        CompanionCommands.Menu(roster, refKey, canRecruit, locks).Select(row => (CompanionMenuEntry)row.Id).ToArray();

    static void Pick(CompanionRoster roster, string refKey, CompanionMenuEntry entry)
    {
        Assert.Contains(entry, Offered(roster, refKey, true));
        Assert.True(CompanionCommands.Apply(entry, roster, refKey, Base, true));
    }

    static FollowStep Plan(CompanionEntry entry, float now, FollowMemory memory) =>
        FollowPlanner.Step(new Vector3(FollowDistances.Medium * 2f, 0f, 0f), Vector3.Zero, CompanionFollowRules.StateOf(entry.State), CompanionFollowRules.StartUnits(entry.Distance), now, memory, FollowTunables.Default);

    [Fact]
    public void Recruit_follow_wait_follow_dismiss_walks_the_menu_the_planner_and_the_carry_rule_in_step()
    {
        var roster = new CompanionRoster();
        Assert.Equal(new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.ComeWithMe }, Offered(roster, Ann, true));
        Assert.Equal(new[] { CompanionMenuEntry.Talk }, Offered(roster, Ann, false));

        Pick(roster, Ann, CompanionMenuEntry.ComeWithMe);
        Assert.Equal(
            new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.Wait, CompanionMenuEntry.Distance, CompanionMenuEntry.Dismiss },
            Offered(roster, Ann, false, CompanionMenuLocks.Trade));
        Assert.Equal(CompanionMenuEntry.Trade, Offered(roster, Ann, false)[1]);
        Assert.Equal(new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.ComeWithMe }, Offered(roster, Bob, true));
        var walking = Plan(roster.Current!, 0f, FollowMemory.None);
        Assert.Equal(FollowAction.GoTo, walking.Action);
        Assert.True(CompanionFollowRules.Carries(roster.Current, Ann, true));

        Pick(roster, Ann, CompanionMenuEntry.Wait);
        Assert.DoesNotContain(CompanionMenuEntry.Wait, Offered(roster, Ann, false));
        Assert.Contains(CompanionMenuEntry.Follow, Offered(roster, Ann, false));
        Assert.Equal(FollowAction.Stop, Plan(roster.Current!, 1f, walking.Memory).Action);
        Assert.False(CompanionFollowRules.Carries(roster.Current, Ann, true));

        Pick(roster, Ann, CompanionMenuEntry.Follow);
        Assert.Contains(CompanionMenuEntry.Wait, Offered(roster, Ann, false));
        Assert.Equal(FollowAction.GoTo, Plan(roster.Current!, 2f, FollowMemory.None).Action);
        Assert.True(CompanionFollowRules.Carries(roster.Current, Ann, true));

        Pick(roster, Ann, CompanionMenuEntry.Dismiss);
        Assert.Equal(new[] { CompanionMenuEntry.Talk, CompanionMenuEntry.ComeWithMe }, Offered(roster, Ann, true));
        Assert.False(CompanionFollowRules.Carries(roster.Current, Ann, true));
    }

    [Fact]
    public void The_menu_row_ids_round_trip_to_the_entries_and_labels_follow_the_roster()
    {
        var roster = Recruited();
        CompanionCommands.Apply(CompanionMenuEntry.Distance, roster, Ann, Base, true);

        var rows = CompanionCommands.Menu(roster, Ann, false, CompanionMenuLocks.None);

        Assert.Equal(new[] { "Talk", "Trade", "Wait here", "Distance: Far", "Dismiss" }, rows.Select(row => row.Label));
        Assert.Equal(rows.Select(row => CompanionCommands.Label((CompanionMenuEntry)row.Id, roster.Current)), rows.Select(row => row.Label));
        Assert.Null(CompanionCommands.Notice(CompanionMenuEntry.Wait));
        Assert.Equal("Companion recruited.", CompanionCommands.Notice(CompanionMenuEntry.ComeWithMe));
        Assert.Equal("Companion dismissed.", CompanionCommands.Notice(CompanionMenuEntry.Dismiss));
    }
}
