using System;
using OpenCommonwealth.Services.Ui.Dialogs;

namespace OpenCommonwealth.Services.Dialogue;

public static class VoiceTiming
{
    public const int TailMs = 150;

    public static int LineMs(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 ? (int)Math.Ceiling(seconds * 1000) + TailMs : 0;

    // Invariant: a clip with no usable length leaves the node as it was; otherwise the voice carries the line, so it is no longer forced, and a displayed line stays up at least as long as the same text unvoiced.
    public static DialogueNode Voiced(DialogueNode node, double seconds, bool subtitles)
    {
        var ms = LineMs(seconds);
        if (ms <= 0) return node;
        if (subtitles && !string.IsNullOrEmpty(node.Line)) ms = Math.Max(ms, DialogueNode.ReadableMs(node.Line));
        return node with { LineDurationMs = ms, ForceLine = false };
    }
}
