namespace OpenCommonwealth.Services.Ui.Swf;

public static class LoadingArt
{
    public const string Movie = "Interface/LoadingMenu.swf";
    public const string Root = "FilterHolder_mc/Menu_mc/";
    public const string Left = Root + "LeftText_mc";
    public const string Black = Root + "BlackRect_mc";
    public const string Spinner = Root + "VaultTecLogo_mc/#14";

    public const double TextX = 13;
    public const double TextY = 81.35;
    public const double TextWidth = 501;
    public const double LineHeight = 22.8;
    public const int TextSize = 19;
    public const double NumberX = 2;
    public const double NumberSpacer = 29;
    public const int NumberSize = 17;
    public const double BorderLeft = 8;
    public const double BorderTop = 3;
    public const double BorderRight = 5;
    public const double BorderBottom = 10;
    public const double BorderWidth = 2;
    public const double FieldWidth = 504.95;
    public const uint Ink = 0xDDDDDD;

    public const string NumberPrefix = "VDSG No. ";

    public static readonly string[] Hidden =
    [
        Left + "/LoadScreenText_tf", Left + "/VDSGText_tf", Left + "/LevelMeter_mc", Left + "/LevelText_tf",
    ];

    public static double TextTop(int lines) => TextY - (Math.Max(1, lines) - 1) * LineHeight;

    public static (double X, double Y, double W, double H) Border(int lines)
    {
        var top = TextTop(lines) - BorderTop;
        return (TextX - BorderLeft, top, FieldWidth + BorderLeft + BorderRight, 2 + Math.Max(1, lines) * LineHeight + BorderTop + BorderBottom);
    }

    public static IEnumerable<SwfTemplate> Templates() => SpinnerArt.Templates(Spinner);

    public static SwfBindings Build(string tip, int number, int lines, int? spinnerFrame = null)
    {
        var b = new SwfBindings();
        foreach (var path in Hidden)
            b.Hide.Add(path);
        if (spinnerFrame is { } frame)
            SpinnerArt.Drive(b, Spinner, frame);
        if (tip.Length == 0)
            return b;
        var top = TextTop(lines);
        b.Sprites.Add(new SwfSprite("", Left, TextX, top, 1, new SwfSpriteText(tip, 0, 0, TextWidth, Math.Max(1, lines) * LineHeight + 4, TextSize, true, Ink, SwfAlign.Left, false, true)));
        b.Sprites.Add(new SwfSprite("", Left, NumberX, top - NumberSpacer, 1, new SwfSpriteText(NumberPrefix + number, 0, 0, 400, 26.8, NumberSize, false, 0xFFFFFF, SwfAlign.Left)));
        var (x, y, w, h) = Border(lines);
        var t = BorderWidth;
        foreach (var (bx, by, bw, bh) in new[] { (x - t / 2, y - t / 2, w + t, t), (x - t / 2, y + h - t / 2, w + t, t), (x - t / 2, y - t / 2, t, h + t), (x + w - t / 2, y - t / 2, t, h + t) })
            b.Sprites.Add(new SwfSprite("", Left, bx, by, 1, null, new SwfSpriteBar(0, 0, bw, bh, 1, 0)));
        return b;
    }
}
