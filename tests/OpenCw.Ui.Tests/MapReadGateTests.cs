using OpenCommonwealth.Services.Ui.PipBoy;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MapReadGateTests
{
    [Fact]
    public void A_successful_read_is_kept_until_the_identity_changes()
    {
        var gate = new MapReadGate();
        Assert.True(gate.ShouldRead("m|1", false));
        gate.Report(true);

        Assert.False(gate.ShouldRead("m|1", true));
        Assert.True(gate.ShouldRead("m|2", false));
    }

    [Fact]
    public void A_failed_read_is_retried_on_every_request_but_a_bounded_number_of_background_calls()
    {
        var gate = new MapReadGate();
        Assert.True(gate.ShouldRead("m|1", false));
        gate.Report(false);

        for (var i = 1; i < MapReadGate.MaxBackgroundFailures; i++)
        {
            Assert.True(gate.ShouldRead("m|1", false));
            gate.Report(false);
        }

        Assert.False(gate.ShouldRead("m|1", false));
        Assert.True(gate.ShouldRead("m|1", true));
        gate.Report(true);
        Assert.False(gate.ShouldRead("m|1", true));
    }

    [Fact]
    public void A_changed_identity_or_a_reset_starts_over()
    {
        var gate = new MapReadGate();
        gate.ShouldRead("m|1", false);
        for (var i = 0; i < MapReadGate.MaxBackgroundFailures; i++) gate.Report(false);
        Assert.False(gate.ShouldRead("m|1", false));

        Assert.True(gate.ShouldRead("m|2", false));
        gate.Report(true);
        gate.Reset();
        Assert.True(gate.ShouldRead("m|2", false));
    }
}
