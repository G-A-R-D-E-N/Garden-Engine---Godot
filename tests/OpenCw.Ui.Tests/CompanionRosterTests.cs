using System;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionRosterTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";

    [Fact]
    public void Recruit_fills_the_one_slot_as_follow_at_medium_distance_and_defensive_stance()
    {
        var roster = new CompanionRoster();

        Assert.True(roster.Recruit(Ann, "0A01:A.esm"));

        Assert.True(roster.IsCompanion(Ann));
        Assert.True(roster.HasBeenCompanion(Ann));
        Assert.Equal(new CompanionEntry(Ann, "0A01:A.esm", CompanionState.Follow, CompanionDistance.Medium, CompanionStance.Defensive), roster.Current);
    }

    [Fact]
    public void Recruiting_another_actor_dismisses_the_previous_companion()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");

        Assert.True(roster.Recruit(Bob, "0B01:A.esm"));

        Assert.False(roster.IsCompanion(Ann));
        Assert.True(roster.IsCompanion(Bob));
        Assert.True(roster.HasBeenCompanion(Ann));
        Assert.Equal("0B01:A.esm", roster.Current!.BaseKey);
    }

    [Fact]
    public void Recruiting_the_current_companion_again_is_a_no_op_that_keeps_its_state()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");
        roster.Configure(Ann, CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive);

        Assert.False(roster.Recruit(Ann, "0A02:A.esm"));

        Assert.Equal(new CompanionEntry(Ann, "0A01:A.esm", CompanionState.Wait, CompanionDistance.Far, CompanionStance.Aggressive), roster.Current);
    }

    [Fact]
    public void Dismiss_clears_only_the_matching_companion()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");

        Assert.False(roster.Dismiss(Bob));
        Assert.True(roster.IsCompanion(Ann));

        Assert.True(roster.Dismiss(Ann));
        Assert.False(roster.IsCompanion(Ann));
        Assert.Null(roster.Current);
    }

    [Fact]
    public void A_disallowed_actor_is_refused_until_it_is_allowed_again()
    {
        var roster = new CompanionRoster();
        roster.SetDisallowed(Ann, true);

        Assert.False(roster.Recruit(Ann, "0A01:A.esm"));
        Assert.False(roster.IsCompanion(Ann));
        Assert.True(roster.IsDisallowed(Ann));

        roster.SetDisallowed(Ann, false);
        Assert.True(roster.Recruit(Ann, "0A01:A.esm"));
    }

    [Fact]
    public void Empty_keys_throw_and_an_empty_key_is_never_a_companion()
    {
        var roster = new CompanionRoster();

        Assert.ThrowsAny<ArgumentException>(() => roster.Recruit("", "0A01:A.esm"));
        Assert.ThrowsAny<ArgumentException>(() => roster.Recruit(Ann, ""));
        Assert.ThrowsAny<ArgumentException>(() => roster.SetDisallowed("", true));
        Assert.False(roster.IsCompanion(""));
    }

    [Fact]
    public void Configure_changes_only_the_current_companion()
    {
        var roster = new CompanionRoster();
        roster.Recruit(Ann, "0A01:A.esm");

        Assert.False(roster.Configure(Bob, CompanionState.Wait, CompanionDistance.Near, CompanionStance.Aggressive));
        Assert.True(roster.Configure(Ann, CompanionState.Wait, CompanionDistance.Near, CompanionStance.Aggressive));
        Assert.Equal(new CompanionEntry(Ann, "0A01:A.esm", CompanionState.Wait, CompanionDistance.Near, CompanionStance.Aggressive), roster.Current);
    }

    [Fact]
    public void Every_change_raises_Changed_and_Clear_drops_the_companion_and_both_sets()
    {
        var roster = new CompanionRoster();
        int raised = 0;
        roster.Changed += () => raised++;
        roster.Recruit(Ann, "0A01:A.esm");
        roster.Dismiss(Ann);
        roster.Dismiss(Ann);
        Assert.Equal(2, raised);

        roster.SetDisallowed(Bob, true);
        roster.Recruit(Ann, "0A01:A.esm");
        roster.Clear();

        Assert.Null(roster.Current);
        Assert.False(roster.HasBeenCompanion(Ann));
        Assert.False(roster.IsDisallowed(Bob));
    }
}
