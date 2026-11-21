using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workbench;

namespace OpenCommonwealth.Services.Ui.Trade;

// Constraint: the selection rule is UNVERIFIED game behaviour (docs/code-notes/opencw-instance-items.md); results are cached per id, so build a new instance when the record environment changes.
public sealed class DefaultItemMods(ObjectModRules? rules = null)
{
    private const string Prefix = "ObjectTemplate";

    private readonly Dictionary<string, ItemState> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Invariant: Plain for an id that is not an ARMO or WEAP, has no Default combination or unreadable rows; otherwise the includes of the first Default combination.
    public ItemState For(IRecordReader reader, string id)
    {
        if (!(rules ?? ObjectModRules.Current).ApplyDefaultMods) return ItemState.Plain;
        if (_cache.TryGetValue(id, out var state)) return state;
        var before = reader.FailureCount;
        state = Read(reader, id);
        if (reader.FailureCount == before) _cache[id] = state;
        return state;
    }

    private static ItemState Read(IRecordReader reader, string id)
    {
        var prefix = reader.Signature(id) switch { "ARMO" => "ARMO\\", "WEAP" => "WEAP\\", _ => null };
        if (prefix is null
            || reader.List(id, prefix + Prefix + "Default") is not { } flags
            || reader.List(id, prefix + Prefix + "IncludeCount") is not { } counts || counts.Count != flags.Count
            || reader.List(id, prefix + Prefix + "IncludeMod") is not { } words) return ItemState.Plain;
        var first = flags.ToList().FindIndex(IsSet);
        if (first < 0 || !TryCount(counts[first], out var take)) return ItemState.Plain;
        var skip = 0;
        for (var i = 0; i < first; i++)
        {
            if (!TryCount(counts[i], out var before)) return ItemState.Plain;
            skip += before;
        }
        if (skip + take > words.Count) return ItemState.Plain;
        var mods = words.Skip(skip).Take(take)
            .Select(word => uint.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ? reader.ResolveFormId(id, raw) : null)
            .OfType<string>().Where(mod => reader.Signature(mod) == RecordBenchRecipes.ModSignature)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return new ItemState(mods);
    }

    private static bool IsSet(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value != 0 : string.Equals(text, "True", StringComparison.OrdinalIgnoreCase);

    private static bool TryCount(string text, out int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out count);
}
