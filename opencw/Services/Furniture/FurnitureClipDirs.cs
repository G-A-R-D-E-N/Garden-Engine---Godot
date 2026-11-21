using Radicle.Interop;

namespace OpenCommonwealth.Services;

public static class FurnitureClipDirs
{
    private const ushort FurnitureRole = 2;
    private const ushort ThirdPerson = 0;
    private const int MaxDepth = 4;

    // Invariant: a row with no actor keyword matches every actor; null keys never match.
    public static IReadOnlyList<string> Directories(IReadOnlyList<RaceSubgraphRow> rows, IReadOnlyCollection<FormKey?> furnitureKeywords, IReadOnlyCollection<FormKey?> actorKeywords)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Role != FurnitureRole || row.Perspective != ThirdPerson) continue;
            if (!row.TargetKeywords.Any(k => k is { } key && furnitureKeywords.Contains(key))) continue;
            if (!row.ActorKeywords.All(k => k is null) && !row.ActorKeywords.Any(k => k is { } key && actorKeywords.Contains(key))) continue;
            foreach (var prefix in row.PathPrefixes)
            {
                var dir = "meshes/" + prefix.Replace('\\', '/').ToLowerInvariant();
                if (seen.Add(dir)) dirs.Add(dir);
            }
        }
        return dirs;
    }

    // Invariant: each race is read once and links stop at MaxDepth, so a cyclic or self link cannot loop.
    public static IReadOnlyList<RaceSubgraphRow> RaceChain(Func<FormKey, (IReadOnlyList<RaceSubgraphRow> Rows, FormKey? Template, IReadOnlyList<FormKey> Additive)> read, FormKey race)
    {
        var rows = new List<RaceSubgraphRow>();
        var visited = new HashSet<FormKey>();
        Visit(race, 1);
        return rows;

        void Visit(FormKey current, int depth)
        {
            if (depth > MaxDepth || !visited.Add(current)) return;
            var (raceRows, template, additive) = read(current);
            rows.AddRange(raceRows);
            foreach (var added in additive) Visit(added, depth + 1);
            if (template is { } parent) Visit(parent, depth + 1);
        }
    }
}
