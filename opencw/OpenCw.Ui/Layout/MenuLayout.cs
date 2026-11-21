using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Layout;

public enum LayoutRowKind
{
    Shape,
    Bitmap,
    Text,
    Clip,
}

public enum LayoutTextAlign
{
    Left,
    Center,
    Right,
    Justify,
}

public readonly record struct LayoutRect(double X, double Y, double Width, double Height);

public sealed record TextStyle(
    int? FontId,
    double FontSize,
    uint Rgba,
    LayoutTextAlign Align,
    bool Multiline,
    bool WordWrap,
    bool AutoSize,
    string InitialText,
    string? FontClass = null);

public sealed record LayoutRow(
    string Path,
    LayoutRowKind Kind,
    int Depth,
    bool Visible,
    LayoutRect Rect,
    TextStyle? Text);

public sealed record LayoutFont(
    int FontId,
    string Name,
    bool? Bold,
    bool? Italic,
    string? FontName,
    string? FontClass,
    uint? GlyphCount,
    IReadOnlyList<ushort>? Codes);

public sealed record MenuLayout(
    string Menu,
    string State,
    double StageWidth,
    double StageHeight,
    IReadOnlyList<LayoutFont> Fonts,
    IReadOnlyList<LayoutRow> Rows)
{
    public LayoutRow? Row(string path) => Rows.FirstOrDefault(row => row.Path == path);
}

public static class LayoutRows
{
    public static (MenuLayout Layout, LayoutRow Row)? Resolve(MenuLayout? primary, MenuLayout fallback, string path)
    {
        if (primary is not null && primary.Row(path) is { } cached) return (primary, cached);
        if (fallback.Row(path) is { } defaults) return (fallback, defaults);
        return null;
    }
}
