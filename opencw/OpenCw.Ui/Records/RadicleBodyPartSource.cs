using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Vats;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Invariant: NPC_ RNAM names the RACE (of the Traits template owner when the Traits template flag is set) and RACE GNAM names the BPTD.
public sealed class RadicleBodyPartSource : IBodyPartSource, IBodyPartNpcRecords
{
    private const string NpcRacePath = "NPC_\\Race";
    private const string NpcTemplateFlagsPath = "NPC_\\TemplateFlags";
    private const string RaceBodyPartDataPath = "RACE\\BodyPartData";

    private readonly IRecordSource _source;
    private readonly IRecordReader _reader;

    public RadicleBodyPartSource(IRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _reader = new RadicleRecordReader(source);
    }

    public RadicleBodyPartSource(RadicleEnvironment env, StringTables strings)
        : this(new PairRecordSource(env, strings))
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    public IReadOnlyList<VatsBodyPartData>? Parts(string actorBaseKey)
    {
        try
        {
            if (Winner(actorBaseKey) is not { } npc || Signature(npc) != "NPC_" || BodyPartRace.Resolve(actorBaseKey, this) is not { } raceKey)
                return null;
            if (Winner(raceKey) is not { } race || Signature(race) != "RACE" || RecordRows.Link(_reader, raceKey, RaceBodyPartDataPath) is not { } bptdKey)
                return null;
            if (Winner(bptdKey) is not { } bptd || Signature(bptd) != "BPTD")
                return null;
            return _env.BodyPartData(bptd, _source.Strings).Parts
                .Where(p => p.Node is not null)
                .Select(p => new VatsBodyPartData(p.Name.Text, p.PartNode, p.VatsTarget, p.Node!.RawPartType, p.Node.DamageMult,
                    p.Node.Flags, p.Node.HealthPercent, p.Node.ToHitChance))
                .ToArray();
        }
        catch (Exception e) when (e is RadicleException or ArgumentException or DecoderFallbackException)
        {
            return null;
        }
    }

    uint? IBodyPartNpcRecords.TemplateFlags(string npcKey) =>
        uint.TryParse(_reader.Scalar(npcKey, NpcTemplateFlagsPath), NumberStyles.None, CultureInfo.InvariantCulture, out var flags) ? flags : null;

    string? IBodyPartNpcRecords.TraitsOwner(string npcKey)
    {
        if (Winner(npcKey) is not { } record)
            return null;
        var source = _env.NpcTemplateSource(record, Radicle.Interop.Native.NpcTemplateSlot.Traits);
        return source.Reason is Radicle.Interop.Native.NpcTemplateReason.Own or Radicle.Interop.Native.NpcTemplateReason.TemplateNpc
            && source.Source is { } owner
            ? RecordKey.Format(owner)
            : null;
    }

    string? IBodyPartNpcRecords.Race(string npcKey) => RecordRows.Link(_reader, npcKey, NpcRacePath);

    private Record? Winner(string key) =>
        RecordKey.TryParse(key, out var plugin, out var local) ? _env.Winner(new FormKey(plugin, local)) : null;

    private string Signature(Record record) => _env.Info(record).Signature;
}
