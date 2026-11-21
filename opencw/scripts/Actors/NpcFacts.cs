using System;
using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;
using NpcTemplateSlot = Radicle.Interop.Native.NpcTemplateSlot;

namespace OpenCommonwealth;

internal sealed record NpcFacts(FormKey? Race, IReadOnlyDictionary<FormKey, int>? Factions, IReadOnlySet<FormKey>? Keywords)
{
    public static readonly NpcFacts Unknown = new(null, null, null);
}

internal interface INpcRecords
{
    uint? TemplateFlags(FormKey npc);

    // Constraint: null means undecided; Own and TemplateNpc must return the slot's owner.
    FormKey? TemplateSource(FormKey npc, NpcTemplateSlot slot);

    FormKey? Race(FormKey npc);

    IReadOnlyDictionary<FormKey, int>? Factions(FormKey npc);

    IReadOnlyList<FormKey>? Keywords(FormKey record);
}

internal readonly record struct NpcCrimeFaction(bool Known, FormKey? Faction);

internal static class NpcTemplates
{
    public const uint TraitsFlag = 1u << 0;
    public const uint FactionsFlag = 1u << 2;
    public const uint KeywordsFlag = 1u << 12;

    // Requires: ownCrimeFaction answers the NPC_ own CRIF (null for none) and may throw for an unreadable record; the Factions template slot owner is used, UNVERIFIED.
    public static NpcCrimeFaction CrimeFactionOf(FormKey npc, INpcRecords records, Func<FormKey, FormKey?> ownCrimeFaction)
    {
        var template = records.TemplateFlags(npc) ?? uint.MaxValue;
        FormKey? owner = (template & FactionsFlag) == 0 ? npc : records.TemplateSource(npc, NpcTemplateSlot.Factions);
        return owner is { } source ? new NpcCrimeFaction(true, ownCrimeFaction(source)) : new NpcCrimeFaction(false, null);
    }

    // Requires: a Known direct answer wins; an unknown one is replaced by the runtime-base answer and stays unknown if that is unknown too, never None.
    public static NpcCrimeFaction FirstKnown(NpcCrimeFaction direct, Func<NpcCrimeFaction> viaRuntimeBase) => direct.Known ? direct : viaRuntimeBase();

    // Requires: a null stored faction is a recorded "reports to nobody" (Known, None); a stored key the host cannot parse is unknown.
    public static NpcCrimeFaction OverrideOf(string? stored, Func<string, FormKey?> parse) =>
        stored is null ? new NpcCrimeFaction(true, null) : parse(stored) is { } key ? new NpcCrimeFaction(true, key) : default;

    public static NpcFacts FactsOf(FormKey npc, INpcRecords records)
    {
        var template = records.TemplateFlags(npc) ?? uint.MaxValue;
        FormKey? traitsOwner = (template & TraitsFlag) == 0 ? npc : records.TemplateSource(npc, NpcTemplateSlot.Traits);
        FormKey? race = traitsOwner is { } raceOwner ? records.Race(raceOwner) : null;
        FormKey? factionsOwner = (template & FactionsFlag) == 0 ? npc : records.TemplateSource(npc, NpcTemplateSlot.Factions);
        var factions = factionsOwner is { } factionsFrom ? records.Factions(factionsFrom) : null;
        FormKey? keywordsOwner = (template & KeywordsFlag) == 0 ? npc : records.TemplateSource(npc, NpcTemplateSlot.Keywords);
        HashSet<FormKey>? keywords = null;
        if (keywordsOwner is { } owner && race is { } raceKey && records.Keywords(owner) is { } own && records.Keywords(raceKey) is { } inherited)
            keywords = own.Concat(inherited).ToHashSet();
        return new NpcFacts(race, factions, keywords);
    }
}
