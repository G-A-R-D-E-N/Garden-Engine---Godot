using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Character;

public enum CrossMode { Assign, Quick }

public sealed record FavoriteEntry(string Name, int Count = 1, string? AmmoName = null, int AmmoCount = 0, string KeyLabel = "",
                                   bool Available = true, bool Equipped = false);

public sealed class FavouritesCrossModel
{
    public const int SlotCount = 12;
    public const double AssignHoldSeconds = 0.45;
    public const double StickThreshold = 0.35;
    public const string AssignFailedText = "inventory changed, select the item again";

    public static readonly IReadOnlyList<string> KeyLabels = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

    private FavoriteEntry?[] _slots;
    private int? _highlight;
    private double _closeLeft;

    private FavouritesCrossModel(CrossMode mode, FavoriteEntry?[] slots)
    {
        Mode = mode;
        _slots = slots;
    }

    public event Action<string>? SoundRequested;

    public event Action<int, FavoriteEntry>? Used;

    public event Action<int>? Assigned;

    public event Action? Closed;

    public event Action? Changed;

    public CrossMode Mode { get; }

    public bool IsClosed { get; private set; }

    public int? Highlighted => _highlight;

    public string Message { get; private set; } = string.Empty;

    public FavoriteEntry? HighlightedEntry => _highlight is { } h ? _slots[h - 1] : null;

    public string NameLine => HighlightedEntry is { } e
        ? string.Create(CultureInfo.InvariantCulture, $"{e.Name} ({e.Count})") + (e.Equipped ? "  Equipped" : string.Empty)
        : string.Empty;

    public string AmmoLine => HighlightedEntry is { AmmoName: { Length: > 0 } ammo } e
        ? string.Create(CultureInfo.InvariantCulture, $"{ammo} ({e.AmmoCount})")
        : string.Empty;

    public static FavouritesCrossModel? TryOpen(CrossMode mode, IReadOnlyList<FavoriteEntry?>? slots, bool anotherModalOpen,
                                                bool treePaused, Action<string>? sound = null)
    {
        if (slots is null || slots.Count != SlotCount || anotherModalOpen || treePaused) return null;
        if (mode == CrossMode.Quick && slots.All(s => s is null))
        {
            sound?.Invoke("ui.error");
            return null;
        }
        return new FavouritesCrossModel(mode, slots.ToArray());
    }

    public static int ArmOf(int slot) => (slot - 1) / 3;

    public static int RingOf(int slot)
    {
        var pos = (slot - 1) % 3;
        return ArmOf(slot) is 0 or 2 ? 3 - pos : pos + 1;
    }

    public static string LabelOf(int slot) => KeyLabels[slot - 1];

    public static int SlotAt(int arm, int ring) => arm switch
    {
        0 => 4 - ring,
        1 => 3 + ring,
        2 => 10 - ring,
        _ => 9 + ring,
    };

    public FavoriteEntry? Slot(int slot) => slot is >= 1 and <= SlotCount ? _slots[slot - 1] : null;

    public bool IsVisible(int slot)
    {
        if (slot < 1 || slot > SlotCount) return false;
        if (Mode == CrossMode.Assign || RingOf(slot) == 1) return true;
        for (var ring = RingOf(slot); ring <= 3; ring++)
        {
            if (_slots[SlotAt(ArmOf(slot), ring) - 1] is not null) return true;
        }
        return false;
    }

    public bool Select(int slot)
    {
        if (IsClosed || !IsVisible(slot)) return false;
        SetHighlight(slot);
        return true;
    }

    public bool Handle(MenuInputAction action)
    {
        if (IsClosed) return false;
        switch (action)
        {
            case MenuInputAction.Up:
            case MenuInputAction.Down:
            case MenuInputAction.Left:
            case MenuInputAction.Right: Move(action); return true;
            case MenuInputAction.Accept: Activate(); return true;
            case MenuInputAction.Back: Cancel(); return true;
            default: return false;
        }
    }

    public void Move(MenuInputAction direction)
    {
        if (IsClosed) return;
        if (Target(direction) is { } slot && IsVisible(slot)) SetHighlight(slot);
    }

    public bool Stick(double right, double up)
    {
        if (IsClosed || !double.IsFinite(right) || !double.IsFinite(up)) return false;
        var deflection = Math.Sqrt(right * right + up * up);
        if (deflection < StickThreshold) return false;
        var arm = Math.Abs(right) >= Math.Abs(up) ? (right > 0 ? 1 : 0) : (up > 0 ? 2 : 3);
        var ring = deflection > 0.9 ? 3 : deflection > 0.65 ? 2 : 1;
        for (; ring >= 1; ring--)
        {
            var slot = SlotAt(arm, ring);
            if (!IsVisible(slot)) continue;
            SetHighlight(slot);
            return true;
        }
        return false;
    }

    public bool Wheel(int direction)
    {
        if (IsClosed || direction == 0) return false;
        var filled = Enumerable.Range(1, SlotCount).Where(s => IsVisible(s) && _slots[s - 1] is not null).ToList();
        if (filled.Count == 0) return false;
        int next;
        if (_highlight is not { } current) next = direction > 0 ? filled[0] : filled[^1];
        else if (direction > 0) next = filled.FirstOrDefault(s => s > current, filled[0]);
        else next = filled.LastOrDefault(s => s < current, filled[^1]);
        SetHighlight(next);
        return true;
    }

    public void Activate()
    {
        if (IsClosed || _highlight is not { } slot) return;
        var entry = _slots[slot - 1];
        if (Mode == CrossMode.Assign)
        {
            Sound("ui.accept");
            Assigned?.Invoke(slot);
            if (_closeLeft <= 0) _closeLeft = AssignHoldSeconds;
            Raise();
            return;
        }
        if (entry is null || !entry.Available)
        {
            Sound("ui.error");
            return;
        }
        Sound("ui.accept");
        Used?.Invoke(slot, entry);
        Close();
    }

    public void FailAssign()
    {
        if (IsClosed) return;
        Message = AssignFailedText;
        Close();
    }

    public void Cancel() => Close();

    public void Tick(double seconds)
    {
        if (IsClosed || _closeLeft <= 0 || !(seconds > 0)) return;
        _closeLeft -= seconds;
        if (_closeLeft <= 0) Close();
    }

    public bool SetSlots(IReadOnlyList<FavoriteEntry?>? slots)
    {
        if (IsClosed || slots is null || slots.Count != SlotCount) return false;
        _slots = slots.ToArray();
        if (Mode == CrossMode.Quick)
        {
            if (_slots.All(s => s is null))
            {
                Close();
                return true;
            }
            if (_highlight is { } h && (_slots[h - 1] is not { Available: true } || !IsVisible(h))) _highlight = Nearest(h);
        }
        Raise();
        return true;
    }

    private int? Nearest(int from) =>
        Enumerable.Range(1, SlotCount).Where(s => _slots[s - 1] is { Available: true } && IsVisible(s))
            .OrderBy(s => Math.Abs(s - from)).ThenBy(s => s).Select(s => (int?)s).FirstOrDefault();

    private int? Target(MenuInputAction direction)
    {
        if (_highlight is not { } h)
        {
            return direction switch
            {
                MenuInputAction.Up => 9,
                MenuInputAction.Down => 10,
                MenuInputAction.Left => 3,
                _ => 4,
            };
        }
        var arm = ArmOf(h);
        switch (direction)
        {
            case MenuInputAction.Up:
                if (arm is 0 or 1) return 9;
                if (arm == 2) return h > 7 ? h - 1 : null;
                return h > 10 ? h - 1 : 9;
            case MenuInputAction.Down:
                if (arm is 0 or 1) return 10;
                if (arm == 3) return h < 12 ? h + 1 : null;
                return h < 9 ? h + 1 : 10;
            case MenuInputAction.Left:
                if (arm >= 2) return 3;
                if (arm == 0) return h > 1 ? h - 1 : null;
                return h > 4 ? h - 1 : 3;
            default:
                if (arm >= 2) return 4;
                if (arm == 1) return h < 6 ? h + 1 : null;
                return h < 3 ? h + 1 : 4;
        }
    }

    private void SetHighlight(int slot)
    {
        if (_highlight == slot) return;
        _highlight = slot;
        Sound(string.Create(CultureInfo.InvariantCulture, $"ui.focus.{RingOf(slot)}"));
        Raise();
    }

    private void Close()
    {
        if (IsClosed) return;
        IsClosed = true;
        Closed?.Invoke();
        Raise();
    }

    private void Sound(string name) => SoundRequested?.Invoke(name);

    private void Raise() => Changed?.Invoke();
}
