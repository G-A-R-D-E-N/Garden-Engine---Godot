using System;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class RadicleVendorRecords : IActorRecords
{
    private const string PlacedBasePath = "REFR\\Base";

    private readonly IRecordSource _source;
    private readonly IRecordReader _reader;

    public RadicleVendorRecords(IRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _reader = new RadicleRecordReader(source);
    }

    public RadicleVendorRecords(RadicleEnvironment env, StringTables strings)
        : this(new PairRecordSource(env, strings))
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    private StringTables _strings => _source.Strings;

    public ActorSetup? Read(string refKey, string baseKey)
    {
        try
        {
            if (Find(baseKey) is not { } npc) return null;
            var info = _env.Info(npc);
            var name = _strings.ResolveName(npc) is { } bytes ? RadicleRecordReader.DecodeName(bytes) : info.NameText;
            return new ActorSetup(string.IsNullOrWhiteSpace(name) ? info.EditorId ?? baseKey : name, true, []);
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    public string? ChestBaseKey(string chestRefKey) => RecordRows.Link(_reader, chestRefKey, PlacedBasePath);

    private Record? Find(string id) =>
        RecordKey.TryParse(id, out var plugin, out var local) ? _env.Winner(new FormKey(plugin, local)) : null;
}
