using System;
using System.Numerics;

namespace OpenCommonwealth.Services.Terrain;

public enum LodTextureFormat { Bc1 = 1, Bc3 = 3 }

public static partial class LodTextureDds
{
    public readonly record struct Info(int Width, int Height, int Mips, LodTextureFormat Format, int DataOffset);

    public static int BlockBytes(LodTextureFormat format) => format == LodTextureFormat.Bc1 ? 8 : 16;

    public static int MipBytes(int width, int height, int mip, LodTextureFormat format)
    {
        int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
        return ((w + 3) / 4) * ((h + 3) / 4) * BlockBytes(format);
    }

    public static int ChainBytes(int width, int height, int mips, LodTextureFormat format)
    {
        int total = 0;
        for (int m = 0; m < mips; m++) total += MipBytes(width, height, m, format);
        return total;
    }

    public static int FullMipCount(int width, int height) => BitOperations.Log2((uint)Math.Max(width, height)) + 1;

    public static bool TryLayerOffset(Info info, int size, out int offset)
    {
        offset = 0;
        int skip = info.Width >= size ? BitOperations.Log2((uint)(info.Width / size)) : -1;
        if (skip < 0 || info.Height != info.Width || size << skip != info.Width || info.Mips < skip + FullMipCount(size, size))
            return false;
        offset = info.DataOffset + ChainBytes(info.Width, info.Height, skip, info.Format);
        return true;
    }

    public static Vector3 MeanLinear(ReadOnlySpan<byte> rgb) => Mean(rgb, Srgb.ByteToLinear);

    public static Vector3 MeanNoise(ReadOnlySpan<byte> rgb) =>
        Mean(rgb, b => MathF.Max(b / 255f * TerrainLodConstants.LodNoiseScale - TerrainLodConstants.LodNoiseBias, 0f));

    private static Vector3 Mean(ReadOnlySpan<byte> rgb, Func<byte, float> channel)
    {
        var sum = Vector3.Zero;
        int texels = rgb.Length / 3;
        for (int n = 0; n < texels; n++) sum += new Vector3(channel(rgb[n * 3]), channel(rgb[n * 3 + 1]), channel(rgb[n * 3 + 2]));
        return texels > 0 ? sum / texels : Vector3.Zero;
    }
}
