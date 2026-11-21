using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public enum DialogueOutcome
{
    Finished,
    Left,
    Aborted,
    Trade,
    Command,
}

public enum DialogueState
{
    Speaking,
    Choosing,
    Resolving,
    Waiting,
    ChallengeResult,
    Closing,
    Ended,
}

public enum DialogueChallenge
{
    None,
    Easy,
    Medium,
    Hard,
    VeryHard,
}

public enum DialogueChoiceKind
{
    Normal,
    Trade,
    Command,
    Exit,
}

public sealed record DialogueChoice(
    string Text,
    string Tag = "",
    DialogueChallenge Challenge = DialogueChallenge.None,
    bool Used = false,
    bool Enabled = true,
    DialogueChoiceKind Kind = DialogueChoiceKind.Normal,
    int? Slot = null);

public sealed record DialogueNode(
    string Id,
    string SpeakerName,
    string Line,
    IReadOnlyList<DialogueChoice> Choices,
    int LineDurationMs = 0,
    bool CanExit = false,
    bool ForceLine = false)
{
    public const int MaxChoices = 4;

    public VoiceRef? Voice { get; init; }

    public int SpeakingMs => string.IsNullOrEmpty(Line)
        ? 0
        : LineDurationMs > 0 ? LineDurationMs : Math.Max(1500, 60 * Line.Length);
}

public readonly record struct DialogueSlotView(DialogueChoice? Choice, bool Visible, bool Enabled, double Alpha, bool Focused);
