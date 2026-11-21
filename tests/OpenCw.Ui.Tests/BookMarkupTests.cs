using OpenCommonwealth.Services.Ui.Records;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class BookMarkupTests
{
    [Fact]
    public void TagsAreDroppedEntitiesDecodedAndPageBreaksBecomeBlankLines()
    {
        var plain = BookMarkup.Plain("<font size='20'>Title</font><br>Line &amp; more\r\n[pagebreak]\r\nPage two\0");
        Assert.Equal("Title\nLine & more\n\nPage two", plain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("[pagebreak]")]
    public void NothingReadableIsNull(string? markup) => Assert.Null(BookMarkup.Plain(markup));

    [Fact]
    public void LongBlankRunsCollapseToOneBlankLine()
    {
        Assert.Equal("a\n\nb", BookMarkup.Plain("a\n\n\n\n\nb"));
    }
}
