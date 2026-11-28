namespace OpenCommonwealth.Services.Ui.Swf;

public static class SwfTextScale
{
    public const double Factor = 1.0;

    public static int Px(double fontHeight) => (int)Math.Round(fontHeight * Factor);
}
