namespace OpenCommonwealth.Services.Ui.Terminals;

public interface IHackingWords
{
    IReadOnlyList<string> OfLength(int length);
}

public sealed class HackingDictionary : IHackingWords
{
    private static readonly char[] Separators = [' ', '\r', '\n'];

    private readonly Dictionary<int, List<string>> _byLength = new();

    private HackingDictionary()
    {
    }

    public static HackingDictionary Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var dictionary = new HackingDictionary();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string word = token.ToUpperInvariant();
            if (!word.All(char.IsAsciiLetterUpper) || !seen.Add(word))
                continue;
            if (!dictionary._byLength.TryGetValue(word.Length, out List<string>? bucket))
                dictionary._byLength[word.Length] = bucket = new List<string>();
            bucket.Add(word);
        }
        return dictionary;
    }

    public bool IsEmpty => _byLength.Count == 0;

    public IReadOnlyList<string> OfLength(int length) =>
        _byLength.TryGetValue(length, out List<string>? bucket) ? bucket : Array.Empty<string>();
}
