using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class NoteReadingTests
{
    [Fact]
    public void ATerminalTargetOpensTheTerminalAndPlaysNothing()
    {
        string? opened = null;
        var played = false;
        var result = NoteReading.Read("Tape", "000010:Test.esm", true, key => { opened = key; return null; }, () => played = true);
        Assert.Equal(InventoryActionStatus.Done, result.Status);
        Assert.Equal("000010:Test.esm", opened);
        Assert.False(played);
    }

    [Fact]
    public void ATerminalThatWillNotOpenRefusesWithItsReasonAndDoesNotFallBackToSound()
    {
        var played = false;
        var result = NoteReading.Read("Tape", "000010:Test.esm", true, _ => "locked", () => played = true);
        Assert.Equal((InventoryActionStatus.Refused, "locked"), (result.Status, result.Message));
        Assert.False(played);
    }

    [Fact]
    public void ASoundTargetPlaysAndNothingToPlayRefuses()
    {
        var played = 0;
        Assert.Equal(InventoryActionStatus.Done, NoteReading.Read("Tape", null, true, _ => null, () => played++).Status);
        Assert.Equal(1, played);
        var empty = NoteReading.Read("Tape", null, false, _ => null, () => played++);
        Assert.Equal((InventoryActionStatus.Refused, NoteReading.NothingToPlay), (empty.Status, empty.Message));
        Assert.Equal(1, played);
    }
}
