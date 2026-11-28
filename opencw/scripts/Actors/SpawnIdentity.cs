using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;

namespace OpenCommonwealth;

public readonly record struct SpawnChoice(string NpcKey, string BaseType, bool Leveled);

// Invariant: no Godot types and no record reads here, so the placed-versus-chosen decision stays unit-testable.
public static class SpawnIdentity
{
    public const string LeveledType = "LeveledNpc";

    // Requires: a pinned choice wins over a fresh pick; null means a leveled marker gave no NPC_ and spawns no puppet; a leveled marker with no pin and no pick seam keeps its list key and type.
    public static SpawnChoice? Choose(string baseType, string baseKey, string? pinned, Func<string?>? pick)
    {
        if (baseType != LeveledType) return new SpawnChoice(baseKey, baseType, false);
        string? chosen = pinned is { Length: > 0 } ? pinned : pick?.Invoke();
        if (chosen is null && pick is null) return new SpawnChoice(baseKey, baseType, false);
        return chosen is { Length: > 0 } ? new SpawnChoice(chosen, "Npc", true) : null;
    }

    public static string KeyOf(string baseKey, string? resolved) => string.IsNullOrEmpty(resolved) ? baseKey : resolved;

    // Requires: the reference's own attachments win over a chosen attachment of the same name, because one owner cannot hold a script twice; null when nothing remains.
    public static string? MergeScripts(string? ownMetadata, IReadOnlyList<ScriptAttachment> chosen)
    {
        var own = ScriptAttachments.Parse(ownMetadata);
        var merged = chosen.Where(script => !own.Any(item => string.Equals(item.Name, script.Name, StringComparison.OrdinalIgnoreCase))).Concat(own).ToList();
        return merged.Count == 0 ? null : ScriptAttachments.ToMetadataJson(merged);
    }
}
