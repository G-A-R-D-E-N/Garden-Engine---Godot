using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferencePositionNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec RefSpec = TestHosts.Stub("ObjectReference", null,
        "Float Function GetPositionX() native", "Float Function GetPositionY() native", "Float Function GetPositionZ() native");

    private static readonly ScriptSpec UserSpec = new("PositionUser",
        "Scriptname PositionUser\n" +
        "ObjectReference Property Target Auto\n" +
        "Float Property Sum Auto\n" +
        "Event OnInit()\n" +
        "  Sum = Target.GetPositionX() + Target.GetPositionY() * 10.0 + Target.GetPositionZ() * 100.0\n" +
        "EndEvent\n");

    private ScriptInstance Setup(Func<string, (double, double, double)?> position, out RadicleScriptHost host)
    {
        host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), RefSpec, UserSpec));
        Assert.Equal(3, new ReferencePositionNativeAdapters(host, TestHosts.DefaultGate(), position).Bind().Bound);
        return host.Attach("PositionUser", new Dictionary<string, PapyrusValue> { ["Target"] = PapyrusValue.FromObject(new EngineObjectReference("R:T.esm")) });
    }

    [Fact]
    public void EachAxisAnswersItsOwnCoordinate()
    {
        var instance = Setup(_ => (1, 2, 3), out var host);

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Sum", out var sum));
        Assert.Equal(321f, sum);
    }

    [Fact]
    public void AnOwnedReferenceAsksForItsPlacedReferenceKey()
    {
        var asked = new List<string>();
        Setup(key => { asked.Add(key); return (1, 2, 3); }, out var host);
        var instance = host.Attach("PositionUser", new Dictionary<string, PapyrusValue>
        {
            ["Target"] = PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference("B:T.esm", "R:T.esm"))),
        });

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.Equal(3, asked.Count);
        Assert.All(asked, key => Assert.Equal("R:T.esm", key));
    }

    [Fact]
    public void AReferenceWithNoNodeStopsTheScriptAndNamesIt()
    {
        var instance = Setup(_ => null, out var host);

        Assert.False(host.Initialize(instance).Completed);
        Assert.Contains("R:T.esm", instance.Failure);
    }
}
