using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Combat;

public static class HeldWeaponMount
{
    // Invariant: UNVERIFIED candidate names, in order; the bone that is returned always exists in the rig it was picked from.
    public static readonly IReadOnlyList<string> HandBones = ["WEAPON", "Weapon", "RArm_Hand", "Hand_R", "RHand"];

    public static string? HoldBone(IEnumerable<string> boneNames)
    {
        var bones = boneNames.ToArray();
        foreach (var candidate in HandBones)
            if (bones.FirstOrDefault(bone => string.Equals(bone, candidate, StringComparison.OrdinalIgnoreCase)) is { } found) return found;
        return null;
    }

    // Invariant: the suffix after the last underscore of an attach point keyword editor id; null when there is none.
    public static string? SuffixOf(string? attachPointEditorId)
    {
        if (string.IsNullOrWhiteSpace(attachPointEditorId)) return null;
        var suffix = attachPointEditorId[(attachPointEditorId.LastIndexOf('_') + 1)..].Trim();
        return suffix.Length == 0 ? null : suffix;
    }

    // Invariant: prefers the node named P-<suffix>, then a P- node ending with the suffix; null means mount the part at the weapon root.
    public static string? ModMount(IEnumerable<string> nodeNames, string? suffix)
    {
        if (suffix is null) return null;
        var nodes = nodeNames.Where(name => name.StartsWith("P-", StringComparison.OrdinalIgnoreCase)).ToArray();
        return nodes.FirstOrDefault(name => string.Equals(name, "P-" + suffix, StringComparison.OrdinalIgnoreCase))
            ?? nodes.FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
