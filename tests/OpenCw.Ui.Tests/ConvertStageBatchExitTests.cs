using OpenCommonwealth.Services;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ConvertStageBatchExitTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(3, 0, 1)]
    [InlineData(2, 10, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(50, 100, 0)]
    public void Exit_depends_on_new_successes_not_existing_outputs(long failed, long newlySucceeded, int expected) =>
        Assert.Equal(expected, ConvertStageBatchExit.ExitCode(failed, newlySucceeded));
}
