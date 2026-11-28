using System;
using System.Linq;

namespace OpenCommonwealth.Services.Terrain;

public static class Srgb
{
    private static readonly float[] ByteToLinearTable =
        Enumerable.Range(0, 256).Select(b => (float)ToLinear(b / 255.0)).ToArray();

    private static readonly float[] Thresholds =
        Enumerable.Range(0, 255).Select(k => (float)ToLinear((k + 0.5) / 255.0)).ToArray();

    private const int Cells = 8192;
    private static readonly byte[] CellStart = BuildCellStart();

    public static double ToLinear(double srgb) =>
        srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);

    public static double FromLinear(double linear) =>
        linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;

    public static float ToLinear(float srgb) =>
        srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);

    public static float FromLinear(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

    public static float ByteToLinear(byte b) => ByteToLinearTable[b];

    public static byte LinearToByte(float linear)
    {
        if (!(linear > 0f)) return 0;
        if (linear >= 1f) return 255;
        byte b = CellStart[(int)(linear * Cells)];
        return b < 255 && Thresholds[b] < linear ? (byte)(b + 1) : b;
    }

    private static byte[] BuildCellStart()
    {
        var table = new byte[Cells + 1];
        for (int k = 0; k <= Cells; k++)
        {
            int b = Array.BinarySearch(Thresholds, k / (float)Cells);
            table[k] = (byte)(b >= 0 ? b : ~b);
            if (k > 0 && table[k] - table[k - 1] > 1) throw new InvalidOperationException("sRGB table step too coarse");
        }
        return table;
    }
}
