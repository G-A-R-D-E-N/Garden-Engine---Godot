using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum DeathChoice
{
    LoadLast,
    LoadOther,
    MainMenu,
    Quit,
}

public enum DeathPhase
{
    Intro,
    Interactive,
    Confirming,
    Leaving,
    Done,
    Hidden,
}

public sealed record DeathInfo(string CauseText = "", bool HasLastSave = false, string LastSaveLabel = "",
                               bool CanLoadOther = false, double AutoReloadSeconds = 0);

public readonly record struct DeathAction(DeathChoice Choice, string Label, bool Enabled, bool Focused);

public sealed class DeathScreenModel
{
    public const double TintSeconds = 1.2;
    public const double TintMaxAlpha = 0.7;
    public const double TitleFadeSeconds = 0.4;
    public const double CauseAt = 2.0;
    public const double ActionsAt = 2.8;
    public const double ActionsFadeSeconds = 0.3;
    public const double LeaveFadeSeconds = 0.4;
    public const double FailureSeconds = 5;
    public const string WarnCue = "ui.warn";
    public const string OpenCue = "ui.open";
    public const string AcceptCue = "ui.accept";
    public const string ErrorCue = "ui.error";

    private static readonly DeathChoice[] Order = [DeathChoice.LoadLast, DeathChoice.LoadOther, DeathChoice.MainMenu, DeathChoice.Quit];
    private static readonly string[] Labels = ["Load last save", "Load game", "Main menu", "Quit game"];

    private readonly Func<DeathChoice, string?> _perform;
    private double _elapsed;
    private double _leave;
    private double _failure;
    private double _auto;
    private bool _warned;
    private bool _opened;
    private bool _skipped;
    private bool _autoCancelled;
    private int _pendingIndex = -1;
    private DeathChoice _chosen;

    public DeathScreenModel(DeathInfo info, Func<DeathChoice, string?> perform)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(perform);
        Info = info;
        _perform = perform;
        FocusedIndex = -1;
        _auto = double.IsFinite(info.AutoReloadSeconds) ? Math.Max(0, info.AutoReloadSeconds) : 0;
    }

    public event Action<string>? CueRequested;

    public event Action<DeathChoice>? Chosen;

    public DeathInfo Info { get; }

    public DeathPhase Phase { get; private set; }

    public int FocusedIndex { get; private set; }

    public string FailureMessage { get; private set; } = "";

    public DeathChoice? PendingConfirm => Phase == DeathPhase.Confirming ? Order[_pendingIndex] : null;

    public bool TreePaused => Phase != DeathPhase.Hidden && _elapsed >= TintSeconds;

    public double TintAlpha => Math.Clamp(_elapsed / TintSeconds, 0, 1) * TintMaxAlpha;

    public double TitleAlpha => Math.Clamp((_elapsed - TintSeconds) / TitleFadeSeconds, 0, 1);

    public double CauseAlpha => CauseLine.Length == 0 ? 0 : Math.Clamp((_elapsed - CauseAt) / TitleFadeSeconds, 0, 1);

    public double ActionsAlpha => Phase is DeathPhase.Leaving or DeathPhase.Done or DeathPhase.Hidden
        ? 0
        : Math.Clamp((_elapsed - ActionsAt) / ActionsFadeSeconds, 0, 1);

    public double CurtainAlpha => Phase == DeathPhase.Leaving ? Math.Clamp(_leave / LeaveFadeSeconds, 0, 1) : Phase == DeathPhase.Done ? 1 : 0;

    public string CauseLine => FailureMessage.Length > 0 ? FailureMessage : Info.CauseText ?? "";

    public string LastSaveLabel => Info.HasLastSave ? Info.LastSaveLabel : "";

    public bool CountdownActive => !_autoCancelled && _auto > 0 && Info.HasLastSave && _opened && Phase == DeathPhase.Interactive;

    public double AutoReloadRemaining => CountdownActive ? _auto : 0;

    public IReadOnlyList<DeathAction> Actions
    {
        get
        {
            var list = new DeathAction[Order.Length];
            for (int i = 0; i < list.Length; i++)
                list[i] = new DeathAction(Order[i], Labels[i], IsEnabled(i), i == FocusedIndex && _opened);
            return list;
        }
    }

    public void Tick(double dt)
    {
        if (Phase is DeathPhase.Hidden or DeathPhase.Done)
            return;
        if (!_warned)
        {
            _warned = true;
            Cue(WarnCue);
        }
        if (!double.IsFinite(dt) || dt <= 0)
            return;
        if (Phase == DeathPhase.Leaving)
        {
            _leave += dt;
            if (_leave >= LeaveFadeSeconds)
                Finish();
            return;
        }
        _elapsed += dt;
        if (!_opened && _elapsed >= ActionsAt)
            Open();
        if (FailureMessage.Length > 0 && _opened && (_failure += dt) >= FailureSeconds)
            FailureMessage = "";
        if (CountdownActive && (_auto -= dt) <= 0)
        {
            _autoCancelled = true;
            Activate(0);
        }
    }

    public bool Skip()
    {
        if (_opened || _skipped || Phase != DeathPhase.Intro)
            return false;
        _skipped = true;
        _elapsed = Math.Max(_elapsed, ActionsAt + ActionsFadeSeconds);
        Open();
        return true;
    }

    public bool Confirm()
    {
        if (Skip())
            return true;
        return Phase == DeathPhase.Interactive && FocusedIndex >= 0 && Activate(FocusedIndex);
    }

    public void Cancel()
    {
        if (Phase is DeathPhase.Hidden or DeathPhase.Done or DeathPhase.Leaving)
            return;
        if (Phase == DeathPhase.Confirming)
        {
            ResolveConfirm(false);
            return;
        }
        Cue(ErrorCue);
    }

    public void Move(int delta)
    {
        if (Phase != DeathPhase.Interactive || !_opened || delta == 0)
            return;
        _autoCancelled = true;
        int step = Math.Sign(delta);
        for (int i = FocusedIndex + step; i is >= 0 and < 4; i += step)
        {
            if (IsEnabled(i))
            {
                FocusedIndex = i;
                return;
            }
        }
    }

    public void Hover(int index)
    {
        if (Phase != DeathPhase.Interactive || !_opened || index is < 0 or >= 4 || !IsEnabled(index))
            return;
        _autoCancelled = true;
        FocusedIndex = index;
    }

    public bool Activate(int index)
    {
        if (Phase != DeathPhase.Interactive || !_opened || index is < 0 or >= 4 || !IsEnabled(index))
            return false;
        bool asks = Order[index] is DeathChoice.MainMenu or DeathChoice.Quit;
        if (asks && ActionsAlpha < 1)
            return false;
        _autoCancelled = true;
        FocusedIndex = index;
        if (asks)
        {
            _pendingIndex = index;
            Phase = DeathPhase.Confirming;
            return true;
        }
        Commit(index);
        return true;
    }

    public bool ResolveConfirm(bool accepted)
    {
        if (Phase != DeathPhase.Confirming)
            return false;
        int index = _pendingIndex;
        _pendingIndex = -1;
        Phase = DeathPhase.Interactive;
        FocusedIndex = index;
        if (accepted)
            Commit(index);
        return true;
    }

    public void Failed(string message)
    {
        if (Phase is DeathPhase.Hidden)
            return;
        FailureMessage = string.IsNullOrWhiteSpace(message) ? "Failed" : message;
        _failure = 0;
        _leave = 0;
        Phase = DeathPhase.Interactive;
        Cue(ErrorCue);
    }

    public bool Hide()
    {
        if (Phase == DeathPhase.Hidden)
            return false;
        Phase = DeathPhase.Hidden;
        return true;
    }

    private bool IsEnabled(int index) => Order[index] switch
    {
        DeathChoice.LoadLast => Info.HasLastSave,
        DeathChoice.LoadOther => Info.CanLoadOther,
        _ => true,
    };

    private void Open()
    {
        _opened = true;
        Phase = DeathPhase.Interactive;
        for (int i = 0; i < 4 && FocusedIndex < 0; i++)
            if (IsEnabled(i))
                FocusedIndex = i;
        Cue(OpenCue);
    }

    private void Commit(int index)
    {
        _chosen = Order[index];
        _leave = 0;
        Phase = DeathPhase.Leaving;
        Cue(AcceptCue);
        Chosen?.Invoke(_chosen);
    }

    private void Finish()
    {
        string? error = _perform(_chosen);
        if (Phase == DeathPhase.Hidden)
            return;
        if (error is null)
            Phase = DeathPhase.Done;
        else
            Failed(error);
    }

    private void Cue(string cue) => CueRequested?.Invoke(cue);
}
