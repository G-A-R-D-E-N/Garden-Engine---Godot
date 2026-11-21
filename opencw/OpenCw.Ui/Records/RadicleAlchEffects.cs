using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.PipBoy;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: id is an ALCH record key; a record with no readable effect list reads as null; the caller holds the GameLoader.EnvLifetime read lock.
public sealed class RadicleAlchEffects
{
    private readonly RadicleEnvironment _env;

    public RadicleAlchEffects(RadicleEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    public IReadOnlyList<ConsumableEffect>? Read(string id) => ReadAs(id, "ALCH");

    public IReadOnlyList<ConsumableEffect>? ReadSpell(string id) => ReadAs(id, "SPEL", "ENCH");

    private IReadOnlyList<ConsumableEffect>? ReadAs(string id, params string[] signatures)
    {
        try
        {
            if (!RecordKey.TryParse(id, out var plugin, out var local) || _env.Winner(new FormKey(plugin, local)) is not { } record) return null;
            if (Array.IndexOf(signatures, _env.Info(record).Signature) < 0) return null;
            return Map(_env.MagicEffects(record));
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    // Invariant: one null effect key or non-finite magnitude makes the whole list unreadable, never a partial list.
    internal static IReadOnlyList<ConsumableEffect>? Map(IReadOnlyList<MagicEffectEntry> entries)
    {
        var effects = new List<ConsumableEffect>(entries.Count);
        foreach (var e in entries)
        {
            if (e.Effect is not { } key || !float.IsFinite(e.Magnitude)) return null;
            effects.Add(new ConsumableEffect(RecordKey.Format(key), e.Magnitude, (int)Math.Min(e.Duration, int.MaxValue), e.Conditional));
        }
        return effects;
    }
}
