using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Terrain;

public static class TerrainLodPartial
{
    public const string ReplaceFlag = "--terrain-lod-replace-partial";

    public static bool CheckUnread(IReadOnlyCollection<(int X, int Y)> unread, bool hasPrevious, bool replacePartial, Action<string> log)
    {
        if (unread.Count == 0) return true;
        var cells = string.Join(", ", unread.OrderBy(c => c.Y).ThenBy(c => c.X).Take(10).Select(c => $"({c.X},{c.Y})"))
                    + (unread.Count > 10 ? ", ..." : "");
        if (replacePartial)
        {
            log($"  terrain LOD: {unread.Count} LAND cells could not be read ({cells}); {ReplaceFlag} bakes them flat at the default height.");
            return true;
        }
        if (hasPrevious)
            throw new InvalidDataException($"{unread.Count} LAND cells could not be read ({cells}); kept the previous terrain LOD (which may itself be partial). " +
                                           $"Pass {ReplaceFlag} to replace it with one where they are flat.");
        log($"  terrain LOD: {unread.Count} LAND cells could not be read ({cells}); there is no previous terrain LOD to keep, " +
            "so they bake flat at the default height and the run fails.");
        return false;
    }

    public static void CheckKept(string what, string reason, bool previousHas, bool replacePartial, Exception? failure = null)
    {
        if (previousHas && !replacePartial)
            throw new InvalidDataException($"this bake has no {what} ({reason}); kept the previous terrain LOD and its {what}. " +
                                           $"Pass {ReplaceFlag} to bake without them.", failure);
    }
}
