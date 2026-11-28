namespace OpenCommonwealth.Services.Ui.Vats;

public interface IBodyPartNpcRecords
{
    uint? TemplateFlags(string npcKey);

    // Invariant: null means undecided; the owner of the Traits slot (the NPC itself or its template chain end).
    string? TraitsOwner(string npcKey);

    string? Race(string npcKey);
}

public static class BodyPartRace
{
    public const uint TraitsFlag = 1u << 0;

    // Invariant: unreadable template flags read as all set, like NpcTemplates.FactsOf.
    public static string? Resolve(string npcKey, IBodyPartNpcRecords records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var flags = records.TemplateFlags(npcKey) ?? uint.MaxValue;
        var owner = (flags & TraitsFlag) == 0 ? npcKey : records.TraitsOwner(npcKey);
        return owner is null ? null : records.Race(owner);
    }
}
