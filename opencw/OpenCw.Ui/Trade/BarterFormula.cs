using System;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record BarterSettings(float Max = 2.5f, float Min = 1.5f, float BuyMax = 1.05f, float SellMax = 0.95f)
{
    public static BarterSettings Read(IRecordReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var d = new BarterSettings();
        return new BarterSettings(
            PickpocketSettings.Value(reader, "fBarterMax", d.Max),
            PickpocketSettings.Value(reader, "fBarterMin", d.Min),
            PickpocketSettings.Value(reader, "fBarterBuyMax", d.BuyMax),
            PickpocketSettings.Value(reader, "fBarterSellMax", d.SellMax));
    }
}

public static class BarterFormula
{
    // Invariant: single precision, as the exe; selling means the player sells (the player list side), buying is the vendor list side; the perk multiplier applies after the Charisma curve and before the buy floor or sell cap.
    public static float Multiplier(BarterSettings s, float charisma, bool selling, float perk = 1f)
    {
        ArgumentNullException.ThrowIfNull(s);
        var m = s.Max - (s.Max - s.Min) * charisma * 0.1f;
        if (!selling)
        {
            var buy = MathF.Max(m, 1f) * perk;
            return buy < s.BuyMax ? s.BuyMax : buy;
        }
        var sell = 1f / m;
        if (sell < 0f || sell > 1f) sell = 1f;
        sell *= perk;
        return sell > s.SellMax ? s.SellMax : sell;
    }
}
