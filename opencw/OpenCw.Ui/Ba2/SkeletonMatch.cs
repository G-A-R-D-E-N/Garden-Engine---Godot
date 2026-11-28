using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Ba2;

public static class SkeletonMatch
{
    public static string? Find(string animEntryPath, IEnumerable<string> skeletonPaths)
    {
        var candidates = Candidates(animEntryPath, skeletonPaths);
        return candidates.Count == 1 ? candidates[0] : null;
    }

    public static List<string> Candidates(string animEntryPath, IEnumerable<string> skeletonPaths)
    {
        string normAnim = animEntryPath.Replace('\\', '/').ToLowerInvariant();
        int required = normAnim.StartsWith("meshes/actors/", StringComparison.Ordinal) ? 3 : 2;
        int best = required - 1, bestBelow = int.MaxValue;
        var found = new List<string>();

        foreach (var path in skeletonPaths)
        {
            string skeleton = path.ToLowerInvariant();
            int shared = SharedDirectories(normAnim, skeleton);
            if (shared < required) continue;
            int below = skeleton.Split('/').Length - 1 - shared;
            if (shared > best || shared == best && below < bestBelow)
            {
                best = shared;
                bestBelow = below;
                found.Clear();
                found.Add(path);
            }
            else if (shared == best && below == bestBelow && !found.Any(f => f.Equals(path, StringComparison.OrdinalIgnoreCase)))
                found.Add(path);
        }

        return found;
    }

    private static int SharedDirectories(string a, string b)
    {
        var left = a.Split('/');
        var right = b.Split('/');
        int limit = Math.Min(left.Length, right.Length) - 1;
        int match = 0;
        while (match < limit && left[match] == right[match]) match++;
        return match;
    }
}
