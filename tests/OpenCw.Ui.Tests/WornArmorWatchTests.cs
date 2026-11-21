using System.Linq;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WornArmorWatchTests
{
    static EquippedArmor Piece(string id, uint mask) => new(id, ItemState.Plain, mask);

    [Fact]
    public void Each_worn_item_is_reported_once_however_often_it_is_worn_again()
    {
        var watch = new WornArmorWatch();
        var worn = new[] { Piece("0001:a.esm", 1), Piece("0002:a.esm", 2) };

        Assert.Equal(new[] { "0001:a.esm", "0002:a.esm" }, watch.NewlyWorn(worn));
        Assert.Empty(watch.NewlyWorn(worn));
        Assert.Empty(watch.NewlyWorn(worn.Take(1).Concat(new[] { Piece("0001:A.ESM", 4) })));
        Assert.Equal(new[] { "0003:a.esm" }, watch.NewlyWorn(worn.Append(Piece("0003:a.esm", 8))));
    }

    [Fact]
    public void Nothing_worn_reports_nothing()
    {
        Assert.Empty(new WornArmorWatch().NewlyWorn(System.Array.Empty<EquippedArmor>()));
    }
}
