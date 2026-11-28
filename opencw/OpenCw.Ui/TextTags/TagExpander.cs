using System.Text;

namespace OpenCommonwealth.Services.Ui.TextTags;

public interface ITagResolver
{
    string? Resolve(string kind, string? value, string? sub);
}

public static class TagExpander
{
    public const string CapSub = "Cap";

    // Invariant: a tag the resolver leaves null is copied verbatim and an unmatched '<' keeps the rest of the text, so no input text is ever dropped.
    public static string Expand(string? text, ITagResolver? resolver)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (resolver is null)
            return text;
        var result = new StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf('<', at);
            int close = open < 0 ? -1 : text.IndexOf('>', open + 1);
            if (close < 0)
                break;
            open = text.LastIndexOf('<', close, close - open + 1);
            result.Append(text, at, open - at);
            string inner = text.Substring(open + 1, close - open - 1);
            result.Append(Replace(inner, resolver) ?? text.Substring(open, close - open + 1));
            at = close + 1;
        }
        result.Append(text, at, text.Length - at);
        return result.ToString();
    }

    private static string? Replace(string inner, ITagResolver resolver)
    {
        int dot = inner.IndexOf('.');
        int equals = inner.IndexOf('=');
        int first = dot < 0 ? equals : equals < 0 ? dot : Math.Min(dot, equals);
        if (first <= 0)
            return null;
        string kind = inner[..first];
        string? value = null;
        string? sub = null;
        if (dot >= 0 && (equals < 0 || dot < equals))
        {
            sub = inner[(dot + 1)..(equals < 0 ? inner.Length : equals)];
            value = equals < 0 ? null : inner[(equals + 1)..];
        }
        else
        {
            value = inner[(equals + 1)..(dot < 0 ? inner.Length : dot)];
            sub = dot < 0 ? null : inner[(dot + 1)..];
        }
        bool capitalize = sub is not null && sub.Contains(CapSub, StringComparison.Ordinal);
        if (capitalize)
            sub = sub!.Replace(CapSub, string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrEmpty(sub))
            sub = null;
        string? replacement = resolver.Resolve(kind, value, sub);
        return capitalize && !string.IsNullOrEmpty(replacement) && char.IsLower(replacement[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }
}
