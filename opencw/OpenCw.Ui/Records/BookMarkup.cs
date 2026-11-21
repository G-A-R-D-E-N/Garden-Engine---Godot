using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Ui.Records;

public static partial class BookMarkup
{
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\[\s*pagebreak\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex PageBreak();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    // Invariant: null for an empty result; tags are dropped, a [pagebreak] marker (UNVERIFIED spelling) becomes a blank line, entities are decoded.
    public static string? Plain(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return null;
        var text = PageBreak().Replace(markup.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd((char)0), "\n\n");
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Tag().Replace(text, string.Empty));
        var lines = new StringBuilder();
        foreach (var line in text.Split('\n')) lines.Append(line.Trim()).Append('\n');
        var plain = BlankRuns().Replace(lines.ToString(), "\n\n").Trim();
        return plain.Length == 0 ? null : plain;
    }
}
