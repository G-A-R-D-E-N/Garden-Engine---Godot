using System.Globalization;
using OpenCommonwealth.Services.Ui.Menus;
using Xunit;

namespace OpenCw.Ui.Tests;

public class NumberTextTests
{
    [Fact]
    public void FormatKeepsDotDecimalUnderCommaCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            Assert.Equal("2.5", NumberText.Format(2.5));
            Assert.Equal("1.23", NumberText.Format(1.234));
            Assert.Equal("3", NumberText.Format(3));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
