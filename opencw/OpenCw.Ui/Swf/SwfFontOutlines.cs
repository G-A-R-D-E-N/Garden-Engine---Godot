using System.Globalization;
using System.Text;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record SwfGlyphArt(string Svg, double Advance, double Ascent, double Size);

public static class SwfFontOutlines
{
    public static readonly string[] FontMovies = ["Interface/fonts_en.swf", "Interface/fonts_console.swf"];

    public static SwfGlyphArt? Read(byte[] fontMovie, string fontClass, char code, double size)
    {
        using var file = SwfFile.FromBytes(fontMovie);
        return Read(file, fontClass, code, size);
    }

    public static SwfFontLocation? Locate(SwfFile file, string fontClass)
    {
        var font = file.Layout().Fonts.FirstOrDefault(f => f.FontClass == fontClass && f.Codes is not null);
        if (font?.Codes is null || font.UnitsPerEm is not { } em || em == 0)
            return null;
        var character = file.Characters().FirstOrDefault(c => c.CharacterId == font.FontId);
        return character is null ? null : new SwfFontLocation(character.TagIndex, em, font.Ascent ?? 0, font.Codes);
    }

    public static SwfGlyphArt? Read(SwfFile file, string fontClass, char code, double size, ISwfFontSource? fonts = null)
    {
        if (Locate(file, fontClass) is not { } font)
            return null;
        var glyph = font.Codes.ToList().IndexOf(code);
        if (glyph < 0)
            return null;
        var glyphs = file.FontGlyphs(font.TagIndex);
        var advance = Advance(fonts, fontClass, code);
        return glyph < glyphs.Count ? Outline(glyphs[glyph].Commands, font.UnitsPerEm, font.Ascent, size, advance) : null;
    }

    // Constraint: a failing font source must never lose the glyph; the outline estimate stands in.
    private static double? Advance(ISwfFontSource? fonts, string fontClass, char code)
    {
        try
        {
            return fonts?.Metrics(fontClass) is { } metrics && metrics.Advances.TryGetValue(code, out var em) ? em : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static SwfGlyphArt? Outline(IReadOnlyList<SwfShapeCommandRecord> commands, double unitsPerEm, double ascent, double size, double? advanceEm = null)
    {
        var k = size / unitsPerEm;
        var path = new StringBuilder();
        double x = 0, y = 0, minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        void Mark(double px, double py)
        {
            minX = Math.Min(minX, px);
            maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py);
            maxY = Math.Max(maxY, py);
        }
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        foreach (var c in commands)
        {
            switch (c.Kind)
            {
                case SwfShapeCommandKind.Move:
                    x = c.X;
                    y = c.Y;
                    path.Append('M').Append(F(x * k)).Append(' ').Append(F(y * k));
                    Mark(x, y);
                    break;
                case SwfShapeCommandKind.StraightEdge:
                    x += c.X;
                    y += c.Y;
                    path.Append('L').Append(F(x * k)).Append(' ').Append(F(y * k));
                    Mark(x, y);
                    break;
                case SwfShapeCommandKind.CurvedEdge:
                    var cx = x + c.ControlX;
                    var cy = y + c.ControlY;
                    x = cx + c.X;
                    y = cy + c.Y;
                    path.Append('Q').Append(F(cx * k)).Append(' ').Append(F(cy * k)).Append(' ').Append(F(x * k)).Append(' ').Append(F(y * k));
                    Mark(cx, cy);
                    Mark(x, y);
                    break;
            }
        }
        if (path.Length == 0 || maxX <= minX || maxY <= minY)
            return null;
        var svg = FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{F(minX * k)} {F(minY * k)} {F((maxX - minX) * k)} {F((maxY - minY) * k)}\" width=\"{F((maxX - minX) * k)}\" height=\"{F((maxY - minY) * k)}\"><path fill=\"#fff\" fill-rule=\"evenodd\" d=\"{path}Z\"/></svg>");
        return new SwfGlyphArt(svg, advanceEm is { } em ? em * size : (minX + maxX) * k, ascent * k, size);
    }
}
