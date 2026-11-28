using OpenCommonwealth.Services.Ui.Streaming;
using Xunit;

public class FallbackBudgetTests
{
    [Theory]
    [InlineData(false, true, true, 0, FallbackDecision.Discard)]
    [InlineData(true, false, true, 0, FallbackDecision.Discard)]
    [InlineData(true, true, false, 0, FallbackDecision.Discard)]
    [InlineData(true, true, true, 0, FallbackDecision.Retry)]
    [InlineData(true, true, true, 1, FallbackDecision.Defer)]
    [InlineData(false, true, true, 1, FallbackDecision.Discard)]
    public void DecisionsRespectResidencyEpochRangeAndBudget(bool current, bool epoch, bool wanted, int used, FallbackDecision expected)
        => Assert.Equal(expected, FallbackBudget.Decide(current, epoch, wanted, used));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void FailedThreadedSyncHonoursFlag(bool flag, bool attempt) =>
        Assert.Equal(attempt, FallbackBudget.AttemptFailedThreadedSync(flag));
}
