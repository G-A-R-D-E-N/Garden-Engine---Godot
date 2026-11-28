using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Layout;

public static partial class SwfLayoutConversion
{
    public const double FallbackFontSize = 16;
    private const ushort RootTimelineId = 0;
    private const uint White = 0xFFFFFFFF;

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"<br\s*/?>|</p\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    public static IReadOnlyList<MenuLayout> ToMenuLayouts(MenuSwfRef source, SwfLayoutRecord record)
    {
        var rootStates = record.States.Where(state => state.TimelineId == RootTimelineId).ToList();
        var fonts = record.Fonts.Select(ToLayoutFont).ToList();
        var layouts = new List<MenuLayout>();
        foreach (var state in rootStates)
        {
            var rows = ConvertRows(state.Rows, record.Stage, source.Rows);
            if (rows.Count == 0)
                continue;
            var name = state.Label.Length > 0 ? state.Label : DefaultLayouts.State;
            layouts.Add(new MenuLayout(source.Menu, name, record.Stage.Width, record.Stage.Height, fonts, rows));
        }
        return layouts;
    }

    private static List<LayoutRow> ConvertRows(IReadOnlyList<SwfLayoutRowRecord> source, SwfBoundsRecord stage, IReadOnlyDictionary<string, string> aliases)
    {
        var rows = new List<LayoutRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in source)
        {
            if (!row.HasRect || ToKind(row.Kind) is not { } kind || !seen.Add(row.Path))
                continue;
            rows.Add(new LayoutRow(
                row.Path,
                kind,
                rows.Count,
                row.Visible,
                new LayoutRect(row.Rect.X - stage.X, row.Rect.Y - stage.Y, row.Rect.Width, row.Rect.Height),
                row.Text is { } text ? ToTextStyle(text) : null));
        }
        foreach (var (name, swfPath) in aliases)
        {
            if (seen.Contains(name) || rows.FirstOrDefault(row => row.Path == swfPath) is not { } target)
                continue;
            seen.Add(name);
            rows.Add(target with { Path = name });
        }
        return rows;
    }

    private static LayoutRowKind? ToKind(SwfCharacterKind kind) => kind switch
    {
        SwfCharacterKind.Shape or SwfCharacterKind.Morph => LayoutRowKind.Shape,
        SwfCharacterKind.Bitmap => LayoutRowKind.Bitmap,
        SwfCharacterKind.Text or SwfCharacterKind.EditText => LayoutRowKind.Text,
        SwfCharacterKind.Sprite or SwfCharacterKind.Button => LayoutRowKind.Clip,
        _ => null,
    };

    private static TextStyle ToTextStyle(SwfLayoutTextRecord text) => new(
        text.FontId,
        text.FontHeight ?? FallbackFontSize,
        text.Color is { } argb ? ToRgba(argb) : White,
        ToAlign(text.Align),
        text.Multiline,
        text.WordWrap,
        text.AutoSize,
        text.Html ? WebUtility.HtmlDecode(Tags().Replace(LineBreaks().Replace(text.Text, "\n"), "")).TrimEnd('\n', '\r') : text.Text,
        text.FontClass);

    private static LayoutFont ToLayoutFont(SwfLayoutFontRecord font) => new(
        font.FontId,
        font.Name,
        font.Bold,
        font.Italic,
        font.FontName,
        font.FontClass,
        font.GlyphCount,
        font.Codes);

    // Constraint: Radicle packs colour as 0xAARRGGBB, TextStyle.Rgba is 0xRRGGBBAA.
    internal static uint ToRgba(uint argb) => (argb << 8) | (argb >> 24);

    private static LayoutTextAlign ToAlign(byte? align) => align switch
    {
        1 => LayoutTextAlign.Right,
        2 => LayoutTextAlign.Center,
        3 => LayoutTextAlign.Justify,
        _ => LayoutTextAlign.Left,
    };
}
