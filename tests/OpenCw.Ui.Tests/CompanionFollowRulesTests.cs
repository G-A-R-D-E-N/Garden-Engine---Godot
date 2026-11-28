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
}
