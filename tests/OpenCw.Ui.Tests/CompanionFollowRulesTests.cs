using System;
using System.Numerics;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class CompanionFollowRulesTests
{
    const string Ann = "00AA:A.esm";
    const string Bob = "00BB:A.esm";

    static CompanionEntry Entry(CompanionState state = CompanionState.Follow, string refKey = Ann) =>
        new(refKey, "0A01:A.esm", state, CompanionDistance.Medium, CompanionStance.Defensive);

    [Fact]
    public void Each_distance_maps_to_its_follow_band()
    {
        Assert.Equal(FollowDistances.Near, CompanionFollowRules.StartUnits(CompanionDistance.Near));
        Assert.Equal(FollowDistances.Medium, CompanionFollowRules.StartUnits(CompanionDistance.Medium));
        Assert.Equal(FollowDistances.Far, CompanionFollowRules.StartUnits(CompanionDistance.Far));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFollowRules.StartUnits((CompanionDistance)99));
    }

    [Fact]
    public void Each_roster_state_maps_to_its_planner_state()
    {
        Assert.Equal(FollowerState.Follow, CompanionFollowRules.StateOf(CompanionState.Follow));
        Assert.Equal(FollowerState.Wait, CompanionFollowRules.StateOf(CompanionState.Wait));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFollowRules.StateOf((CompanionState)99));
    }

    [Fact]
    public void The_placement_distance_is_inside_the_stop_band_so_a_placed_follower_does_not_walk()
    {
        var tunables = FollowTunables.Default;
        float place = CompanionFollowRules.PlaceUnits(CompanionDistance.Near, tunables);
        var step = FollowPlanner.Step(new Vector3(place, 0, 0), Vector3.Zero, FollowerState.Follow, FollowDistances.Near, 0f, FollowMemory.None, tunables);

        Assert.True(place < FollowDistances.Near * tunables.StopRatio);
        Assert.Equal(FollowAction.Idle, step.Action);
    }

    [Fact]
    public void Only_a_living_follow_companion_is_carried()
    {
        Assert.True(CompanionFollowRules.Carries(Entry(), Ann, true));
        Assert.False(CompanionFollowRules.Carries(Entry(CompanionState.Wait), Ann, true));
        Assert.False(CompanionFollowRules.Carries(Entry(), Ann, false));
        Assert.False(CompanionFollowRules.Carries(Entry(), Bob, true));
        Assert.False(CompanionFollowRules.Carries(null, Ann, true));
        Assert.False(CompanionFollowRules.Carries(Entry(), "", true));
    }

    [Fact]
    public void A_marker_is_skipped_only_while_a_live_puppet_with_its_reference_is_present()
    {
        Assert.True(CompanionFollowRules.SkipsMarker(Ann, true));
        Assert.False(CompanionFollowRules.SkipsMarker(Ann, false));
        Assert.False(CompanionFollowRules.SkipsMarker(Bob, false));
        Assert.True(CompanionFollowRules.SkipsMarker(Bob, true));
        Assert.False(CompanionFollowRules.SkipsMarker("", true));
    }

    [Fact]
    public void Only_a_follow_companion_with_no_puppet_and_an_enabled_reference_needs_a_respawn()
    {
        Assert.True(CompanionFollowRules.NeedsRespawn(Entry(), false, true));
        Assert.False(CompanionFollowRules.NeedsRespawn(Entry(), true, true));
        Assert.False(CompanionFollowRules.NeedsRespawn(Entry(), false, false));
        Assert.False(CompanionFollowRules.NeedsRespawn(Entry(CompanionState.Wait), false, true));
        Assert.False(CompanionFollowRules.NeedsRespawn(null, false, true));
    }

    [Fact]
    public void Candidates_are_right_left_then_behind_the_player_at_yaw_zero()
    {
        var player = new Vector3(10, 2, 20);

        var at = CompanionFollowRules.Candidates(player, 0f, 1.5f);

        Assert.Equal(3, at.Length);
        Assert.Equal(new Vector3(11.5f, 2, 20), at[0]);
        Assert.Equal(new Vector3(8.5f, 2, 20), at[1]);
        Assert.Equal(new Vector3(10, 2, 21.5f), at[2]);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.3f)]
    [InlineData(-3f)]
    public void No_candidate_is_the_player_s_own_position_so_the_forced_placement_never_overlaps(float yaw)
    {
        var player = new Vector3(10, 2, 20);

        Assert.All(CompanionFollowRules.Candidates(player, yaw, 1.5f), point => Assert.True(Vector3.Distance(player, point) > 1f));
    }

    [Fact]
    public void Candidates_turn_with_the_yaw()
    {
        var at = CompanionFollowRules.Candidates(Vector3.Zero, MathF.PI / 2f, 2f);

        Assert.True(Vector3.Distance(new Vector3(0, 0, -2), at[0]) < 1e-5f);
        Assert.True(Vector3.Distance(new Vector3(0, 0, 2), at[1]) < 1e-5f);
        Assert.True(Vector3.Distance(new Vector3(2, 0, 0), at[2]) < 1e-5f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_bad_distance_is_refused(float meters) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFollowRules.Candidates(Vector3.Zero, 0f, meters));

    [Fact]
    public void A_non_finite_yaw_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CompanionFollowRules.Candidates(Vector3.Zero, float.NaN, 1f));

    // Detects: a waiting companion left in the carry holder, where the next transition frees it; only a Wait entry for the same reference must be adopted.
    [Fact]
    public void Only_the_waiting_companion_is_adopted_into_its_cell()
    {
        Assert.True(CompanionFollowRules.AdoptsIntoCell(Entry(CompanionState.Wait), Ann));
        Assert.False(CompanionFollowRules.AdoptsIntoCell(Entry(CompanionState.Follow), Ann));
        Assert.False(CompanionFollowRules.AdoptsIntoCell(Entry(CompanionState.Wait), Bob));
        Assert.False(CompanionFollowRules.AdoptsIntoCell(null, Ann));
        Assert.False(CompanionFollowRules.AdoptsIntoCell(Entry(CompanionState.Wait), ""));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(0, 0, false)]
    [InlineData(-1, -1, false)]
    public void A_follower_owns_the_walker_route_only_while_it_is_the_route_it_started(int mine, int current, bool owns) =>
        Assert.Equal(owns, CompanionFollowRules.OwnsRoute(mine, current));

    [Fact]
    public void The_first_candidate_that_resolves_and_is_clear_is_chosen_in_order()
    {
        var at = new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(3, 0, 0) };

        var picked = CompanionFollowRules.FirstClear(at, point => point.X == 1 ? null : point, point => point.X != 2);

        Assert.Equal(new Vector3(3, 0, 0), picked);
    }

    [Fact]
    public void A_clear_first_candidate_wins_and_later_ones_are_not_probed()
    {
        var at = new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0) };
        int probed = 0;

        var picked = CompanionFollowRules.FirstClear(at, point => point, point => { probed++; return true; });

        Assert.Equal(new Vector3(1, 0, 0), picked);
        Assert.Equal(1, probed);
    }

    [Fact]
    public void The_resolved_point_is_the_one_tested_for_clearance_and_returned()
    {
        var at = new[] { new Vector3(1, 0, 0) };
        var moved = new Vector3(1, 5, 0);

        var picked = CompanionFollowRules.FirstClear(at, point => moved, point => point == moved);

        Assert.Equal(moved, picked);
    }

    [Fact]
    public void No_clear_candidate_gives_null_so_the_caller_can_fall_back_to_the_first_offset()
    {
        var at = CompanionFollowRules.Candidates(Vector3.Zero, 0f, 1.5f);

        Assert.Null(CompanionFollowRules.FirstClear(at, point => point, point => false));
        Assert.Null(CompanionFollowRules.FirstClear(at, point => null, point => true));
        Assert.Null(CompanionFollowRules.FirstClear([], point => point, point => true));
    }

    [Fact]
    public void A_pending_arrival_whose_player_went_away_retargets_instead_of_dropping_the_marker()
    {
        Assert.Equal(PendingStep.Retarget, CompanionFollowRules.PlanPending(true, true, true, false, 1f, 10f));
        Assert.Equal(PendingStep.Place, CompanionFollowRules.PlanPending(true, true, true, true, 1f, 10f));
    }

    [Fact]
    public void A_pending_arrival_with_no_player_waits_until_the_timeout_then_retargets()
    {
        Assert.Equal(PendingStep.Wait, CompanionFollowRules.PlanPending(true, true, false, false, 9.9f, 10f));
        Assert.Equal(PendingStep.Retarget, CompanionFollowRules.PlanPending(true, true, false, false, 10f, 10f));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void A_pending_arrival_is_dropped_only_when_its_puppet_is_gone_or_unmarked(bool puppetLive, bool marked)
    {
        Assert.Equal(PendingStep.Drop, CompanionFollowRules.PlanPending(puppetLive, marked, true, true, 0f, 10f));
        Assert.Equal(PendingStep.Drop, CompanionFollowRules.PlanPending(puppetLive, marked, false, false, 20f, 10f));
    }

    [Fact]
    public void The_death_latch_follows_the_puppet_and_is_cleared_by_a_live_one()
    {
        Assert.Equal(Ann, CompanionFollowRules.DeathLatch(null, Ann, true, false));
        Assert.Null(CompanionFollowRules.DeathLatch(Ann, Ann, true, true));
        Assert.Equal(Ann, CompanionFollowRules.DeathLatch(Ann, Ann, false, false));
        Assert.Null(CompanionFollowRules.DeathLatch(null, Ann, false, false));
    }

    [Fact]
    public void The_death_latch_of_another_companion_does_not_suppress_this_one()
    {
        Assert.Null(CompanionFollowRules.DeathLatch(Bob, Ann, false, false));
    }

    [Fact]
    public void After_a_load_the_latch_comes_from_the_saved_life_not_the_replaced_world()
    {
        Assert.Null(CompanionFollowRules.LatchAfterLoad(Entry(), false));
        Assert.Null(CompanionFollowRules.LatchAfterLoad(Entry(), null));
        Assert.Null(CompanionFollowRules.LatchAfterLoad(null, true));
        Assert.Equal(Ann, CompanionFollowRules.LatchAfterLoad(Entry(), true));
    }

    [Fact]
    public void Death_after_a_save_then_loading_it_lets_the_absent_companion_respawn()
    {
        var latched = CompanionFollowRules.DeathLatch(null, Ann, true, false);
        Assert.Equal(Ann, latched);

        latched = CompanionFollowRules.LatchAfterLoad(Entry(), false);
        latched = CompanionFollowRules.DeathLatch(latched, Ann, false, false);

        Assert.True(CompanionFollowRules.NeedsRespawn(Entry(), false, true));
        Assert.NotEqual(Ann, latched);
    }
}
