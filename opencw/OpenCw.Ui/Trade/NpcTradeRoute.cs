using OpenCommonwealth.Services.Ui.Dialogs;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum TradeRoute
{
    NotTrade,
    NoVendorChest,
    Barter,
}

public interface IVendorChests
{
    string? ChestKey(string npcRefKey, string npcBaseKey);
}

public sealed class NoVendorChests : IVendorChests
{
    public string? ChestKey(string npcRefKey, string npcBaseKey) => null;
}

public sealed record NpcTradeDecision(TradeRoute Route, string? ChestKey = null);

public static class NpcTradeRoute
{
    public static NpcTradeDecision Decide(DialogueOutcome outcome, IVendorChests chests, string npcRefKey, string npcBaseKey)
    {
        if (outcome != DialogueOutcome.Trade) return new NpcTradeDecision(TradeRoute.NotTrade);
        return chests.ChestKey(npcRefKey, npcBaseKey) is { Length: > 0 } chest
            ? new NpcTradeDecision(TradeRoute.Barter, chest)
            : new NpcTradeDecision(TradeRoute.NoVendorChest);
    }
}
