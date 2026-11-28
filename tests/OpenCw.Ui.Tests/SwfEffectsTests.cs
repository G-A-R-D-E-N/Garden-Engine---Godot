using OpenCommonwealth.Services.Ui.Swf;
using Xunit;

namespace OpenCw.Ui.Tests;

public class SwfEffectsTests
{
    [Fact]
    public void NoneHasNoTintOffsetOrBlend()
    {
        Assert.False(SwfEffects.None.HasTint);
        Assert.False(SwfEffects.None.HasOffset);
        Assert.Equal(SwfBlend.Mix, SwfEffects.None.Blend);
    }

    [Fact]
    public void TintAndOffsetComeFromTheColourTransform()
    {
        var fx = new SwfEffects(true, new[] { 128, 256, 256, 256 }, new[] { 0, 51, 0, 0 }, null, Array.Empty<SwfFilter>());
        Assert.True(fx.HasTint);
        Assert.Equal(0.5f, fx.Tint.R);
        Assert.True(fx.HasOffset);
        Assert.Equal(0.2f, fx.Offset.G, 3);
    }

    [Theory]
    [InlineData((byte)3, SwfBlend.Mul)]
    [InlineData((byte)8, SwfBlend.Add)]
    [InlineData((byte)4, SwfBlend.Add)]
    [InlineData((byte)9, SwfBlend.Sub)]
    [InlineData((byte)1, SwfBlend.Mix)]
    [InlineData((byte)14, SwfBlend.Mix)]
    public void BlendModesMapToGodotBlends(byte mode, SwfBlend expected) =>
        Assert.Equal(expected, new SwfEffects(false, new[] { 256, 256, 256, 256 }, new[] { 0, 0, 0, 0 }, mode, Array.Empty<SwfFilter>()).Blend);

    [Fact]
    public void LeafInheritsBlendAndFiltersButKeepsItsOwnColour()
    {
        var shadow = new SwfFilter(SwfFilter.DropShadow, false, false, 1, 0xFF000000, 2, 2, 0, 3, 1);
        var above = new SwfEffects(true, new[] { 10, 10, 10, 10 }, new[] { 0, 0, 0, 0 }, 8, new[] { shadow });
        var leaf = SwfEffects.None.Inherit(above);
        Assert.Equal(SwfBlend.Add, leaf.Blend);
        Assert.Single(leaf.Filters);
        Assert.False(leaf.HasTint);
        var own = new SwfEffects(false, new[] { 256, 256, 256, 256 }, new[] { 0, 0, 0, 0 }, 3, Array.Empty<SwfFilter>()).Inherit(above);
        Assert.Equal(SwfBlend.Mul, own.Blend);
    }
}
