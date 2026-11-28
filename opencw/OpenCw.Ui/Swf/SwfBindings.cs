using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record SwfListRow(string Text, bool Selected);

public sealed record SwfListWindow(IReadOnlyList<SwfListRow> Rows, bool MoreAbove, bool MoreBelow, int First = 0);

public sealed class SwfScroll
{
    private readonly Dictionary<string, int> _first = new();

    public int First(string key) => _first.GetValueOrDefault(key);

    public int Wheel(string key, int count, int notches, int visible = SwfBindings.ListRows)
    {
        var first = Math.Clamp(First(key) + notches, 0, Math.Max(0, count - visible));
        _first[key] = first;
        return first;
    }

    public static SwfListWindow Window(SwfScroll? scroll, string key, IReadOnlyList<SwfListRow> rows)
    {
        var window = SwfBindings.Window(rows, SwfBindings.ListRows, scroll?.First(key) ?? 0);
        if (scroll is not null)
            scroll._first[key] = window.First;
        return window;
    }
}

public readonly record struct SwfPlace(double Scale, double Tx, double Ty)
{
    public static SwfPlace Identity => new(1, 0, 0);

    public bool IsIdentity => Scale == 1 && Tx == 0 && Ty == 0;
}

public readonly record struct SwfFill(double Fraction, double Width);

public enum SwfAlign { Left, Center, Right }

public sealed record SwfSpriteText(string Text, double X, double Y, double Width, double Height, int FontSize, bool Bold, uint Rgb = 0xFFFFFF, SwfAlign Align = SwfAlign.Center, bool Shrink = false, bool Wrap = false);

public sealed record SwfSpriteBar(double X, double Y, double Width, double Height, double Fraction, double Track = 0.3);

public sealed record SwfHint(string Path, bool RightJustified, string Text, char Glyph, bool Focused, string Fallback);

public sealed record SwfSprite(string Template, string Parent, double X, double Y, double Alpha, SwfSpriteText? Label = null, SwfSpriteBar? Bar = null, double Scale = 1);

public sealed class SwfBindings
{
    public HashSet<string> Hide { get; } = new();
    public HashSet<string> Show { get; } = new();
    public HashSet<string> Hollow { get; } = new();

    // Constraint: a clip is emptied only when a sprite aimed at it found its template, so a missing template keeps the static art.
    public HashSet<string> HollowWithTemplate(Func<string, bool> hasTemplate) =>
        Hollow.Where(path => Sprites.Any(s => s.Parent == path && hasTemplate(s.Template))).ToHashSet();
    public Dictionary<string, double> Alpha { get; } = new();
    public Dictionary<string, SwfPlace> Place { get; } = new();
    public Dictionary<string, SwfFill> FillRight { get; } = new();
    public List<SwfSprite> Sprites { get; } = new();
    public List<SwfHint> Hints { get; } = new();
    public HashSet<string> Headers { get; } = new();
    public List<SwfBracket> Brackets { get; } = new();
    public Dictionary<string, string> Text { get; } = new();
    public Dictionary<string, double> FillX { get; } = new();
    public Dictionary<string, IReadOnlyList<SwfListRow>> Lists { get; } = new();

    public const int ListRows = 9;
    public const double HeaderArrowGap = 10;

    public const string HeaderText = "/textField";
    public const string HeaderRightArrow = "/RightArrow";

    public static double HeaderArrowX(double textX, double textWidth, double fieldWidth) =>
        textX + Math.Clamp(textWidth, 0, Math.Max(0, fieldWidth)) + HeaderArrowGap;

    public static SwfListWindow Window(IReadOnlyList<SwfListRow> rows, int visible = ListRows, int prior = 0)
    {
        var selected = -1;
        for (var i = 0; i < rows.Count && selected < 0; i++)
            if (rows[i].Selected)
                selected = i;
        var first = prior;
        if (selected >= 0)
            first = Math.Clamp(first, selected - visible + 1, selected);
        first = Math.Max(0, Math.Min(first, rows.Count - visible));
        return new SwfListWindow(rows.Skip(first).Take(visible).ToList(), first > 0, first + visible < rows.Count, first);
    }

    private static string Step(double value, double step) => (Math.Round(value / step) * step + 0.0).ToString("R", CultureInfo.InvariantCulture);

    public string Signature()
    {
        var sb = new System.Text.StringBuilder();
        void Keys(char tag, IEnumerable<string> keys)
        {
            foreach (var k in keys.OrderBy(x => x, StringComparer.Ordinal))
                sb.Append(tag).Append(k).Append('\n');
        }
        Keys('H', Hide);
        Keys('S', Show);
        Keys('O', Hollow);
        foreach (var (k, v) in Alpha.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('A').Append(k).Append('=').Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (k, v) in Place.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('P').Append(k).Append('=').Append(v.Scale.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(v.Tx.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(v.Ty.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (k, v) in FillX.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('F').Append(k).Append('=').Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (k, v) in FillRight.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('R').Append(k).Append('=').Append(v.Fraction.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(v.Width.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (k, v) in Text.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('T').Append(k).Append('=').Append(v).Append('\n');
        foreach (var sprite in Sprites.Where(x => Math.Round(x.Alpha / 0.05) > 0))
            sb.Append('X').Append(sprite.Template).Append('|').Append(sprite.Parent).Append('|').Append(Step(sprite.X, 0.5)).Append(',').Append(Step(sprite.Y, 0.5)).Append(',').Append(Step(sprite.Alpha, 0.05)).Append('|').Append(sprite.Label is { } label ? FormattableString.Invariant($"{label.Text}@{label.X:R},{label.Y:R},{label.Width:R},{label.Height:R},{label.FontSize},{label.Bold},{label.Rgb},{label.Align},{label.Shrink},{label.Wrap}") : "").Append('|').Append(sprite.Bar is { } bar ? Step(bar.Fraction, 0.02) + "," + bar.Track.ToString("R", CultureInfo.InvariantCulture) + "," + Step(bar.Width, 0.5) + "," + Step(bar.Height, 0.5) : "").Append('|').Append(Step(sprite.Scale, 0.01)).Append('\n');
        foreach (var hint in Hints)
            sb.Append('K').Append(hint.Path).Append('|').Append(hint.RightJustified).Append('|').Append(hint.Text).Append('|').Append(hint.Glyph).Append('|').Append(hint.Focused).Append('|').Append(hint.Fallback).Append('\n');
        foreach (var k in Headers.OrderBy(x => x, StringComparer.Ordinal))
            sb.Append('D').Append(k).Append('\n');
        foreach (var bracket in Brackets)
            sb.Append('B').Append(bracket.Header).Append('=').Append(bracket.Right.ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(bracket.Bottom.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (k, rows) in Lists.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append('L').Append(k).Append('=').Append(string.Join('\u001f', rows.Select(r => (r.Selected ? "*" : "-") + r.Text))).Append('\n');
        return sb.ToString();
    }

    public static string Clean(string s) =>
        s.StartsWith('$') ? Regex.Replace(s[1..].Replace('_', ' '), @"\s+", " ").Trim() : s;

    public static string Number(int n, int digits = 0) => n.ToString(digits > 0 ? "D" + digits : "D", CultureInfo.InvariantCulture);
}
