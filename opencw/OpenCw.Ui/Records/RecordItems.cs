using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed record ItemRecord(string Id, string Signature, string Name, int? Value, double? Weight);

public interface IItemRecords
{
    ItemRecord? Find(string id);
}

public sealed class RecordItems : IItemRecords
{
    public static readonly IReadOnlySet<string> ItemSignatures =
        new HashSet<string>(ItemRecordTypes.Core.Concat(ItemRecordTypes.Extra), StringComparer.Ordinal);

    private readonly IRecordReader _reader;
    private readonly Dictionary<string, ItemRecord?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RecordItems(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public ItemRecord? Find(string id)
    {
        if (_cache.TryGetValue(id, out var cached))
            return cached;
        var found = Read(id);
        _cache[id] = found;
        return found;
    }

    private ItemRecord? Read(string id)
    {
        if (_reader.Signature(id) is not { } signature || !ItemSignatures.Contains(signature))
            return null;
        var (value, weight) = Values(id, signature);
        return new ItemRecord(id, signature, _reader.Name(id) ?? _reader.EditorId(id) ?? id, value, weight);
    }

    private (int? Value, double? Weight) Values(string id, string signature)
    {
        var value = signature == "NOTE" ? Clamped(_reader.Scalar(id, "NOTE\\Value")) : Whole(_reader.Scalar(id, signature + "\\Value"));
        return (value, Real(_reader.Scalar(id, signature + "\\Weight")));
    }

    private static int? Whole(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Clamped(string? text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (int)Math.Min(value, (ulong)int.MaxValue) : null;

    private static double? Real(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
}
