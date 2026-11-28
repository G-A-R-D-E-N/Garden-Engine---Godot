using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PapyrusIntegerTests
{
    [Theory]
    [InlineData(2147483648L, int.MinValue)]
    [InlineData(-2147483649L, int.MaxValue)]
    [InlineData(4294967296L, 0)]
    [InlineData(4294967297L, 1)]
    [InlineData(long.MaxValue, -1)]
    [InlineData(long.MinValue, 0)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue)]
    [InlineData(-1L, -1)]
    public void FromIntKeepsOnlyTheLow32BitsAsASignedInteger(long input, int expected)
    {
        var value = PapyrusValue.FromInt(input);

        Assert.Equal(PapyrusValueKind.Int, value.Kind);
        Assert.Equal((long)expected, value.Integer);
    }

    [Fact]
    public void WrappedValuesCompareAndPrintAsTheirInt32()
    {
        Assert.Equal(PapyrusValue.FromInt(int.MinValue), PapyrusValue.FromInt((long)int.MaxValue + 1));
        Assert.Equal("-2147483648", PapyrusValue.FromInt((long)int.MaxValue + 1).ToString());
    }
}
