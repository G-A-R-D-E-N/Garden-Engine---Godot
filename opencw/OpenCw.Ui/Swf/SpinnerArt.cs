namespace OpenCommonwealth.Services.Ui.Swf;

public static class SpinnerArt
{
    public const int Frames = 57;
    public const string TemplatePrefix = "spin";
    public const double DefaultRate = 30;

    public static string Template(int frame) => TemplatePrefix + frame;

    public static IEnumerable<SwfTemplate> Templates(string clipPath) =>
        Enumerable.Range(0, Frames).Select(frame => new SwfTemplate(Template(frame), null, clipPath, frame));

    // Constraint: the clip loops from its first frame at the movie frame rate; a rate of zero or less holds the first frame.
    public static int Frame(double seconds, double frameRate) =>
        frameRate > 0 && seconds > 0 ? (int)((long)(seconds * frameRate) % Frames) : 0;

    public static void Drive(SwfBindings bindings, string clipPath, int frame)
    {
        bindings.Hollow.Add(clipPath);
        bindings.Sprites.Add(new SwfSprite(Template(Math.Clamp(frame, 0, Frames - 1)), clipPath, 0, 0, 1));
    }
}

public sealed class SpinnerClock
{
    private double _seconds;
    private bool _running;

    // Constraint: the clip restarts at frame 0 each time it becomes shown, so a shown spinner never starts mid-loop.
    public int Tick(double delta, bool shown, double frameRate)
    {
        if (!shown)
        {
            _running = false;
            return 0;
        }
        if (!_running)
        {
            _running = true;
            _seconds = 0;
        }
        var frame = SpinnerArt.Frame(_seconds, frameRate);
        _seconds += Math.Max(0, delta);
        return frame;
    }
}
