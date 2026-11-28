using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Workbench;

// Constraint: the item stock, the supplied level and the optional perk store are the only real sources; a null perk store, keywords and globals stay unknown, so gated recipes stay hidden at a bench.
public sealed class PlayerRecipeFacts(PlayerItems items, Func<double?>? level = null, PlayerPerks? perks = null) : IRecipeFacts
{
    public double? Level => level?.Invoke();

    public bool? HasPerk(FormKey perk) => perks?.Has(RecordKey.Format(perk));

    public bool? HasKeyword(FormKey keyword) => null;

    public double? Global(FormKey global) => null;

    public int ItemCount(FormKey item) => items.CountOf(RecordKey.Format(item));
}
