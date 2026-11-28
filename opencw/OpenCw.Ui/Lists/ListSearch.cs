using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Lists;

public static class ListSearch
{
    public static string[] Tokenize(string? query) =>
        query is null ? Array.Empty<string>() : query.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

    public static bool Matches(string text, IReadOnlyList<string> tokens) =>
        tokens.All(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
}
