using System;
using System.Numerics;
using OpenCommonwealth.Services.Interaction;
using Xunit;

namespace OpenCw.Ui.Tests;

public class FollowPlannerTests
{
    private static readonly Vector3 Origin = Vector3.Zero;

    private static readonly FollowMemory Walking = new(true, new Vector3(67.5f, 0, 0), Origin, 0f);

    private static FollowStep Step(Vector3 self, Vector3 player, FollowMemory memory = default, float now = 0f, FollowerState state = FollowerState.Follow) =>
        FollowPlanner.Step(self, player, state, FollowDistances.Near, now, memory, FollowTunables.Default);

    [Fact]
    public void Beyond_the_start_band_walks_to_the_stop_distance_on_the_line_to_the_player()
    {
        var step = Step(new Vector3(200, 0, 0), Origin);

        Assert.Equal(FollowAction.GoTo, step.Action);
        Assert.Equal(FollowGait.Walk, step.Gait);
        Assert.True(step.Memory.HasGoal);
        Assert.Equal(new Vector3(67.5f, 0, 0), step.Memory.Goal);
    }

    [Fact]
    public void Inside_the_start_band_an_idle_follower_stays_idle()
    {
        var step = Step(new Vector3(150, 0, 0), Origin);

        Assert.Equal(FollowAction.Idle, step.Action);
        Assert.Equal(FollowMemory.None, step.Memory);
    }

    [Fact]
    public void Just_past_the_start_band_an_idle_follower_starts_walking()
    {
        Assert.Equal(FollowAction.GoTo, Step(new Vector3(151, 0, 0), Origin).Action);
    }

    [Fact]
    public void A_walking_follower_keeps_walking_between_the_stop_and_start_bands()
    {
        var step = Step(new Vector3(120, 0, 0), Origin, Walking, now: 0.1f);

        Assert.Equal(FollowAction.GoTo, step.Action);
        Assert.Equal(Walking, step.Memory);
    }

    [Fact]
    public void A_walking_follower_stops_once_inside_the_stop_band()
    {
        var step = Step(new Vector3(75, 0, 0), Origin, Walking, now: 0.1f);

        Assert.Equal(FollowAction.Stop, step.Action);
        Assert.Equal(FollowMemory.None, step.Memory);
    }

    [Fact]
    public void The_goal_is_kept_within_half_a_second_of_its_issue()
    {
        var memory = new FollowMemory(true, new Vector3(100, 0, 0), new Vector3(400, 0, 0), 0f);

        var step = Step(Origin, new Vector3(600, 0, 0), memory, now: 0.4f);

        Assert.Equal(memory, step.Memory);
    }

    [Fact]
    public void A_player_move_under_one_metre_keeps_the_goal()
    {
        var memory = new FollowMemory(true, new Vector3(100, 0, 0), new Vector3(400, 0, 0), 0f);

        var step = Step(Origin, new Vector3(400.5f, 0, 0), memory, now: 5f);

        Assert.Equal(memory, step.Memory);
    }

    [Fact]
    public void After_half_a_second_and_a_player_move_the_goal_is_repathed_to_the_stop_distance()
    {
        var memory = new FollowMemory(true, new Vector3(100, 0, 0), new Vector3(400, 0, 0), 0f);

        var step = Step(Origin, new Vector3(600, 0, 0), memory, now: 0.5f);

        Assert.Equal(FollowAction.GoTo, step.Action);
        Assert.Equal(new Vector3(532.5f, 0, 0), step.Memory.Goal);
        Assert.Equal(new Vector3(600, 0, 0), step.Memory.PlayerAtIssue);
        Assert.Equal(0.5f, step.Memory.IssuedAt);
    }

    [Theory]
    [InlineData(151f)]
    [InlineData(200f)]
    [InlineData(500f)]
    [InlineData(2000f)]
    public void The_goal_is_inside_the_stop_distance_and_never_on_the_player(float distance)
    {
        var goal = Step(new Vector3(distance, 0, 0), Origin).Memory.Goal;

        Assert.InRange(Vector3.Distance(goal, Origin), 67.4f, 67.6f);
    }

    [Fact]
    public void A_zero_distance_is_idle_when_not_walking_and_stops_a_walk()
    {
        Assert.Equal(FollowAction.Idle, Step(Origin, Origin).Action);
        Assert.Equal(FollowAction.Stop, Step(Origin, Origin, Walking, now: 0.1f).Action);
    }

    [Fact]
    public void A_non_finite_position_stops_and_clears_the_goal()
    {
        var step = Step(new Vector3(float.NaN, 0, 0), Origin, Walking, now: 0.1f);

        Assert.Equal(FollowAction.Stop, step.Action);
        Assert.Equal(FollowMemory.None, step.Memory);
    }

    [Fact]
    public void Running_starts_only_beyond_twice_the_start_band()
    {
        Assert.Equal(FollowGait.Walk, Step(new Vector3(300, 0, 0), Origin).Gait);
        Assert.Equal(FollowGait.Run, Step(new Vector3(301, 0, 0), Origin).Gait);
    }

    [Fact]
    public void A_start_band_that_is_not_positive_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FollowPlanner.Step(Origin, Origin, FollowerState.Follow, 0f, 0f, FollowMemory.None, FollowTunables.Default));
    }

    [Fact]
    public void A_waiting_follower_stops_and_drops_its_goal_even_far_from_the_player()
    {
        var step = Step(Origin, new Vector3(900, 0, 0), Walking, now: 0.1f, state: FollowerState.Wait);

        Assert.Equal(FollowAction.Stop, step.Action);
        Assert.Equal(FollowMemory.None, step.Memory);
    }

    [Fact]
    public void A_waiting_follower_holds_while_the_player_walks_away()
    {
        for (var i = 0; i < 10; i++)
        {
            var step = Step(Origin, new Vector3(300 + 100 * i, 0, 0), FollowMemory.None, now: i * 0.5f, state: FollowerState.Wait);

            Assert.Equal(FollowAction.Stop, step.Action);
        }
    }

    [Fact]
    public void Following_resumes_from_a_wait_with_a_fresh_goal()
    {
        var waiting = Step(Origin, new Vector3(400, 0, 0), Walking, now: 0.1f, state: FollowerState.Wait);

        var resumed = Step(Origin, new Vector3(400, 0, 0), waiting.Memory, now: 6f);

        Assert.Equal(FollowAction.GoTo, resumed.Action);
        Assert.Equal(FollowGait.Run, resumed.Gait);
        Assert.Equal(new Vector3(332.5f, 0, 0), resumed.Memory.Goal);
        Assert.Equal(6f, resumed.Memory.IssuedAt);
    }

    [Fact]
    public void Following_resumes_idle_when_the_player_is_still_inside_the_start_band()
    {
        var step = Step(Origin, new Vector3(100, 0, 0), FollowMemory.None, now: 6f, state: FollowerState.Follow);

        Assert.Equal(FollowAction.Idle, step.Action);
    }

    [Fact]
    public void The_goal_is_strictly_inside_the_stop_distance()
    {
        var goal = Step(new Vector3(200, 0, 0), Origin).Memory.Goal;

        Assert.True(Vector3.Distance(goal, Origin) < FollowDistances.Near * FollowTunables.Default.StopRatio);
    }

    [Fact]
    public void A_follower_that_reaches_its_goal_stops()
    {
        var issued = Step(new Vector3(200, 0, 0), Origin);

        var arrived = Step(issued.Memory.Goal, Origin, issued.Memory, now: 0.1f);

        Assert.Equal(FollowAction.Stop, arrived.Action);
    }

    [Fact]
    public void Height_is_ignored_in_the_distance_and_the_goal_keeps_the_player_height()
    {
        var step = Step(new Vector3(200, 1000, 0), Origin);

        Assert.Equal(new Vector3(67.5f, 0, 0), step.Memory.Goal);
    }

    [Fact]
    public void A_follower_straight_along_z_gets_a_goal_on_z()
    {
        var step = Step(new Vector3(0, 0, 200), Origin);

        Assert.Equal(new Vector3(0, 0, 67.5f), step.Memory.Goal);
    }

    [Fact]
    public void A_diagonal_follower_gets_a_goal_on_the_horizontal_bearing()
    {
        var step = Step(new Vector3(162, 0, 216), Origin);

        Assert.Equal(new Vector3(40.5f, 0, 54), step.Memory.Goal);
    }

    [Fact]
    public void A_goal_issued_after_the_current_clock_time_is_expired_and_repathed()
    {
        var memory = new FollowMemory(true, new Vector3(100, 0, 0), new Vector3(600, 0, 0), 10f);

        var step = Step(Origin, new Vector3(600, 0, 0), memory, now: 2f);

        Assert.Equal(FollowAction.GoTo, step.Action);
        Assert.Equal(new Vector3(532.5f, 0, 0), step.Memory.Goal);
        Assert.Equal(2f, step.Memory.IssuedAt);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-0.5f)]
    [InlineData(float.NaN)]
    public void A_stop_ratio_outside_zero_to_one_is_refused(float stopRatio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FollowPlanner.Step(new Vector3(200, 0, 0), Origin, FollowerState.Follow, FollowDistances.Near, 0f, FollowMemory.None, FollowTunables.Default with { StopRatio = stopRatio }));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(float.NaN)]
    public void A_run_ratio_not_above_one_is_refused(float runRatio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FollowPlanner.Step(new Vector3(200, 0, 0), Origin, FollowerState.Follow, FollowDistances.Near, 0f, FollowMemory.None, FollowTunables.Default with { RunRatio = runRatio }));
    }
}
