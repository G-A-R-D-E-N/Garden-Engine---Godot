using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Ui.Terminals;

// Requires: read returns the dictionary text, or null while it is unavailable; only a parse with words is kept, so a dictionary that appears later is found.
public sealed class LazyHackingWords(Func<string?> read) : IHackingWords
{
    private HackingDictionary? _words;

    public IReadOnlyList<string> OfLength(int length)
    {
        if (_words is null && read() is { Length: > 0 } text && HackingDictionary.Parse(text) is { IsEmpty: false } parsed)
            _words = parsed;
        return _words?.OfLength(length) ?? Array.Empty<string>();
    }
}

public static class HackingIntelligence
{
    public const string Id = "Intelligence";

    public static float? Of(IReadOnlyList<SpecialEntry> special)
    {
        foreach (var entry in special)
            if (string.Equals(entry.Id, Id, StringComparison.OrdinalIgnoreCase) && double.IsFinite(entry.Value + entry.Modifier))
                return (float)(entry.Value + entry.Modifier);
        return null;
    }
}
