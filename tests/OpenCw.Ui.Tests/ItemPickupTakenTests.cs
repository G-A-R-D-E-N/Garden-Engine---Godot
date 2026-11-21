using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class ItemPickupTakenTests
{
    private const string Leather = "000C01:Fallout4.esm";
    private const string Ref = "000A03:Fallout4.esm";

    [Fact]
    public void ARespawnedReferenceThatWasAlreadyTakenIsRemovedWithoutAddingItAgain()
    {
        var ledger = new ContainerLedger();
        var items = new PlayerItems();
        var removed = new List<string>();
        var pickup = new ItemPickup(items, _ => true, t => removed.Add(t.RefKey), ledger);
        var target = new ActivationTarget(Ref, Leather, "MiscItem", new HashSet<string>(), 4);

        pickup.Activate(target);
        var again = pickup.Activate(target);

        Assert.Equal(ActivationStatus.Handled, again.Status);
        Assert.Equal(4, items.CountOf(Leather));
        Assert.Equal([Ref, Ref], removed);
    }
}
