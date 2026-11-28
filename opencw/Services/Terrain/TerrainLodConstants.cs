namespace OpenCommonwealth.Services.Terrain;

public static class TerrainLodConstants
{
    public const int PatchQuads = 32;
    public const int PatchVerts = PatchQuads + 1;

    public const int Apron = 2;
    public const int PageVerts = PatchVerts + 2 * Apron;

    public const int MinDetailLevel = -4;

    public const int DetailMaskSize = 129;

    public const double CellSizeFo4 = 4096.0;
    public const double SampleSpacingFo4 = CellSizeFo4 / PatchQuads;

    public const int AlgorithmVersion = 2;

    public const int LodTextureSize = 512;

    public const float LodNoiseScale = 3.778f;
    public const float LodNoiseBias = 2.006f;
    public const double LodNoisePerUnit = 0.00025;
    public const double LodDetailNormalsPerUnit = 0.00035;
    public const double LodMatchNear = 6 * CellSizeFo4;
    public const double LodMatchFar = 25 * CellSizeFo4;

    public static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }
}
