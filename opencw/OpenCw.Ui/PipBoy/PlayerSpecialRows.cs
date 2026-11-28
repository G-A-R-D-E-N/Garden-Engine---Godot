using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PlayerSpecialRows
{
    public static readonly IReadOnlyList<string> Ids =
        ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];

    // Invariant: all seven must be stored and positive, otherwise none show, so the sheet is never partial; the AVIF editor id stands in for the name until RecordPlayerStatusSource resolves the record.
    public static IReadOnlyList<SpecialEntry> Build(Func<string, double?> stored)
    {
        var rows = new List<SpecialEntry>(Ids.Count);
        foreach (var id in Ids)
        {
            if (stored(id) is not { } value || !double.IsFinite(value) || value <= 0) return [];
            rows.Add(new SpecialEntry(id, id, value));
        }
        return rows;
    }

    public static IReadOnlyList<SpecialEntry> WithSpent(IReadOnlyList<SpecialEntry> stored, IReadOnlyList<int> spent)
    {
        if (stored.Count != Ids.Count || spent.Count != Ids.Count) return stored;
        var rows = new SpecialEntry[stored.Count];
        for (var i = 0; i < rows.Length; i++)
            rows[i] = stored[i] with { Value = Math.Clamp(Math.Round(stored[i].Value) + spent[i], 1, 10) };
        return rows;
    }
}
