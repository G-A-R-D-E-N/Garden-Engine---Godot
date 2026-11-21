using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Trade;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ContainerUnlockEventTests
{
    [Fact]
    public void AnUnlockRaisesTheEventOncePerNewReference()
    {
        var ledger = new ContainerLedger();
        var raised = new List<string>();
        ledger.UnlockMarked += raised.Add;

        ledger.MarkUnlocked("door1");
        ledger.MarkUnlocked("door1");
        ledger.MarkUnlocked("door2");

        Assert.Equal(["door1", "door2"], raised);
    }

    [Fact]
    public void AReferenceUnlockedBeforeAListenerAttachedDoesNotReplay()
    {
        var ledger = new ContainerLedger();
        ledger.MarkUnlocked("door1");
        var raised = new List<string>();
        ledger.UnlockMarked += raised.Add;

        ledger.MarkUnlocked("door1");

        Assert.Empty(raised);
        Assert.True(ledger.IsUnlocked("door1"));
    }
}
