namespace OpenCommonwealth.Services;

public sealed record FurnitureClips(string? Enter, string? Pose, string? Exit, string? QuickExit)
{
    // Requires: each element of sides is a single side flag (Front, Rear, Right or Left); other values are skipped.
    public static FurnitureClips Pick(IReadOnlyList<string> dirs, Func<string, IReadOnlyCollection<string>> list, IReadOnlyList<FurnitureSides> sides)
    {
        var prefixes = sides.Select(SidePrefix).OfType<string>().Append(string.Empty).ToList();
        return new FurnitureClips(
            Find(dirs, list, prefixes, "EnterFromStand"),
            Find(dirs, list, new[] { string.Empty }, "PoseA_Idle1"),
            Find(dirs, list, prefixes, "ExitToStand"),
            Find(dirs, list, prefixes, "QuickExitToStand"));
    }

    public IEnumerable<(string Entry, bool Loop)> Entries()
    {
        foreach (var (entry, loop) in new[] { (Enter, false), (Pose, true), (Exit, false), (QuickExit, false) })
            if (entry is not null) yield return (entry, loop);
    }

    public static IReadOnlyCollection<string> DirectFiles(string dir, IEnumerable<string> paths)
    {
        var prefix = dir.TrimEnd('/') + "/";
        return paths.Select(p => p.Replace('\\', '/'))
            .Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Length > prefix.Length && p.IndexOf('/', prefix.Length) < 0)
            .Select(p => p[prefix.Length..])
            .ToList();
    }

    public static IReadOnlyList<FurnitureSides> SidesOf(FurnitureSides sides) =>
        new[] { FurnitureSides.Left, FurnitureSides.Right, FurnitureSides.Front, FurnitureSides.Rear }.Where(side => sides.HasFlag(side)).ToList();

    private static string? SidePrefix(FurnitureSides side) => side switch
    {
        FurnitureSides.Front => "Front",
        FurnitureSides.Rear => "Rear",
        FurnitureSides.Right => "Right",
        FurnitureSides.Left => "Left",
        _ => null,
    };

    private static string? Find(IReadOnlyList<string> dirs, Func<string, IReadOnlyCollection<string>> list, IReadOnlyList<string> prefixes, string role)
    {
        foreach (var dir in dirs)
        {
            var files = list(dir);
            foreach (var prefix in prefixes)
            {
                var file = files.FirstOrDefault(f => string.Equals(f, prefix + role + ".hkx", StringComparison.OrdinalIgnoreCase));
                if (file is not null) return $"{dir}/{file}";
            }
        }
        return null;
    }
}
