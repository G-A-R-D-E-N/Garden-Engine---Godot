using System;

namespace OpenCommonwealth;

public static class ScenePathFilter
{
    public static bool IsExteriorCell(string? path) =>
        !string.IsNullOrEmpty(path) && !path.Contains("/persistent/", StringComparison.Ordinal);
}
