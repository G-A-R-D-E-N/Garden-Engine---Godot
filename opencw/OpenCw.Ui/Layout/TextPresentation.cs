using System;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Layout;

public sealed record TextPresentation(
    double Size,
    uint Rgba,
    LayoutTextAlign Align,
    bool Wrap,
    bool Bold,
    bool Italic,
    string? FontName);

public static class TextPresentations
{
    public static TextPresentation? Resolve(MenuLayout layout, LayoutRow row, double stageScale)
    {
        if (row.Text is not { } style) return null;
        var font = layout.Fonts.FirstOrDefault(candidate => candidate.FontId == style.FontId);
        if (font is null && !string.IsNullOrEmpty(style.FontClass))
            font = layout.Fonts.FirstOrDefault(candidate => candidate.FontClass == style.FontClass);
        return new TextPresentation(
            double.IsFinite(style.FontSize * stageScale) ? Math.Max(1, style.FontSize * stageScale) : 1,
            style.Rgba,
            style.Align,
            style.Multiline && style.WordWrap,
            font?.Bold ?? false,
            font?.Italic ?? false,
            font?.FontName);
    }
}
