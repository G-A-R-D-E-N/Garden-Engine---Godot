using OpenCommonwealth.Services.Ui.Workshop;
using Xunit;

namespace OpenCw.Ui.Tests;

public class WorkshopObjectFactsTests
{
    [Fact]
    public void AWorkObjectOrABedIsATargetCarryingTheFlagsAndTheAllowance()
    {
        Assert.Equal(new CommandTarget("o1", true, true, false), WorkshopObjectFacts.Of("o1", true, false));
        Assert.Equal(new CommandTarget("o2", true, false, true), WorkshopObjectFacts.Of("o2", false, true));
        Assert.Equal(new CommandTarget("o3", false, true, true), WorkshopObjectFacts.Of("o3", true, true, false));
    }

    [Fact]
    public void AnythingElseIsNotATarget()
    {
        Assert.Null(WorkshopObjectFacts.Of("o1", false, false));
    }
}
