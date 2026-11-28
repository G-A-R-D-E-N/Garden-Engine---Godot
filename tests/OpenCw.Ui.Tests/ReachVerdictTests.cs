using OpenCommonwealth;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ReachVerdictTests
{
    [Fact]
    public void A_leg_that_reached_its_goal_without_stalls_passes() =>
        Assert.Null(ReachVerdict.LegFailure(ReachVerdict.Reached, 0));

    [Fact]
    public void A_leg_that_reached_its_goal_only_by_teleporting_past_stalls_fails() =>
        Assert.Contains("2 stall", ReachVerdict.LegFailure(ReachVerdict.Reached, 2));

    [Theory]
    [InlineData("FELL")]
    [InlineData("GAVE UP")]
    [InlineData("TIMEOUT")]
    [InlineData(ReachVerdict.NoGoal)]
    public void Any_other_leg_result_fails_and_names_the_outcome(string result) =>
        Assert.Equal(result, ReachVerdict.LegFailure(result, 0));

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 7, 1)]
    [InlineData(false, 0, 2)]
    [InlineData(false, 3, 2)]
    public void The_exit_code_is_0_for_a_clean_run_1_for_failures_and_2_when_nothing_was_measured(bool compiled, int failures, int expected) =>
        Assert.Equal(expected, ReachVerdict.ExitCode(compiled, failures));
}
