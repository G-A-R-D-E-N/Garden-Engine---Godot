using System.IO;

namespace OpenCommonwealth.Services;

public static class WorldspaceCellRetry
{
    public const string CellsFolder = "cells";

    public static string CellStem(int gx, int gy) => $"cell_{gx}_{gy}";

    public static string FailureMarkerPath(string wsDirAbs, int gx, int gy) =>
        Path.Combine(wsDirAbs, CellsFolder, $"{CellStem(gx, gy)}.failed");

    public static bool IsUpToDate(string wsDirAbs, int gx, int gy)
    {
        var sceneAbs = Path.Combine(wsDirAbs, CellsFolder, $"{CellStem(gx, gy)}.tscn");
        if (!File.Exists(sceneAbs) || new FileInfo(sceneAbs).Length == 0) return false;
        return !File.Exists(FailureMarkerPath(wsDirAbs, gx, gy));
    }
}