namespace OpenCommonwealth.Services.Ui.Trade;

public interface IVendorRecords
{
    IReadOnlySet<string> VendorKeywords { get; }
    IEnumerable<(string Keyword, string Reference)> LinkedRefs(string npcRefKey);
    IEnumerable<string?> FactionChests(string npcBaseKey);
}

public sealed class VendorChests(IVendorRecords records) : IVendorChests
{
    public string? ChestKey(string npcRefKey, string npcBaseKey) => Resolve(npcRefKey, npcBaseKey).FirstOrDefault();

    public IReadOnlyList<string> Resolve(string npcRefKey, string npcBaseKey)
    {
        var chests = new List<string>();
        foreach (var (keyword, reference) in records.LinkedRefs(npcRefKey))
            if (records.VendorKeywords.Contains(keyword)) Add(chests, reference);
        foreach (var chest in records.FactionChests(npcBaseKey)) Add(chests, chest);
        return chests;
    }

    private static void Add(List<string> chests, string? reference)
    {
        if (!string.IsNullOrEmpty(reference) && !chests.Contains(reference)) chests.Add(reference);
    }
}
