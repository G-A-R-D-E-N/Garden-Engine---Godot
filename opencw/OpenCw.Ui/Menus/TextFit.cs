using System;

namespace OpenCommonwealth.Services.Ui.Menus;

public readonly record struct FittedText(string Text, double Scale, bool Truncated);

public static class TextFit
{
    public const double MinScale = 0.6;
    public const string Ellipsis = "...";

    public static FittedText Fit(string? text, double maxWidth, Func<string, double> measure, double minScale = MinScale)
    {
        text ??= string.Empty;
        minScale = Math.Clamp(minScale, 0.01, 1);
        if (!(maxWidth > 0)) return new FittedText(string.Empty, minScale, text.Length > 0);
        var full = measure(text);
        if (!(full > maxWidth)) return new FittedText(text, 1, false);
        var scale = maxWidth / full;
        if (scale >= minScale) return new FittedText(text, scale, false);
        var budget = maxWidth / minScale;
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + Ellipsis;
            if (measure(candidate) <= budget) return new FittedText(candidate, minScale, true);
        }
        return new FittedText(Ellipsis, minScale, true);
    }
}
