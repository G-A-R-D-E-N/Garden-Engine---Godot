using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class MenuInputDriver
{
    private static readonly IReadOnlyList<MenuInputAction> NoActions = Array.Empty<MenuInputAction>();

    private readonly MenuInputMapper _mapper = new();
    private readonly MenuInputRepeater _repeater;
    private readonly Dictionary<MenuInputAction, int> _down = new();

    public MenuInputDriver(MenuRepeatTiming? timing = null) => _repeater = new MenuInputRepeater(timing);

    public MenuDevice LastDevice { get; private set; } = MenuDevice.Keyboard;

    public bool UsingController => LastDevice == MenuDevice.Controller;

    public IReadOnlyList<MenuInputAction> Handle(MenuInputEvent e)
    {
        var changes = _mapper.Map(e);
        if (changes.Count == 0) return NoActions;
        LastDevice = e.Device;
        List<MenuInputAction>? actions = null;
        foreach (var change in changes)
        {
            switch (change.Phase)
            {
                case MenuPhase.Pulse:
                    (actions ??= new()).Add(change.Action);
                    break;
                case MenuPhase.Press:
                    _down[change.Action] = _down.GetValueOrDefault(change.Action) + 1;
                    if (_down[change.Action] == 1)
                    {
                        (actions ??= new()).Add(change.Action);
                        _repeater.Press(change.Action);
                    }
                    break;
                case MenuPhase.Release:
                    Release(change.Action);
                    break;
            }
        }
        return actions ?? NoActions;
    }

    public IReadOnlyList<MenuInputAction> Advance(double delta) => _repeater.Advance(delta);

    public void NoteDevice(MenuDevice device) => LastDevice = device;

    public void Reset()
    {
        _mapper.Reset();
        _repeater.Clear();
        _down.Clear();
    }

    private void Release(MenuInputAction action)
    {
        if (!_down.TryGetValue(action, out var count)) return;
        if (count > 1)
        {
            _down[action] = count - 1;
            return;
        }
        _down.Remove(action);
        _repeater.Release(action);
    }
}
