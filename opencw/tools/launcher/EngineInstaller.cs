namespace OpenCommonwealth.Launcher;

internal static class EngineInstaller
{
    internal const string NestedName = "GardenGodot";
    internal const string ReleaseMarker = ".engine-release";

    internal static bool Owned(string dir) =>
        !Directory.Exists(dir)
        || !Directory.EnumerateFileSystemEntries(dir).Any()
        || File.Exists(Path.Combine(dir, ReleaseMarker))
        || File.Exists(Path.Combine(dir, ".installed-from"));

    internal static string? ResolveDestination(string dir, out string? error)
    {
        error = null;
        if (Owned(dir)) return dir;
        string nested = Path.Combine(dir, NestedName);
        if (Owned(nested)) return nested;
        error = $"{nested} already exists and isn't a Garden engine install; choose another folder.";
        return null;
    }

    internal static string UniqueSibling(string dir, string kind)
    {
        string basePath = dir.TrimEnd('\\', '/');
        string candidate;
        do candidate = $"{basePath}.{kind}-{Guid.NewGuid():N}";
        while (Directory.Exists(candidate) || File.Exists(candidate));
        return candidate;
    }

    internal static string? Swap(string staging, string dest)
    {
        if (!Owned(dest)) return $"{dest} isn't a Garden engine install, so it was left alone.";
        string? previous = null;
        try
        {
            if (Directory.Exists(dest))
            {
                if (Directory.EnumerateFileSystemEntries(dest).Any())
                {
                    previous = UniqueSibling(dest, "previous");
                    Directory.Move(dest, previous);
                }
                else Directory.Delete(dest);
            }
            Directory.Move(staging, dest);
        }
        catch (Exception ex)
        {
            if (previous is not null && Directory.Exists(previous))
            {
                if (Directory.Exists(dest) || File.Exists(dest)) return $"couldn't swap in the new engine: {ex.Message}. Previous engine remains at {previous}; restore it manually.";
                try { Directory.Move(previous, dest); }
                catch (Exception rollback) { return $"couldn't swap in the new engine: {ex.Message}. Previous engine remains at {previous}; restore it manually: {rollback.Message}"; }
            }
            return "couldn't swap in the new engine (is the editor still running?): " + ex.Message;
        }
        if (previous is not null) try { Directory.Delete(previous, true); } catch { }
        return null;
    }
}
