using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Hud;

public enum HudGroup
{
    Vitals,
    Ammo,
    Compass,
    Centre,
    Effects,
    PowerArmor,
}

public readonly record struct HudStatusChip(string Id, string Text);

public static class HudText
{
    public const int MaxAmmo = 999;

    public static string Ammo(int count) => Math.Clamp(count, 0, MaxAmmo).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class HudInstruments
{
    public const int MaxEffects = 8;
    public const int MaxChips = 4;
    public const int MaxCharges = 3;
    public const double EnemyFadeInSeconds = 0.15;
    public const double EnemyFadeOutSeconds = 0.3;
    public const double MaxSneakGap = 120;
    public const double SneakStepSeconds = 0.033;
    public const double CriticalFlashHz = 2;
    public const double CriticalLowAlpha = 0.4;

    private readonly List<string> _effects = new();
    private readonly List<HudStatusChip> _chips = new();
    private readonly HashSet<HudGroup> _hidden = new();
    private double[] _segments = Array.Empty<double>();
    private double _enemyAlpha;
    private bool _enemyShown;
    private double _sneakTarget;
    private double _sneakClock;
    private double _criticalClock;

    public double Radiation { get; private set; }

    public int RadiationPercent => (int)Math.Round(Radiation * 100);

    public int? SecondaryCount { get; private set; }

    public IReadOnlyList<string> Effects => _effects;

    public IReadOnlyList<HudStatusChip> Chips => _chips;

    public string EnemyName { get; private set; } = "";

    public double EnemyHealth { get; private set; }

    public int? EnemyLevel { get; private set; }

    public bool EnemyLegendary { get; private set; }

    public double EnemyAlpha => _enemyAlpha;

    public string SneakText { get; private set; } = "";

    public double SneakGap { get; private set; }

    public double CriticalFraction { get; private set; }

    public int CriticalCharges { get; private set; }

    public bool CriticalReady => CriticalVisible && CriticalCharges > 0;

    public bool CriticalVisible { get; private set; }

    public double CriticalAlpha => !CriticalReady
        ? 1
        : CriticalLowAlpha + (1 - CriticalLowAlpha) * (0.5 + 0.5 * Math.Cos(2 * Math.PI * CriticalFlashHz * _criticalClock));

    public double? ActionPointPreview { get; private set; }

    public IReadOnlyList<double> PreviewSegments => _segments;

    public bool IsGroupVisible(HudGroup group) => !_hidden.Contains(group);

    public void SetGroupVisible(HudGroup group, bool visible)
    {
        if (visible)
            _hidden.Remove(group);
        else
            _hidden.Add(group);
    }

    public void SetRadiation(double fraction)
    {
        if (double.IsFinite(fraction) && fraction >= 0)
            Radiation = Math.Min(fraction, 1);
    }

    public void SetSecondaryCount(int? count)
    {
        if (count is null)
            SecondaryCount = null;
        else if (count >= 0)
            SecondaryCount = count;
    }

    public void SetEffects(IReadOnlyList<string>? effects)
    {
        _effects.Clear();
        if (effects is null)
            return;
        foreach (string effect in effects)
        {
            string line = (effect ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (line.Length > 0 && _effects.Count < MaxEffects)
                _effects.Add(line);
        }
    }

    public bool SetStatus(string id, string text)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
            return false;
        int index = _chips.FindIndex(c => c.Id == id);
        if (index >= 0)
        {
            _chips[index] = new HudStatusChip(id, text.Trim());
            return true;
        }
        if (_chips.Count >= MaxChips)
            return false;
        _chips.Add(new HudStatusChip(id, text.Trim()));
        return true;
    }

    public bool ClearStatus(string id) => _chips.RemoveAll(c => c.Id == id) > 0;

    public void SetEnemy(string? name, double health01)
    {
        if (!double.IsFinite(health01))
            return;
        string next = name ?? "";
        if (!_enemyShown || next != EnemyName)
        {
            EnemyLevel = null;
            EnemyLegendary = false;
        }
        _enemyShown = true;
        EnemyName = next;
        EnemyHealth = Math.Clamp(health01, 0, 1);
    }

    public void SetEnemyDetails(int? level, bool legendary)
    {
        EnemyLevel = level is < 0 ? null : level;
        EnemyLegendary = legendary;
    }

    public void ClearEnemy() => _enemyShown = false;

    public void SetSneak(string? state, double detection, bool snap = false)
    {
        SneakText = state?.Trim() ?? "";
        if (!double.IsFinite(detection))
            return;
        _sneakTarget = Math.Clamp(detection, 0, 100) / 100 * MaxSneakGap;
        if (snap || SneakText.Length == 0)
            SneakGap = _sneakTarget;
    }

    public void SetCritical(double fraction, int charges)
    {
        if (!double.IsFinite(fraction))
            return;
        CriticalFraction = Math.Clamp(fraction, 0, 1);
        CriticalCharges = Math.Clamp(charges, 0, MaxCharges);
        CriticalVisible = true;
    }

    public void ClearCritical()
    {
        CriticalVisible = false;
        CriticalFraction = 0;
        CriticalCharges = 0;
    }

    public void SetActionPointPreview(double? projectedCurrent, IReadOnlyList<double>? segmentFractions)
    {
        if (projectedCurrent is not { } projected || !double.IsFinite(projected) || projected < 0)
        {
            ActionPointPreview = null;
            _segments = Array.Empty<double>();
            return;
        }
        ActionPointPreview = projected;
        _segments = (segmentFractions ?? Array.Empty<double>()).Where(double.IsFinite).Select(f => Math.Clamp(f, 0, 1)).ToArray();
    }

    public void Tick(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt))
            return;
        _enemyAlpha = _enemyShown
            ? Math.Min(1, _enemyAlpha + dt / EnemyFadeInSeconds)
            : Math.Max(0, _enemyAlpha - dt / EnemyFadeOutSeconds);
        _criticalClock = (_criticalClock + dt) % (1 / CriticalFlashHz);
        _sneakClock += dt;
        while (_sneakClock >= SneakStepSeconds)
        {
            _sneakClock -= SneakStepSeconds;
            double diff = _sneakTarget - SneakGap;
            double step = Math.Clamp(Math.Ceiling(Math.Abs(diff) / 30), 1, 4);
            SneakGap = Math.Abs(diff) <= step ? _sneakTarget : SneakGap + Math.Sign(diff) * step;
        }
    }
}
