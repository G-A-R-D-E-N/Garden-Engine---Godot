using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus;

/// <summary>
/// Which payload wins when several sources provide the same script.
/// </summary>
/// <remarks>
/// The rule is a total order, so the winner never depends on enumeration order: a loose script
/// beats an archive script, a higher load index beats a lower one, and an ordinal container-path
/// comparison breaks any remaining tie. Script names are compared case-insensitively after
/// normalization, because Papyrus script names are not case sensitive.
/// </remarks>
public static class ScriptCachePrecedence
{
    /// <summary>Lower-cases a script path and drops the extension, so keys are comparable.</summary>
    public static string NormalizeName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith(ScriptCacheLayout.PexExtension, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^ScriptCacheLayout.PexExtension.Length];
        }
        return normalized.ToLowerInvariant();
    }

    public static int Compare(ScriptSourceCandidate left, ScriptSourceCandidate right)
    {
        var byKind = left.Kind.CompareTo(right.Kind);
        if (byKind != 0)
        {
            return byKind;
        }
        var byIndex = left.LoadIndex.CompareTo(right.LoadIndex);
        if (byIndex != 0)
        {
            return byIndex;
        }
        return string.CompareOrdinal(left.ContainerPath, right.ContainerPath);
    }

    public static ScriptSourceCandidate? PickWinner(IReadOnlyList<ScriptSourceCandidate> candidates)
    {
        ScriptSourceCandidate? winner = null;
        foreach (var candidate in candidates)
        {
            if (winner is null || Compare(candidate, winner) > 0)
            {
                winner = candidate;
            }
        }
        return winner;
    }

    /// <summary>
    /// Resolves every script name to its winner, ordered by name, with the number of losing
    /// candidates each winner replaced.
    /// </summary>
    public static IReadOnlyList<(string Name, ScriptSourceCandidate Winner, int Replaced)> Resolve(
        IEnumerable<ScriptSourceCandidate> candidates)
    {
        var groups = candidates
            .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var resolved = new List<(string, ScriptSourceCandidate, int)>();
        foreach (var group in groups)
        {
            var list = group.ToList();
            var winner = PickWinner(list);
            if (winner is null)
            {
                continue;
            }
            resolved.Add((group.Key, winner, list.Count - 1));
        }
        return resolved;
    }
}
