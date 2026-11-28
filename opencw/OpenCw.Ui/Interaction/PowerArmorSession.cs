using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

public sealed class PowerArmorFrame(string refKey, string name = "")
{
    public string RefKey { get; } = refKey;

    public string Name { get; set; } = name;

    private readonly double[] _conditions = [.. Enumerable.Repeat(PowerArmorParts.FullCondition, PowerArmorHudModel.PartCount)];
    private IReadOnlyList<PowerArmorPart>? _parts;

    public IReadOnlyList<PowerArmorPart> Parts => _parts ??= Build();

    public double? ConditionOf(PowerArmorPartId id) => _conditions[(int)id] is var c and >= 0 ? c : null;

    // Requires: null removes the piece; 0 is broken.
    public void SetCondition(PowerArmorPartId id, double? condition)
    {
        _conditions[(int)id] = condition is { } c && double.IsFinite(c) ? Math.Clamp(c, 0, 1) : -1;
        _parts = null;
    }

    internal double[] Conditions => _conditions;

    // Requires: a piece listed is worn at full condition, every other piece is absent (a bare frame has none).
    public void SetPieces(IEnumerable<PowerArmorPartId> present)
    {
        var worn = new HashSet<PowerArmorPartId>(present);
        for (var i = 0; i < _conditions.Length; i++) SetCondition((PowerArmorPartId)i, worn.Contains((PowerArmorPartId)i) ? PowerArmorParts.FullCondition : null);
    }

    private List<PowerArmorPart> Build()
    {
        var list = new List<PowerArmorPart>();
        for (var i = 0; i < _conditions.Length; i++)
            if (_conditions[i] >= 0) list.Add(new((PowerArmorPartId)i, _conditions[i], _conditions[i] <= 0));
        return list;
    }

    public string? CoreKey { get; set; }

    public double CoreCharge { get; set; }

    public bool HasCore => CoreKey != null;
}

public enum PowerArmorRefusal { None, AlreadyInArmor, NotInArmor, NotACore, NoneInInventory, CoreAlreadyInstalled }

// Requires: main thread only.
public sealed class PowerArmorSession(Func<ActivationTarget, bool> isFrame, Func<string, bool> isFusionCore, PlayerItems items, double drainPerSecond = 0, PowerArmorDrain? drain = null)
    : IPowerArmorSource, IActivationHandler
{
    private readonly Dictionary<string, PowerArmorFrame> _frames = new(StringComparer.Ordinal);

    public PowerArmorFrame? Occupied { get; private set; }

    public bool InArmor => Occupied != null;

    public PowerArmorDrain? Model { get; set; } = drain;

    public PowerArmorSettings Settings { get; set; } = PowerArmorSettings.Default;

    // Requires: returns the pieces worn on the frame with this base key, or null when unreadable (the frame then keeps all six).
    public Func<string, IReadOnlyList<PowerArmorPartId>?>? Pieces { get; set; }

    public bool Restoring { get; private set; }

    public int RestoreCount { get; private set; }

    public bool PowerLost => Occupied is { } f && (!f.HasCore || f.CoreCharge <= 0);

    public event Action<PowerArmorFrame>? Entered;

    public event Action<PowerArmorFrame>? Exited;

    public PowerArmorState? Current => Occupied is { } f
        ? new PowerArmorState(f.Parts, f.CoreCharge, MinutesLeft(f), HasCore: f.HasCore, Name: f.Name, WarningText: PowerLost ? PowerArmorHudModel.NoPowerWarning : "")
        : null;

    public string? ResolveBinding(string role) => null;

    public PowerArmorFrame Frame(string refKey, string name = "")
    {
        if (!_frames.TryGetValue(refKey, out var frame)) _frames[refKey] = frame = new PowerArmorFrame(refKey, name);
        return frame;
    }

    public bool Handles(ActivationTarget target) => target.RefKey.Length > 0 && isFrame(target);

    public ActivationResult Activate(ActivationTarget target)
    {
        var isNew = !_frames.ContainsKey(target.RefKey);
        var frame = Frame(target.RefKey);
        if (isNew && Pieces?.Invoke(target.BaseKey) is { } worn) frame.SetPieces(worn);
        var refusal = Occupied?.RefKey == target.RefKey ? Exit() : Enter(frame);
        if (refusal == PowerArmorRefusal.None && InArmor) AutoLoadCore(frame);
        return refusal == PowerArmorRefusal.None ? new(ActivationStatus.Handled) : new(ActivationStatus.Blocked, refusal.ToString());
    }

    public bool AutoLoadCore(PowerArmorFrame frame)
    {
        if (frame.HasCore && frame.CoreCharge > 0) return false;
        foreach (var key in items.Stacks.Select(stack => stack.Id))
            if (isFusionCore(key) && InsertCore(frame, key, 1, items) == PowerArmorRefusal.None) return true;
        return false;
    }

    public PowerArmorRefusal Enter(PowerArmorFrame frame)
    {
        if (InArmor) return PowerArmorRefusal.AlreadyInArmor;
        Occupied = frame;
        Entered?.Invoke(frame);
        return PowerArmorRefusal.None;
    }

    public PowerArmorSave? Capture() =>
        _frames.Count == 0 ? null :
        new(Occupied?.RefKey, _frames.Values.Select(f => new PowerArmorFrameSave(f.RefKey, f.Name, f.CoreKey, f.CoreCharge, [.. f.Conditions])).ToList());

    // Requires: player items already restored.
    public void Restore(PowerArmorSave? save)
    {
        Restoring = true;
        RestoreCount++;
        try { RestoreFrames(save); }
        finally { Restoring = false; }
    }

    private void RestoreFrames(PowerArmorSave? save)
    {
        Exit();
        foreach (var frame in _frames.Values) { frame.CoreKey = null; frame.CoreCharge = 0; ResetParts(frame); }
        if (save is null) return;
        foreach (var row in save.Frames)
        {
            var frame = Frame(row.Ref, row.Name);
            frame.Name = row.Name;
            frame.CoreKey = row.Core;
            frame.CoreCharge = row.Core is null ? 0 : row.Charge;
            ResetParts(frame, row.Conditions);
        }
        if (save.Occupied is { } occupied) Enter(Frame(occupied));
    }

    private static void ResetParts(PowerArmorFrame frame, IReadOnlyList<double>? conditions = null)
    {
        var saved = conditions is { Count: PowerArmorHudModel.PartCount };
        for (var i = 0; i < PowerArmorHudModel.PartCount; i++)
            frame.SetCondition((PowerArmorPartId)i, !saved ? PowerArmorParts.FullCondition : conditions![i] >= 0 ? conditions[i] : null);
    }

    public PowerArmorRefusal Exit()
    {
        if (Occupied is not { } frame) return PowerArmorRefusal.NotInArmor;
        Occupied = null;
        Exited?.Invoke(frame);
        return PowerArmorRefusal.None;
    }

    public PowerArmorRefusal InsertCore(PowerArmorFrame frame, string itemKey, double charge, PlayerItems store)
    {
        if (!isFusionCore(itemKey)) return PowerArmorRefusal.NotACore;
        if (frame.HasCore && frame.CoreCharge > 0) return PowerArmorRefusal.CoreAlreadyInstalled;
        if (!store.Remove(itemKey, 1)) return PowerArmorRefusal.NoneInInventory;
        frame.CoreKey = itemKey;
        frame.CoreCharge = Math.Clamp(charge, 0, 1);
        return PowerArmorRefusal.None;
    }

    public void Drain(PowerArmorDrainKind kind, double amount = 1)
    {
        if (Occupied is not { HasCore: true } f || Model is not { Capacity: > 0 } d || !(amount > 0)) return;
        var units = d.Rates.Of(kind) * d.DamageRate * amount;
        if (units > 0) f.CoreCharge = Math.Max(0, f.CoreCharge - units / d.Capacity);
    }

    public void DamagePart(PowerArmorPartId id, double fraction)
    {
        if (Occupied is not { } f || f.ConditionOf(id) is not { } now || !(fraction > 0)) return;
        f.SetCondition(id, now - fraction);
    }

    // Requires: fraction is the total condition lost, split evenly over the pieces still standing.
    public void DamageSpread(double fraction)
    {
        if (Occupied is not { } f || !(fraction > 0) || !double.IsFinite(fraction)) return;
        var standing = f.Parts.Where(p => !p.IsBroken).Select(p => p.Id).ToList();
        foreach (var id in standing) DamagePart(id, fraction / standing.Count);
    }

    public void Tick(double dt, bool running = false)
    {
        if (running) Drain(PowerArmorDrainKind.Running, dt);
        if (Occupied is not { HasCore: true } f || drainPerSecond <= 0) return;
        f.CoreCharge = Math.Max(0, f.CoreCharge - drainPerSecond * dt);
    }

    private int MinutesLeft(PowerArmorFrame f) =>
        f.HasCore && drainPerSecond > 0 ? (int)Math.Ceiling(f.CoreCharge / drainPerSecond / 60) : -1;
}
