using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Character;

// Requires: main thread only; Pending is set by the new-game path and cleared once the creation menu is confirmed or cancelled.
public static class CharacterCreation
{
    public const int Pool = 21;
    public const int FallbackBase = 1;

    public static bool Pending { get; set; }

    // Invariant: each value starts at the record base (FallbackBase when the record stores none) and tops out at SpecialMax, so base plus raise never passes the ceiling.
    public static StatAllocationModel Build(Func<string, double?> recordBase)
    {
        var stats = PlayerSpecialRows.Ids.Select(id => new StatDef(id, id, BaseOf(recordBase, id), PlayerExperience.SpecialMax)).ToArray();
        var pool = Math.Min(Pool, stats.Sum(stat => stat.Max - stat.Min));
        return new StatAllocationModel(stats, pool, pool == Pool);
    }

    // Invariant: a confirmed allocation writes value minus base as raises and the name; anything else changes nothing and still ends the pending creation.
    public static bool Commit(StatAllocationModel model, string? name, Func<string, double?> recordBase, PlayerExperience experience, PlayerCharacter character)
    {
        Pending = false;
        if (!model.IsConfirmed) return false;
        var raises = PlayerSpecialRows.Ids.ToDictionary(id => id, id => model.Value(id) - BaseOf(recordBase, id));
        if (!experience.SetSpecialRaises(raises)) return false;
        character.SetName(name);
        return true;
    }

    private static int BaseOf(Func<string, double?> recordBase, string id) =>
        recordBase(id) is { } value && double.IsFinite(value) && value >= 1 ? (int)Math.Min(value, PlayerExperience.SpecialMax) : FallbackBase;
}
