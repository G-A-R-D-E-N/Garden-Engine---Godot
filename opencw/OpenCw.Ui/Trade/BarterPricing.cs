using System;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public static class BarterPricing
{
    public static readonly string CapsItemId = RecordKey.Format("Fallout4.esm", 0x0F);

    public const double ValueFactor = 1.0;

    public static double Factor(BarterSide side) => ValueFactor;

    // Requires: charisma is the stored base Charisma of the player NPC_ record (no live actor-value store exists); an unknown or non-finite value keeps the 1:1 Factor.
    public static Func<BarterSide, double> For(BarterSettings settings, float? charisma, IBarterPerkSource? perks = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (charisma is not { } value || !float.IsFinite(value)) return Factor;
        double sell = BarterFormula.Multiplier(settings, value, true, BarterPerks.Modifier(perks, true));
        double buy = BarterFormula.Multiplier(settings, value, false, BarterPerks.Modifier(perks, false));
        return side => side == BarterSide.Player ? sell : buy;
    }
}
