using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Dialogs;

public sealed class DialogueSession
{
    public const int SlotCount = 4;
    public const double ChoiceFadeInSeconds = 0.2;
    public const double OthersFadeOutSeconds = 0.15;
    public const double ChosenHoldSeconds = 0.3;
    public const double ChosenFadeSeconds = 0.25;
    public const double WaitingNoticeSeconds = 5;
    public const double ChallengeSeconds = 1;
    public const double CloseFadeSeconds = 0.2;
    public const double UsedAlpha = 0.6;
    public const double DisabledAlpha = 0.35;
    public const string AcceptCue = "ui.accept";
    public const string ErrorCue = "ui.error";

    private readonly Func<bool>? _stillValid;
    private DialogueChoice?[] _slots = new DialogueChoice?[SlotCount];
    private DialogueNode _node;
    private double _speakTime;
    private double _choosingTime;
    private double _chosenTime;
    private double _waitTime;
    private double _challengeTime;
    private double _closeTime;
    private int _chosen = -1;
    private bool _selectionRaised;
    private DialogueOutcome _pending;

    public DialogueSession(DialogueNode first, bool subtitles = true, Func<bool>? stillValid = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        Subtitles = subtitles;
        _stillValid = stillValid;
        _node = first;
        Start(first);
    }

    public event Action<string>? CueRequested;

    public event Action<string, int>? ChoiceSelected;

    public event Action? SkipRequested;

    public event Action<DialogueNode>? NodeStarted;

    public event Action<DialogueNode>? NodeFinished;

    public event Action? Closing;

    public event Action? ChallengeResultDone;

    public event Action<DialogueOutcome>? Ended;

    public bool Subtitles { get; set; }

    public DialogueState State { get; private set; }

    public DialogueNode Node => _node;

    public DialogueOutcome? Outcome { get; private set; }

    public int FocusedSlot { get; private set; } = -1;

    public string EchoText => _chosen >= 0 ? _slots[_chosen]?.Text ?? "" : "";

    public bool LineVisible => State == DialogueState.Speaking && Node.Line.Length > 0 && (Subtitles || Node.ForceLine);

    public bool ShowWaitingNotice => State == DialogueState.Waiting && _waitTime >= WaitingNoticeSeconds;

    public bool ChallengeSucceeded { get; private set; }

    private bool StuckChoosing =>
        State == DialogueState.Choosing && _choosingTime >= WaitingNoticeSeconds && _slots.All(s => s is not { Enabled: true });

    public double PanelAlpha => State switch
    {
        DialogueState.Closing => Math.Clamp(1 - _closeTime / CloseFadeSeconds, 0, 1),
        DialogueState.Ended => 0,
        _ => 1,
    };

    public double BannerAlpha => State == DialogueState.ChallengeResult ? Envelope(_challengeTime) : 0;

    public DialogueSlotView Slot(int index)
    {
        if (index is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var choice = _slots[index];
        bool shown = choice is not null && State is DialogueState.Choosing or DialogueState.Resolving or DialogueState.Waiting;
        if (!shown || choice is null)
            return new DialogueSlotView(choice, false, choice is { Enabled: true }, 0, false);
        return new DialogueSlotView(choice, true, choice.Enabled, AlphaOf(index, choice), index == FocusedSlot);
    }

    public void Tick(double dt)
    {
        if (State == DialogueState.Ended)
            return;
        if (_stillValid is not null && !_stillValid())
        {
            Abort();
            return;
        }
        if (!double.IsFinite(dt) || dt <= 0)
            return;
        switch (State)
        {
            case DialogueState.Speaking:
                _speakTime += dt;
                if (_speakTime * 1000 >= _node.SpeakingMs)
                    EnterChoosing();
                break;
            case DialogueState.Choosing:
                _choosingTime += dt;
                break;
            case DialogueState.Resolving:
                _chosenTime += dt;
                if (_chosenTime >= OthersFadeOutSeconds)
                    RaiseSelection();
                break;
            case DialogueState.Waiting:
                _waitTime += dt;
                _chosenTime += dt;
                break;
            case DialogueState.ChallengeResult:
                _challengeTime += dt;
                if (_challengeTime >= ChallengeSeconds)
                {
                    State = DialogueState.Waiting;
                    _waitTime = 0;
                    ChallengeResultDone?.Invoke();
                }
                break;
            case DialogueState.Closing:
                _closeTime += dt;
                if (_closeTime >= CloseFadeSeconds)
                    End(_pending);
                break;
        }
    }

    public bool Skip()
    {
        if (State != DialogueState.Speaking)
            return false;
        SkipRequested?.Invoke();
        Cue(AcceptCue);
        EnterChoosing();
        return true;
    }

    public bool Choose(int slot)
    {
        if (State != DialogueState.Choosing || slot is < 0 or >= SlotCount || _slots[slot] is not { Enabled: true })
        {
            if (State is DialogueState.Speaking or DialogueState.Choosing)
                Cue(ErrorCue);
            return false;
        }
        _chosen = slot;
        _chosenTime = 0;
        _selectionRaised = false;
        State = DialogueState.Resolving;
        Cue(AcceptCue);
        return true;
    }

    public bool ConfirmFocused() => FocusedSlot >= 0 && Choose(FocusedSlot);

    public void FocusSlot(int slot)
    {
        if (State == DialogueState.Choosing && slot is >= 0 and < SlotCount && _slots[slot] is { Enabled: true })
            FocusedSlot = slot;
    }

    public bool Cancel()
    {
        if (State == DialogueState.Waiting && ShowWaitingNotice)
        {
            Close(DialogueOutcome.Left);
            return true;
        }
        if (State is not (DialogueState.Speaking or DialogueState.Choosing))
            return false;
        if (!_node.CanExit && !StuckChoosing)
        {
            Cue(ErrorCue);
            return false;
        }
        Close(DialogueOutcome.Left);
        return true;
    }

    public bool CancelFromPadB()
    {
        if (State == DialogueState.Choosing && _slots[1] is { Enabled: true, Kind: DialogueChoiceKind.Exit })
            return Choose(1);
        return Cancel();
    }

    public bool Advance(DialogueNode next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (State != DialogueState.Waiting)
            return false;
        Start(next);
        return true;
    }

    public bool Finish()
    {
        if (State is DialogueState.Closing or DialogueState.Ended)
            return false;
        Close(DialogueOutcome.Finished);
        return true;
    }

    public bool PlayChallengeResult(bool success)
    {
        if (State != DialogueState.Waiting)
            return false;
        ChallengeSucceeded = success;
        _challengeTime = 0;
        State = DialogueState.ChallengeResult;
        Cue(success ? AcceptCue : ErrorCue);
        return true;
    }

    public void Abort()
    {
        if (State != DialogueState.Ended)
            End(DialogueOutcome.Aborted);
    }

    private void Start(DialogueNode node)
    {
        _node = node;
        _slots = Layout(node.Choices);
        _speakTime = 0;
        _choosingTime = 0;
        _chosen = -1;
        _waitTime = 0;
        FocusedSlot = -1;
        NodeStarted?.Invoke(node);
        if (node.SpeakingMs > 0)
            State = DialogueState.Speaking;
        else
            EnterChoosing();
    }

    private void EnterChoosing()
    {
        NodeFinished?.Invoke(_node);
        if (_node.ChoicesAfterFinish is { } after)
            _slots = Layout(after());
        if (_slots.All(s => s is null))
        {
            Close(DialogueOutcome.Finished);
            return;
        }
        State = DialogueState.Choosing;
        _choosingTime = 0;
        FocusedSlot = Array.FindIndex(_slots, s => s is { Enabled: true });
    }

    private void RaiseSelection()
    {
        if (_selectionRaised)
            return;
        _selectionRaised = true;
        var kind = _slots[_chosen]!.Kind;
        if (kind == DialogueChoiceKind.Normal)
        {
            State = DialogueState.Waiting;
            _waitTime = 0;
        }
        ChoiceSelected?.Invoke(_node.Id, _chosen);
        if (kind != DialogueChoiceKind.Normal)
            Close(kind switch
            {
                DialogueChoiceKind.Trade => DialogueOutcome.Trade,
                DialogueChoiceKind.Command => DialogueOutcome.Command,
                _ => DialogueOutcome.Left,
            });
    }

    private void Close(DialogueOutcome outcome)
    {
        if (State is DialogueState.Closing or DialogueState.Ended)
            return;
        State = DialogueState.Closing;
        _pending = outcome;
        _closeTime = 0;
        Closing?.Invoke();
    }

    private void End(DialogueOutcome outcome)
    {
        State = DialogueState.Ended;
        Outcome = outcome;
        Ended?.Invoke(outcome);
    }

    private void Cue(string cue) => CueRequested?.Invoke(cue);

    private double AlphaOf(int index, DialogueChoice choice)
    {
        double basis = !choice.Enabled ? DisabledAlpha : choice.Used ? UsedAlpha : 1;
        if (State == DialogueState.Choosing)
            return basis * Math.Clamp(_choosingTime / ChoiceFadeInSeconds, 0, 1);
        if (index != _chosen)
            return basis * Math.Clamp(1 - _chosenTime / OthersFadeOutSeconds, 0, 1);
        return basis * Math.Clamp(1 - Math.Max(0, _chosenTime - ChosenHoldSeconds) / ChosenFadeSeconds, 0, 1);
    }

    private static double Envelope(double t) =>
        t < 0.15 ? t / 0.15 : t < 0.85 ? 1 : Math.Clamp((ChallengeSeconds - t) / 0.15, 0, 1);

    private static DialogueChoice?[] Layout(IReadOnlyList<DialogueChoice>? choices)
    {
        var slots = new DialogueChoice?[SlotCount];
        var list = (choices ?? Array.Empty<DialogueChoice>()).Where(c => c is not null).Take(DialogueNode.MaxChoices).ToList();
        var rest = new List<(int Preferred, DialogueChoice Choice)>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Slot is { } s && s is >= 0 and < SlotCount && slots[s] is null)
                slots[s] = list[i];
            else
                rest.Add((i, list[i]));
        }
        foreach (var (preferred, choice) in rest)
        {
            int target = slots[preferred] is null ? preferred : Array.FindIndex(slots, s => s is null);
            if (target >= 0)
                slots[target] = choice;
        }
        return slots;
    }
}
