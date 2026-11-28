using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed record MenuRepeatTiming(double InitialDelay = 0.4, double Interval = 0.08);

public sealed class MenuInputRepeater
{
    private const int MaxFiresPerAdvance = 10;

    private static readonly IReadOnlyList<MenuInputAction> NoActions = Array.Empty<MenuInputAction>();

    private readonly MenuRepeatTiming _timing;
    private readonly List<MenuInputAction> _held = new();
    private double _timeToNext;

    public MenuInputRepeater(MenuRepeatTiming? timing = null) => _timing = timing ?? new MenuRepeatTiming();

    public static bool IsRepeatable(MenuInputAction action) =>
        action is MenuInputAction.Up or MenuInputAction.Down or MenuInputAction.Left or MenuInputAction.Right
            or MenuInputAction.PageUp or MenuInputAction.PageDown;

    public void Press(MenuInputAction action)
    {
        if (!IsRepeatable(action) || _held.Contains(action)) return;
        _held.Add(action);
        _timeToNext = _timing.InitialDelay;
    }

    public void Release(MenuInputAction action)
    {
        var index = _held.LastIndexOf(action);
        if (index < 0) return;
        var wasActive = index == _held.Count - 1;
        _held.RemoveAt(index);
        if (wasActive && _held.Count > 0) _timeToNext = _timing.InitialDelay;
    }

    public void Clear() => _held.Clear();

    public IReadOnlyList<MenuInputAction> Advance(double delta)
    {
        if (_held.Count == 0 || !double.IsFinite(delta) || delta <= 0) return NoActions;
        var active = _held[^1];
        _timeToNext -= delta;
        if (_timeToNext > 0) return NoActions;
        var fired = new List<MenuInputAction>();
        while (_timeToNext <= 0 && fired.Count < MaxFiresPerAdvance)
        {
            fired.Add(active);
            _timeToNext += Math.Max(_timing.Interval, 0.001);
        }
        if (_timeToNext <= 0) _timeToNext = Math.Max(_timing.Interval, 0.001);
        return fired;
    }
}
