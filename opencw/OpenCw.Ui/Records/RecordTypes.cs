using System;
using System.Collections.Generic;
using System.Globalization;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed record AliasDisplay(string? Reference, string Display);

public interface IRecordReader
{
    string? Signature(string id);

    string? EditorId(string id);

    string? Name(string id);

    string? Scalar(string id, string path);

    string? Text(string id, string path);

    IReadOnlyList<string>? List(string id, string path);

    string? ResolveFormId(string id, uint rawFormId);

    IReadOnlyList<string> FormLinks(string id, string subrecord);

    IReadOnlyList<RecordCondition>? RecipeConditions(string id);

    int? ConditionCount(string id);

    LockData? Lock(string id);

    IReadOnlyList<string> Winners(string signature);

    string? FindEditorId(string editorId, string? signature = null);

    IReadOnlyList<AliasDisplay> AliasDisplays(string questId);

    IReadOnlyList<string> WorldspaceMapMarkers(string worldspaceId);

    // Invariant: counts the reads that failed and were answered as null or empty; a caller that sees it change across an operation knows that answer is unknown, not absent.
    long FailureCount => 0;

    // Invariant: the plain text of a BOOK record, or null when it has none or the record is not a BOOK.
    string? BookText(string id) => null;
}

public static class RecordKey
{
    public static string Format(string plugin, uint localId) =>
        string.Create(CultureInfo.InvariantCulture, $"{localId:X6}:{plugin}");

    public static string Format(FormKey key) => Format(key.Plugin, key.LocalId);

    public static string Canonical(string id) =>
        TryParse(id, out var plugin, out var localId) ? Format(plugin.ToLowerInvariant(), localId) : id;

    public static bool TryParse(string? id, out string plugin, out uint localId)
    {
        plugin = string.Empty;
        localId = 0;
        if (id is null)
            return false;
        var colon = id.IndexOf(':');
        if (colon <= 0 || colon == id.Length - 1
            || !uint.TryParse(id.AsSpan(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out localId))
            return false;
        plugin = id[(colon + 1)..];
        return true;
    }
}
