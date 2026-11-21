using System;
using System.Net;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.UI.Own;

public static class GardenMessageText
{
    private static readonly Regex Tags = new(
        @"<\s*(?<close>/?)\s*(?<name>[A-Za-z][A-Za-z0-9]*)(?<attributes>[^<>]*)>",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex FontAttributes = new(
        @"^(?:\s+[A-Za-z_:][A-Za-z0-9_:.-]*(?:\s*=\s*(?:'[^'<>]*'|""[^""<>]*""|[^\s'""=<>`]+))?)*\s*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static string ToPlainText(string text)
    {
        string flattened = Tags.Replace(text, match =>
        {
            string name = match.Groups["name"].Value.ToLowerInvariant();
            bool closing = match.Groups["close"].Value.Length != 0;
            string rawAttributes = match.Groups["attributes"].Value;
            string attributes = rawAttributes.Trim();
            if (name == "br" && !closing && (attributes.Length == 0 || attributes == "/"))
                return "\n";
            if (name == "p" && attributes.Length == 0)
                return "\n\n";
            if ((name is "b" or "strong" or "i" or "em" or "u") && attributes.Length == 0)
                return "";
            if (name == "font" && (attributes.Length == 0 ||
                (!closing && FontAttributes.IsMatch(rawAttributes))))
                return "";
            return match.Value;
        });
        return WebUtility.HtmlDecode(flattened).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }
}
