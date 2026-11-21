using OpenCommonwealth.Services.Ui.Terminals;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TerminalRowsTests
{
    [Theory]
    [InlineData(0, 5, 0, 5, true)]
    [InlineData(2, 12, 2, 12, true)]
    [InlineData(0, 5, 1, 5, false)]
    [InlineData(0, 5, 0, 6, false)]
    [InlineData(-1, 0, 0, 0, false)]
    public void Rows_are_kept_only_while_the_visible_window_is_the_same(int shownTop, int shownCount, int top, int count, bool expected) =>
        Assert.Equal(expected, TerminalRows.CanReuse(shownTop, shownCount, top, count));
}
