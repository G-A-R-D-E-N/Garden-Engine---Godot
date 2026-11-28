namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record SwfBracket(string Parent, string Header, double Right, double Bottom);

public static class SwfBracketLines
{
    public const double LineWidth = 2;
    public const double ArrowWidth = 14.3;
    public const double TopY = 27.5;
    public const double TopTick = 31.5;
    public const double BottomTick = 4;

    public static IReadOnlyList<(double X, double Y)[]> Polylines(SwfBracket b, double headerX, double arrowX) =>
        new[]
        {
            new[] { (0.0, TopTick), (0.0, TopY), (headerX, TopY) },
            new[] { (headerX + arrowX + ArrowWidth, TopY), (b.Right, TopY), (b.Right, TopTick) },
            new[] { (0.0, b.Bottom - BottomTick), (0.0, b.Bottom), (b.Right, b.Bottom), (b.Right, b.Bottom - BottomTick) },
        };
}
