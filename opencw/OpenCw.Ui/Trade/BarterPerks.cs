using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Workbench;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum BarterActor
{
    Player,
    Vendor,
}

public sealed record HeldPerk(FormKey Perk, int Rank);

public interface IBarterPerkSource
{
    IReadOnlyList<HeldPerk> Held(BarterActor actor);

    PerkEntryPointsRecord? EntryPoints(FormKey perk);

    IRecipeFacts? Facts(BarterActor actor);
}

public static class BarterPerks
{
    public const byte ModBuyPrices = 0x08;
    public const byte ModSellPrices = 0x3C;
    public const byte MultiplyValue = 3;
    public const sbyte RunOnTarget = 1;

    private static readonly BarterActor[] Actors = [BarterActor.Player, BarterActor.Vendor];

    // Invariant: only Multiply Value rows with a finite positive float apply; an effect row applies when its rank is the held rank minus one and no tab's conditions are a definite False.
    public static float Modifier(IBarterPerkSource? source, bool selling)
    {
        if (source is null) return 1f;
        var entry = selling ? ModSellPrices : ModBuyPrices;
        var factor = 1f;
        foreach (var actor in Actors)
            foreach (var held in source.Held(actor))
            {
                if (source.EntryPoints(held.Perk) is not { } record) continue;
                var rank = Math.Max(held.Rank - 1, 0);
                foreach (var effect in record.Effects)
                    if (effect.EntryPoint == entry && effect.Function == MultiplyValue && effect.Rank == rank
                        && effect.Value is { } value && float.IsFinite(value) && value > 0f && TabsAllow(source, actor, effect))
                        factor *= value;
            }
        return factor;
    }

    internal static bool TabsAllow(IBarterPerkSource source, BarterActor holder, PerkEntryPointEffect effect) =>
        effect.Tabs.All(tab =>
        {
            var subject = tab.RunOn == RunOnTarget ? Other(holder) : holder;
            return source.Facts(subject) is not { } facts
                || RecipeConditionGate.Verdict(tab.Conditions.Select(RecordCondition.From).ToArray(), facts, RecipeConditionGate.RunOnSubject)
                    != RuleVerdict.False;
        });

    private static BarterActor Other(BarterActor actor) => actor == BarterActor.Player ? BarterActor.Vendor : BarterActor.Player;
}
