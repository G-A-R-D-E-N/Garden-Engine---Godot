using System;
using System.Buffers.Binary;
using System.Numerics;
using DdsDoor = Radicle.Interop.DdsImage;

namespace OpenCommonwealth.Services.Terrain;

public static partial class LodTextureDds
{
    private const int LegacyHeaderBytes = 128;

    public static byte[] DecodeRgb(ReadOnlySpan<byte> mip, int width, int height, LodTextureFormat format)
    {
        int bytes = MipBytes(width, height, 0, format);
        var file = new byte[LegacyHeaderBytes + bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x20534444);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(80), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(84), format == LodTextureFormat.Bc1 ? 0x31545844u : 0x35545844u);
        mip[..bytes].CopyTo(file.AsSpan(LegacyHeaderBytes));
        var rgba = DdsDoor.DecodeRgba(file).Rgba;
        if (rgba.Length != width * height * 4)
            throw new InvalidOperationException($"DecodeRgba returned {rgba.Length} bytes for a {width}x{height} mip; expected {width * height * 4}.");
        var rgb = new byte[width * height * 3];
        for (int n = 0; n < width * height; n++)
        {
            rgb[n * 3] = rgba[n * 4];
            rgb[n * 3 + 1] = rgba[n * 4 + 1];
            rgb[n * 3 + 2] = rgba[n * 4 + 2];
        }
        return rgb;
    }

    public static Vector3 MeanLinear(ReadOnlySpan<byte> mip, int width, int height, LodTextureFormat format) =>
        MeanLinear(DecodeRgb(mip, width, height, format));

    public static Vector3 MeanNoise(ReadOnlySpan<byte> mip, int width, int height, LodTextureFormat format) =>
        MeanNoise(DecodeRgb(mip, width, height, format));
}
