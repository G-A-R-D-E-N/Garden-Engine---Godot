using System.Buffers.Binary;
using System.Numerics;
using OpenCommonwealth.Services.Terrain;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class LodTextureDecodeTests
{
    private const ushort Red = 0xF800, Blue = 0x001F, Green = 0x07E0, Olive = 0x8C40, White = 0xFFFF;

    private static byte[] Block(LodTextureFormat format, ushort c0, ushort c1, uint indices)
    {
        var block = new byte[LodTextureDds.BlockBytes(format)];
        var colour = block.AsSpan(block.Length - 8);
        BinaryPrimitives.WriteUInt16LittleEndian(colour, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(colour[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(colour[4..], indices);
        return block;
    }

    private static byte[] Texel(byte[] rgb, int width, int x, int y) => rgb.AsSpan((y * width + x) * 3, 3).ToArray();

    [Fact]
    public void Bc1_ReadsEveryTexelOfEachBlock()
    {
        var mip = Block(LodTextureFormat.Bc1, Red, Blue, 0).Concat(Block(LodTextureFormat.Bc1, Red, Blue, 0x55555555)).ToArray();
        var rgb = LodTextureDds.DecodeRgb(mip, 8, 4, LodTextureFormat.Bc1);
        Assert.Equal(8 * 4 * 3, rgb.Length);
        Assert.Equal(new byte[] { 255, 0, 0 }, Texel(rgb, 8, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0 }, Texel(rgb, 8, 3, 3));
        Assert.Equal(new byte[] { 0, 0, 255 }, Texel(rgb, 8, 4, 0));
        Assert.Equal(new byte[] { 0, 0, 255 }, Texel(rgb, 8, 5, 2));
    }

    [Fact]
    public void Bc3_ReadsTheColourHalfOfTheBlock()
    {
        var rgb = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc3, Green, Red, 0x40000000u), 4, 4, LodTextureFormat.Bc3);
        Assert.Equal(new byte[] { 0, 255, 0 }, Texel(rgb, 4, 0, 0));
        Assert.Equal(new byte[] { 255, 0, 0 }, Texel(rgb, 4, 3, 3));
    }

    [Fact]
    public void Bc1_ClipsABlockThatOverhangsTheMip()
    {
        var rgb = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, Red, Blue, 1u << 8), 2, 2, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 0, 0, 255, 255, 0, 0 }, rgb);
    }

    [Fact]
    public void Bc1_Reads1x1Mip()
    {
        Assert.Equal(new byte[] { 0, 0, 255 }, LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, Red, Blue, 1u), 1, 1, LodTextureFormat.Bc1));
    }

    [Fact]
    public void Bc1_Reads1x2MipWithoutRowPadding()
    {
        var rgb = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, Red, Blue, 1u << 8), 1, 2, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255 }, rgb);
    }

    [Fact]
    public void Bc3_Reads1x1Mip()
    {
        Assert.Equal(new byte[] { 255, 0, 0 }, LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc3, Green, Red, 1u), 1, 1, LodTextureFormat.Bc3));
    }

    [Fact]
    public void Bc3_Reads2x1MipWithoutRowPadding()
    {
        var rgb = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc3, Green, Red, 1u | 1u << 8), 2, 1, LodTextureFormat.Bc3);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 255, 0 }, rgb);
    }

    [Fact]
    public void Bc3_ClipsABlockThatOverhangsTheMip()
    {
        var rgb = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc3, Green, Red, 1u << 8), 2, 2, LodTextureFormat.Bc3);
        Assert.Equal(new byte[] { 0, 255, 0, 0, 255, 0, 255, 0, 0, 0, 255, 0 }, rgb);
    }

    [Fact]
    public void InterpolatedColours_RoundLikeRadicle()
    {
        var third = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, Olive, 0, 0xFFFFFFFFu), 4, 4, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 47, 46, 0 }, Texel(third, 4, 0, 0));
        var twoThirds = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, White, Olive, 0xAAAAAAAAu), 4, 4, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 217, 216, 170 }, Texel(twoThirds, 4, 2, 1));
    }

    [Fact]
    public void Bc1_ThreeColourBlockMidpointsAndBlack_ButBc3ColourIsAlwaysFourColour()
    {
        var bc1 = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, 0, White, 0xAAAAAAAAu), 4, 4, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 128, 128, 128 }, Texel(bc1, 4, 0, 0));
        var black = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc1, 0, White, 0xFFFFFFFFu), 4, 4, LodTextureFormat.Bc1);
        Assert.Equal(new byte[] { 0, 0, 0 }, Texel(black, 4, 0, 0));
        var bc3 = LodTextureDds.DecodeRgb(Block(LodTextureFormat.Bc3, 0, White, 0xAAAAAAAAu), 4, 4, LodTextureFormat.Bc3);
        Assert.Equal(new byte[] { 85, 85, 85 }, Texel(bc3, 4, 0, 0));
    }

    [Fact]
    public void MeanLinear_DecodesTheColourHalfOfBc3Blocks()
    {
        var mip = Block(LodTextureFormat.Bc3, Red, 0, 0).Concat(Block(LodTextureFormat.Bc3, 0, White, 0x55555555u)).ToArray();
        Assert.Equal(new Vector3(1f, 0.5f, 0.5f), LodTextureDds.MeanLinear(mip, 8, 4, LodTextureFormat.Bc3));
    }

    [Fact]
    public void MeanNoise_DecodesBc1AndTakesTheMeanOfFo4sRemap()
    {
        var mip = Block(LodTextureFormat.Bc1, White, 0, 0).Concat(Block(LodTextureFormat.Bc1, 0, 0, 0)).ToArray();
        var mean = LodTextureDds.MeanNoise(mip, 8, 4, LodTextureFormat.Bc1);
        float white = TerrainLodConstants.LodNoiseScale - TerrainLodConstants.LodNoiseBias;
        Assert.Equal(white / 2, mean.X, 5);
        Assert.Equal(mean.X, mean.Y);
        Assert.Equal(mean.X, mean.Z);
    }
}
