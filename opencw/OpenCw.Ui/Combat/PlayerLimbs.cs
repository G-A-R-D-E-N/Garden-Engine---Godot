using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;

namespace OpenCommonwealth.Services.Combat;

// Requires: main thread only; a limb with no entry is unknown (null), never full health.
public sealed class PlayerLimbs
{
    private readonly Dictionary<LimbKind, LimbCondition> _limbs = new();

    public static PlayerLimbs Current { get; } = new();

    public event Action? Changed;

    public LimbCondition? Get(LimbKind limb) => _limbs.TryGetValue(limb, out var condition) ? condition : null;

    // Invariant: the fraction is clamped to [0, 1] and only a limb at 0 can be crippled; non-finite input is ignored.
    public void Set(LimbKind limb, double fraction, bool crippled = false)
    {
        if (!double.IsFinite(fraction)) return;
        var clamped = Math.Clamp(fraction, 0, 1);
        var condition = new LimbCondition(clamped, crippled && clamped <= 0);
        if (_limbs.TryGetValue(limb, out var held) && held == condition) return;
        _limbs[limb] = condition;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_limbs.Count == 0) return;
        _limbs.Clear();
        Changed?.Invoke();
    }
}
