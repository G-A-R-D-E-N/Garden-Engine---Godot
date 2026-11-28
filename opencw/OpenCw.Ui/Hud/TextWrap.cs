using System;
using System.Collections.Generic;
using System.Text;

namespace OpenCommonwealth.Services.Ui.Hud;

public static class TextWrap
{
    public static IReadOnlyList<string> Wrap(string text, double maxWidth, Func<string, double> measure)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = paragraph.Trim();
            if (trimmed.Length == 0) continue;
            if (!(maxWidth > 0))
            {
                lines.Add(trimmed);
                continue;
            }
            WrapParagraph(trimmed, maxWidth, measure, lines);
        }
        return lines;
    }

    private static void WrapParagraph(string paragraph, double maxWidth, Func<string, double> measure, List<string> lines)
    {
        string current = "";
        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (measure(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
            }
            if (measure(word) <= maxWidth)
            {
                current = word;
                continue;
            }
            var piece = new StringBuilder();
            foreach (char c in word)
            {
                if (piece.Length > 0 && !(char.IsLowSurrogate(c) && char.IsHighSurrogate(piece[^1])) && measure(piece.ToString() + c) > maxWidth)
                {
                    lines.Add(piece.ToString());
                    piece.Clear();
                }
                piece.Append(c);
            }
            current = piece.ToString();
        }
        if (current.Length > 0) lines.Add(current);
    }
}
