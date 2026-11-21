using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Constraint: a record name is the raw FULL bytes from StringTables decoded as Windows-1252, as GameLoader.RadicleName does, or the inline name text when no table row resolves.
public sealed class RadicleRecordReader : IRecordReader
{
    private readonly IRecordSource _source;

    public RadicleRecordReader(IRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public RadicleRecordReader(RadicleEnvironment env, StringTables strings)
        : this(new PairRecordSource(env, strings))
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    private StringTables _strings => _source.Strings;

    internal static string DecodeName(byte[] bytes) => CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);

    public string? Signature(string id) => Safe(() => Find(id) is { } record ? _env.Info(record).Signature : null, null);

    public string? EditorId(string id) => Safe(() => Find(id) is { } record ? _env.Info(record).EditorId : null, null);

    public string? Name(string id) => Safe(() =>
    {
        if (Find(id) is not { } record)
            return null;
        return _strings.ResolveName(record) is { } bytes ? RadicleRecordReader.DecodeName(bytes) : _env.Info(record).NameText;
    }, null);

    public string? Scalar(string id, string path) => Safe(() => Find(id) is { } record ? _env.GetField(record, path) : null, null);

    public string? Text(string id, string path) => Safe(() => Find(id) is { } record ? _strings.RecordText(record, path) : null, null);

    // Invariant: null when the path is not readable for the record, an empty list when it has no entries; a null link reads as an empty text.
    public IReadOnlyList<string>? List(string id, string path) => Safe(() =>
        Find(id) is { } record && _env.TryListSize(record, path, out var size)
            ? Enumerable.Range(0, size).Select(index => _env.GetField(record, path, index) ?? string.Empty).ToArray() : null, null);

    // Requires: rawFormId is a word read from the record id itself, so its master index is relative to that record; null for a missing record, a null word or an unresolvable word.
    public string? ResolveFormId(string id, uint rawFormId) => Safe(() =>
        Find(id) is { } record && _env.ResolveFormId(record, rawFormId) is { } key ? RecordKey.Format(key) : null, null);

    public IReadOnlyList<string> FormLinks(string id, string subrecord) => Safe(() =>
        Find(id) is { } record ? (IReadOnlyList<string>)_env.FormLinks(record, subrecord).Select(RecordKey.Format).ToArray() : [], []);

    // Invariant: null when the COBJ condition list is unreadable; the forms are the master-resolved keys of the typed condition parameters.
    public IReadOnlyList<RecordCondition>? RecipeConditions(string id) => Safe<IReadOnlyList<RecordCondition>?>(() =>
    {
        if (Find(id) is not { } record || _env.Info(record).Signature != "COBJ") return null;
        var rows = _env.Conditions(record);
        var typed = _env.ConditionParameters(record, rows);
        return rows.Select((row, i) =>
            new RecordCondition(row, typed[i].Comparison.Form, typed[i].Param1.Form, typed[i].Param2.Form, typed[i].Reference.Form)).ToArray();
    }, null);

    // Invariant: null when the record is missing or its condition run is unreadable, never zero.
    public int? ConditionCount(string id) => Safe<int?>(() => Find(id) is { } record ? _env.Conditions(record).Count() : null, null);

    public LockData? Lock(string id) => Safe(() => Find(id) is { } record ? _env.Lock(record) : null, null);

    public IReadOnlyList<string> Winners(string signature) => Safe(() =>
        (IReadOnlyList<string>)_env.LiveWinnersOf(signature).Select(record => RecordKey.Format(_env.Info(record).FormKey)).ToArray(), []);

    public string? FindEditorId(string editorId, string? signature = null) => Safe(() =>
        _env.FindEditorId(editorId, signature) is { } record ? RecordKey.Format(_env.Info(record).FormKey) : null, null);

    public IReadOnlyList<string> WorldspaceMapMarkers(string worldspaceId) => Safe<IReadOnlyList<string>>(() =>
        RecordKey.TryParse(worldspaceId, out var plugin, out var local)
            ? _env.WorldspaceMapMarkers(new FormKey(plugin, local)).Select(record => RecordKey.Format(_env.Info(record).FormKey)).ToArray()
            : [], []);

    // Invariant: n counts ALST and ALLS aliases only (ALCS collections are not elements); the flat ALFR and ALDN rows are numbered quest-wide, so each running index advances only past an alias that holds one.
    public IReadOnlyList<AliasDisplay> AliasDisplays(string questId) => Safe<IReadOnlyList<AliasDisplay>>(() =>
    {
        if (Find(questId) is not { } record || _env.Info(record).Signature != "QUST" || !_env.TryListSize(record, "QUST\\Aliases", out var aliases))
            return [];
        var rows = new List<AliasDisplay>();
        int forcedAt = 0, displayAt = 0;
        for (var i = 0; i < aliases; i++)
        {
            var scope = $"QUST\\Aliases[{i}]\\";
            string? forced = null, display = null;
            if (_env.TryListSize(record, scope + "AliasForcedReferences", out var forcedSize) && forcedSize > 0)
            {
                forced = RecordRows.Format(_env.GetField(record, "QUST\\AliasForcedReference", forcedAt));
                forcedAt += forcedSize;
            }
            if (_env.TryListSize(record, scope + "AliasDisplayNames", out var displaySize) && displaySize > 0)
            {
                display = RecordRows.Format(_env.GetField(record, "QUST\\AliasDisplayName", displayAt));
                displayAt += displaySize;
            }
            if (display is not null)
                rows.Add(new AliasDisplay(forced, display));
        }
        return rows;
    }, []);

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (RadicleException)
        {
            return fallback;
        }
    }

    private Record? Find(string id) =>
        RecordKey.TryParse(id, out var plugin, out var local) ? _env.Winner(new FormKey(plugin, local)) : null;
}
