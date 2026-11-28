using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Hud;

public readonly record struct PowerArmorPartView(PowerArmorPartId Id, bool Present, double Fill, bool Broken, bool Warn);

public sealed class PowerArmorHudModel
{
    public const int PartCount = 6;
    public const double FadeInSeconds = 0.3;
    public const double FadeOutSeconds = 0.2;
    public const double WarningFadeOutSeconds = 0.2;
    public const double WarnRepeatSeconds = 20;
    public const double PulseHz = 1;
    public const double PulseLowAlpha = 0.4;
    public const double LowConditionFraction = 0.25;
    public const double EffectsShiftX = 50;
    public const double EffectsShiftY = 15;
    public const string WarnCue = "ui.warn";
    public const string ExitRole = "exit";
    public const string ExitToken = "{action:exit}";
    public const string ExitFallback = "Exit";
    public const string NoCoreLabel = "No core";
    public const string DefaultWarning = "Power low";
    public const string NoPowerWarning = "No power";

    private static readonly PowerArmorPartView[] NoParts = Enumerable.Range(0, PartCount)
        .Select(i => new PowerArmorPartView((PowerArmorPartId)i, false, 0, false, false)).ToArray();

    private sealed record View(PowerArmorPartView[] Parts, double Charge, int Minutes, double Threshold, bool HasCore,
        string Name, bool Frame, string Warning)
    {
        public bool WarningActive => !HasCore || Charge <= Threshold;

        public bool SameAs(View other) =>
            Parts.SequenceEqual(other.Parts) && Charge == other.Charge && Minutes == other.Minutes
            && Threshold == other.Threshold && HasCore == other.HasCore && Name == other.Name
            && Frame == other.Frame && Warning == other.Warning;
    }

    private View? _view;
    private Func<string, string?>? _resolver;
    private IPowerArmorSource? _resolverOwner;
    private double _alpha;
    private double _warnFade;
    private double _pulseClock;
    private double _repeatClock;
    private bool _warnActive;

    public event Action<string>? SoundRequested;

    public bool Visible { get; private set; }

    public double Alpha => _alpha;

    public IReadOnlyList<PowerArmorPartView> Parts => _view?.Parts ?? NoParts;

    public string Name => _view?.Name ?? "";

    public bool FrameVisible => Visible && (_view?.Frame ?? false);

    public bool HasCore => _view?.HasCore ?? false;

    public bool NoCore => _view is { HasCore: false };

    public double CoreFill => _view is { HasCore: true } v ? v.Charge : 0;

    public int CorePercent => (int)Math.Round(CoreFill * 100, MidpointRounding.AwayFromZero);

    public string CoreMinutesText => _view is { HasCore: true, Minutes: >= 0 } v ? $"{v.Minutes} min" : "";

    public bool WarningActive => _warnActive;

    public bool WarningVisible => _view is not null && _warnFade > 0;

    public double WarningAlpha => !WarningVisible ? 0
        : _warnFade * _alpha * (PulseLowAlpha + (1 - PulseLowAlpha) * (0.5 + 0.5 * Math.Cos(2 * Math.PI * PulseHz * _pulseClock)));

    public string WarningText
    {
        get
        {
            if (_view is null) return "";
            string raw = _view.Warning.Length > 0 ? _view.Warning : DefaultWarning;
            string bound = _resolver?.Invoke(ExitRole) ?? "";
            return raw.Replace(ExitToken, bound.Length > 0 ? bound : ExitFallback, StringComparison.Ordinal);
        }
    }

    public double EffectsOffsetX => Visible ? EffectsShiftX : 0;

    public double EffectsOffsetY => Visible ? EffectsShiftY : 0;

    public static string PartLabel(PowerArmorPartId id) => id switch
    {
        PowerArmorPartId.Head => "H",
        PowerArmorPartId.Torso => "T",
        PowerArmorPartId.LeftArm => "LA",
        PowerArmorPartId.RightArm => "RA",
        PowerArmorPartId.LeftLeg => "LL",
        PowerArmorPartId.RightLeg => "RL",
        _ => "",
    };

    public void SetBindingResolver(Func<string, string?>? resolver)
    {
        _resolver = resolver;
        _resolverOwner = null;
    }

    public void Sync(IPowerArmorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!ReferenceEquals(_resolverOwner, source))
        {
            _resolverOwner = source;
            _resolver = source.ResolveBinding;
        }
        Apply(source.Current);
    }

    public void Apply(PowerArmorState? state)
    {
        if (state is null)
        {
            Visible = false;
            _warnActive = false;
            _warnFade = 0;
            return;
        }
        var next = Normalise(state);
        if (Visible && _view is not null && next.SameAs(_view)) return;
        _view = next;
        Visible = true;
        SetWarning(next.WarningActive);
    }

    public void Tick(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return;
        _alpha = Visible ? Math.Min(1, _alpha + dt / FadeInSeconds) : Math.Max(0, _alpha - dt / FadeOutSeconds);
        if (!_warnActive)
        {
            _warnFade = Math.Max(0, _warnFade - dt / WarningFadeOutSeconds);
            return;
        }
        _pulseClock = (_pulseClock + dt) % (1 / PulseHz);
        _repeatClock += dt;
        if (_repeatClock < WarnRepeatSeconds - 1e-9) return;
        _repeatClock %= WarnRepeatSeconds;
        SoundRequested?.Invoke(WarnCue);
    }

    private void SetWarning(bool active)
    {
        if (!active)
        {
            _warnActive = false;
            return;
        }
        if (_warnActive) return;
        _warnActive = true;
        _warnFade = 1;
        _pulseClock = 0;
        _repeatClock = 0;
        SoundRequested?.Invoke(WarnCue);
    }

    private static View Normalise(PowerArmorState state)
    {
        var parts = (PowerArmorPartView[])NoParts.Clone();
        foreach (var part in state.Parts ?? Array.Empty<PowerArmorPart>())
        {
            int index = (int)part.Id;
            if (index < 0 || index >= PartCount || parts[index].Present) continue;
            double condition = Clamp01(part.Condition);
            parts[index] = new PowerArmorPartView(part.Id, true, part.IsBroken ? 0 : condition, part.IsBroken,
                part.IsBroken || condition < LowConditionFraction);
        }
        bool hasCore = state.HasCore;
        return new View(parts, hasCore ? Clamp01(state.CoreCharge) : 0, state.CoreMinutesRemaining,
            Clamp01(state.CoreLowThreshold), hasCore, state.Name ?? "", state.FrameVisible, state.WarningText ?? "");
    }

    private static double Clamp01(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
