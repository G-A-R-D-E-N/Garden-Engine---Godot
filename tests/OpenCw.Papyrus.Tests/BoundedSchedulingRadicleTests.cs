using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class BoundedSchedulingRadicleTests : HostFixture
{
    private const string Quick =
        "Scriptname Quick\n" +
        "Function Run()\n" +
        "EndFunction\n";

    private const string Bounded =
        "Scriptname Bounded\n" +
        "Function Run()\n" +
        "  Int n = 0\n" +
        "  n += 1\n" +
        "  n += 1\n" +
        "EndFunction\n";

    private static EvidenceGate Gate() => TestHosts.Gate();

    private RadicleScriptHost Load(int stepBudget, params (string Name, string Source)[] scripts) =>
        (RadicleScriptHost)Track(new RadicleScriptHost(Gate(),
            ConformanceScripts.Compile(scripts).Values, stepBudget: stepBudget));

    private static ScriptInstance Ready(IScriptHost host, string name)
    {
        var instance = host.Attach(name);
        var initialized = host.Initialize(instance);
        Assert.True(initialized.Completed, TestHosts.Describe(host, initialized));
        return instance;
    }

    [Fact]
    public void ARunawayDoesNotBlockOtherEventsFromCompleting()
    {
        var host = Load(16, ("Spin", ConformanceScripts.Spin), ("Quick", Quick));
        var spin = Ready(host, "Spin");
        var quick = Ready(host, "Quick");

        Assert.Equal(VmStopReason.BudgetExhausted, host.RunEvent(spin, "Run").Reason);
        var completed = host.RunEvent(quick, "Run");

        Assert.True(completed.Completed, TestHosts.Describe(host, completed));
        Assert.Equal(0, host.Stats.Ready);
        Assert.Equal(0, host.RunStep(16));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(64)]
    public void ASmallBudgetStillCompletesTheSameWorkOnce(int budget)
    {
        var host = Load(budget, ("Bounded", Bounded));
        var instance = Ready(host, "Bounded");

        var result = host.RunEvent(instance, "Run");

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(0, host.Stats.Dropped);
        Assert.Equal(0, host.RunStep(budget));
    }
}
