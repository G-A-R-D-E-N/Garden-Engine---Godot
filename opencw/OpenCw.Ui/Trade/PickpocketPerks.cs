using System;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class PickpocketPerks
{
    public const byte ModPickpocketChance = 0x38;
    public const byte AddValue = 2;

    // Invariant: only Add Value rows with a finite value apply, through the rank and tab gates of BarterPerks; the bonus is the largest applicable value across held perks, not their sum.
    public static Func<float, float>? Modifier(IBarterPerkSource? source)
    {
        if (source is null) return null;
        float? bonus = null;
        foreach (var held in source.Held(BarterActor.Player))
        {
            if (source.EntryPoints(held.Perk) is not { } record) continue;
            var rank = Math.Max(held.Rank - 1, 0);
            foreach (var effect in record.Effects)
                if (effect.EntryPoint == ModPickpocketChance && effect.Function == AddValue && effect.Rank == rank
                    && effect.Value is { } value && float.IsFinite(value) && BarterPerks.TabsAllow(source, BarterActor.Player, effect))
                    bonus = bonus is { } best ? Math.Max(best, value) : value;
        }
        if (bonus is not { } add) return null;
        return chance => chance + add;
    }
}
