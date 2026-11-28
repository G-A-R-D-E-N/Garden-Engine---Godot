using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed class MenuStack
{
    private readonly List<IMenu> _menus = new();
    private readonly Queue<Action> _pending = new();
    private int _depth;

    public event Action<IMenu?, IMenu?>? TopChanged;

    public IMenu? Top => _menus.Count > 0 ? _menus[^1] : null;

    public int Count => _menus.Count;

    public IReadOnlyList<IMenu> Menus => _menus;

    public bool Contains(IMenu menu) => _menus.Contains(menu);

    // A call made from inside a menu callback or a TopChanged handler is queued and applied in order once the outer call finishes; it then returns true even if the queued call is later refused.
    public bool Push(IMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return Run(() => PushNow(menu));
    }

    public bool Pop() => Run(PopNow);

    public bool Replace(IMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return Run(() => ReplaceNow(menu));
    }

    public bool Remove(IMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return Run(() => RemoveNow(menu));
    }

    public bool Clear() => Run(ClearNow);

    public bool Dispatch(MenuInputAction action) => Run(() => DispatchNow(action));

    private bool Run(Func<bool> operation)
    {
        if (_depth > 0)
        {
            _pending.Enqueue(() => operation());
            return true;
        }
        _depth++;
        try
        {
            var result = operation();
            while (_pending.Count > 0) _pending.Dequeue()();
            return result;
        }
        catch
        {
            _pending.Clear();
            throw;
        }
        finally
        {
            _depth--;
        }
    }

    private bool PushNow(IMenu menu)
    {
        if (_menus.Contains(menu)) return false;
        var below = Top;
        if (below is { IsModal: true } && !menu.IsModal) return false;
        _menus.Add(menu);
        var covered = false;
        try
        {
            below?.OnCovered();
            covered = below is not null;
            menu.OnOpened();
        }
        catch
        {
            _menus.Remove(menu);
            if (covered) below!.OnUncovered();
            throw;
        }
        TopChanged?.Invoke(below, menu);
        return true;
    }

    private bool PopNow()
    {
        if (_menus.Count == 0) return false;
        var closed = _menus[^1];
        _menus.RemoveAt(_menus.Count - 1);
        closed.OnClosed();
        var now = Top;
        now?.OnUncovered();
        TopChanged?.Invoke(closed, now);
        return true;
    }

    private bool ReplaceNow(IMenu menu)
    {
        if (_menus.Count == 0) return PushNow(menu);
        var old = _menus[^1];
        if (_menus.Contains(menu)) return false;
        if (old.IsModal && !menu.IsModal) return false;
        _menus[^1] = menu;
        old.OnClosed();
        menu.OnOpened();
        TopChanged?.Invoke(old, menu);
        return true;
    }

    private bool RemoveNow(IMenu menu)
    {
        var index = _menus.IndexOf(menu);
        if (index < 0) return false;
        if (index == _menus.Count - 1) return PopNow();
        _menus.RemoveAt(index);
        menu.OnClosed();
        return true;
    }

    private bool ClearNow()
    {
        if (_menus.Count == 0) return false;
        var oldTop = _menus[^1];
        while (_menus.Count > 0)
        {
            var closed = _menus[^1];
            _menus.RemoveAt(_menus.Count - 1);
            closed.OnClosed();
        }
        TopChanged?.Invoke(oldTop, Top);
        return true;
    }

    private bool DispatchNow(MenuInputAction action)
    {
        var top = Top;
        if (top is null) return false;
        if (top.HandleAction(action)) return true;
        if (action != MenuInputAction.Back || !top.CloseOnBack) return false;
        if (_pending.Count > 0 || !ReferenceEquals(Top, top)) return false;
        return PopNow();
    }
}
