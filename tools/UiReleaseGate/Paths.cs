namespace UiReleaseGate;

internal static class Paths
{
    public static bool Within(string path, string root) => path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static string Resolve(string path, int depth = 0)
    {
        if (depth >= 64) throw new InvalidDataException("Filesystem link nesting exceeds limit");
        string full = Path.GetFullPath(path), root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget != null)
            {
                var target = info.ResolveLinkTarget(true) ?? throw new InvalidDataException("Broken filesystem link");
                current = Resolve(target.FullName, depth + 1);
            }
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    public static bool Review(string root, string canonicalRoot, string record, out string path)
    {
        path = "";
        string normalized = record.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Contains(':')
            || normalized.Split('/').Contains("..", StringComparer.Ordinal)) return false;
        try
        {
            path = Resolve(Path.Combine(root, normalized));
            return Within(path, canonicalRoot) && File.Exists(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
