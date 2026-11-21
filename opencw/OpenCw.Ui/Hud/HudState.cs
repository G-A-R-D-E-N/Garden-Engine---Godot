using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum CrosshairState
{
    Hidden,
    Default,
    Sneaking,
    Hostile,
    Friendly,
    Interactable,
}

public enum CompassMarkerKind
{
    Objective,
    Location,
    Hostile,
    Friendly,
    Custom,
}

// Bearing is degrees clockwise from north. Pinned markers stay on the compass edge when off range.
public readonly record struct CompassMarker(string Id, double Bearing, CompassMarkerKind Kind, bool Pinned = false);

public readonly record struct InteractionPrompt(string Verb, string Target, string KeyLabel, double HoldProgress, string Icon = "", string Notice = "");

public readonly record struct AmmoReadout(bool HasWeapon, int Clip, int Reserve, bool Infinite);

public sealed class HudState
{
    public const double DamageFlashSeconds = 0.6;
    public const double ObjectiveBannerSeconds = 6;
    public const double ActionPointsDimDelaySeconds = 2;
    public const double ActionPointsDimAlpha = 0.5;

    private readonly List<CompassMarker> _markers = new();
    private bool _healthKnown;
    private double _flashTime;
    private double _flashIntensity;
    private double _bannerTime;
    private double _apIdle;
    private readonly HashSet<int> _hideTokens = new();
    private bool _forceHidden;
    private int _lastHideToken;

    public HudSettings Settings { get; } = new();

    public HudInstruments Instruments { get; } = new();

    public PowerArmorHudModel PowerArmor { get; } = new();

    public ScopeOverlayModel Scope { get; } = new();

    public bool Hidden
    {
        get => _forceHidden || _hideTokens.Count > 0;
        set => _forceHidden = value;
    }

    public double HealthCurrent { get; private set; } = 1;
    public double HealthMax { get; private set; } = 1;
    public double ActionPoints { get; private set; } = 1;
    public double ActionPointsMax { get; private set; } = 1;
    public AmmoReadout Ammo { get; private set; }
    public double CompassHeading { get; private set; }
    public IReadOnlyList<CompassMarker> Markers => _markers;
    public string ObjectiveText { get; private set; } = "";
    public CrosshairState Crosshair { get; set; } = CrosshairState.Default;
    public InteractionPrompt? Interaction { get; private set; }

    public double HealthFraction => Fraction(HealthCurrent, HealthMax);

    public double ActionPointsFraction => Fraction(ActionPoints, ActionPointsMax);

    public double DamageFlashAlpha => _flashTime <= 0 ? 0 : _flashTime / DamageFlashSeconds * _flashIntensity;

    public double ObjectiveEmphasis => Math.Min(1, _bannerTime);

    public double ActionPointsAlpha =>
        ActionPointsMax > 0 && ActionPoints >= ActionPointsMax && _apIdle >= ActionPointsDimDelaySeconds ? ActionPointsDimAlpha : 1;

    public bool PowerArmorActive => PowerArmor.Visible && !Hidden && Instruments.IsGroupVisible(HudGroup.PowerArmor);

    public bool IsPowerArmorDrawn => PowerArmor.Alpha > 0 && !Hidden && Instruments.IsGroupVisible(HudGroup.PowerArmor);

    public double EffectsOffsetX => PowerArmorActive ? PowerArmor.EffectsOffsetX : 0;

    public double EffectsOffsetY => PowerArmorActive ? PowerArmor.EffectsOffsetY : 0;

    public bool IsScopeDrawn => Scope.Alpha > 0 && !Hidden;

    public int PushHidden()
    {
        int token = ++_lastHideToken;
        _hideTokens.Add(token);
        return token;
    }

    public void PopHidden(int token) => _hideTokens.Remove(token);

    public void SetHealth(double current, double max)
    {
        if (!double.IsFinite(current) || !double.IsFinite(max)) return;
        max = Math.Max(0, max);
        current = Math.Clamp(current, 0, max);
        double dropped = _healthKnown && HealthMax > 0 ? (HealthCurrent - current) / HealthMax : 0;
        HealthCurrent = current;
        HealthMax = max;
        _healthKnown = true;
        if (dropped > 0) FlashDamage(dropped);
    }

    public void FlashDamage(double fractionOfHealth)
    {
        if (double.IsNaN(fractionOfHealth)) return;
        double intensity = Math.Clamp(0.3 + 2 * Math.Max(0, fractionOfHealth), 0.3, 1);
        _flashIntensity = Math.Max(intensity, DamageFlashAlpha);
        _flashTime = DamageFlashSeconds;
    }

    public void SetActionPoints(double current, double max)
    {
        if (!double.IsFinite(current) || !double.IsFinite(max)) return;
        max = Math.Max(0, max);
        double next = Math.Clamp(current, 0, max);
        if (next != ActionPoints || max != ActionPointsMax) _apIdle = 0;
        ActionPoints = next;
        ActionPointsMax = max;
    }

    public void SetAmmo(int clip, int reserve, bool infinite = false) =>
        Ammo = new AmmoReadout(true, Math.Max(0, clip), Math.Max(0, reserve), infinite);

    public void ClearAmmo() => Ammo = default;

    public void SetHeading(double degrees)
    {
        if (!double.IsFinite(degrees)) return;
        CompassHeading = CompassModel.Normalize360(degrees);
    }

    public void SetMarkers(IReadOnlyList<CompassMarker> markers)
    {
        _markers.Clear();
        foreach (var marker in markers)
            if (double.IsFinite(marker.Bearing)) _markers.Add(marker);
    }

    public void SetObjective(string? text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed == ObjectiveText) return;
        ObjectiveText = trimmed;
        _bannerTime = trimmed.Length > 0 ? ObjectiveBannerSeconds : 0;
    }

    public void SetInteraction(string verb, string target, string keyLabel, double holdProgress = 0, string icon = "", string notice = "") =>
        Interaction = new InteractionPrompt(verb, target, keyLabel, double.IsFinite(holdProgress) ? Math.Clamp(holdProgress, 0, 1) : 0, icon, notice);

    public void ClearInteraction() => Interaction = null;

    public bool IsShown(HudElement element)
    {
        if (!Settings.IsEnabled(element)) return false;
        if (GroupOf(element) is { } group && !Instruments.IsGroupVisible(group)) return false;
        if (Hidden) return element == HudElement.Subtitles;
        return element switch
        {
            HudElement.Ammo => Ammo.HasWeapon,
            HudElement.Crosshair => Crosshair != CrosshairState.Hidden,
            HudElement.Objective => ObjectiveText.Length > 0,
            HudElement.Interaction => Interaction.HasValue,
            _ => true,
        };
    }

    public void Tick(double dt)
    {
        if (!(dt > 0)) return;
        _flashTime = Math.Max(0, _flashTime - dt);
        _bannerTime = Math.Max(0, _bannerTime - dt);
        _apIdle += dt;
        Instruments.Tick(dt);
        PowerArmor.Tick(dt);
        Scope.Tick(dt);
    }

    private static HudGroup? GroupOf(HudElement element) => element switch
    {
        HudElement.Health or HudElement.ActionPoints => HudGroup.Vitals,
        HudElement.Ammo => HudGroup.Ammo,
        HudElement.Compass => HudGroup.Compass,
        HudElement.Crosshair or HudElement.Interaction => HudGroup.Centre,
        _ => null,
    };

    private static double Fraction(double current, double max) => max > 0 ? Math.Clamp(current / max, 0, 1) : 0;
}
