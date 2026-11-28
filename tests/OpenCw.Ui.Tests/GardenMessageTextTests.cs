using OpenCommonwealth.UI.Own;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class GardenMessageTextTests
{
    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("one<br>two", "one\ntwo")]
    [InlineData("one<br/>two", "one\ntwo")]
    [InlineData("one<br />two", "one\ntwo")]
    [InlineData("A<p>B</p>C", "A\n\nB\n\nC")]
    [InlineData("<b>bold</b> and <i>italic</i>", "bold and italic")]
    [InlineData("<font color='#ff0000'>red</font>", "red")]
    [InlineData("<font size=24>big</font>", "big")]
    [InlineData("<span class=\"x\">kept</span>", "<span class=\"x\">kept</span>")]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("&lt;b&gt;", "<b>")]
    [InlineData("line1\r\nline2\rline3", "line1\nline2\nline3")]
    [InlineData("5 < 6 and 7 > 3", "5 < 6 and 7 > 3")]
    public void ToPlainTextFlattensTheSupportedMarkup(string input, string expected) =>
        Assert.Equal(expected, GardenMessageText.ToPlainText(input));
}
