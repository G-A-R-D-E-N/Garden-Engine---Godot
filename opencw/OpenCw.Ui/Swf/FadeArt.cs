namespace OpenCommonwealth.Services.Ui.Swf;

public static class FadeArt
{
    public const string Movie = "Interface/FaderMenu.swf";
    public const string Rect = "Menu_mc/FadeRect_mc";
    public const string Spinner = "Menu_mc/SpinnerIcon_mc";
    public const string SpinnerClip = Spinner + "/#12";

    public static IEnumerable<SwfTemplate> Templates() => SpinnerArt.Templates(SpinnerClip);

    public static SwfBindings Build(double alpha, bool spinner, int? spinnerFrame = null)
    {
        var b = new SwfBindings();
        b.Hide.Add(Rect);
        if (spinner && alpha > 0)
        {
            b.Alpha[Spinner] = Math.Clamp(alpha, 0, 1);
            if (spinnerFrame is { } frame)
                SpinnerArt.Drive(b, SpinnerClip, frame);
        }
        else
            b.Hide.Add(Spinner);
        return b;
    }
}
