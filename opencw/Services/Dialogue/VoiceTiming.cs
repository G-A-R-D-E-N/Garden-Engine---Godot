using System;

namespace OpenCommonwealth.Services.Dialogue;

public static class VoiceTiming
{
    public const int TailMs = 150;

    public static int LineMs(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? (int)Math.Ceiling(seconds * 1000) + TailMs : 0;
}
