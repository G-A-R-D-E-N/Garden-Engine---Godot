using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorPackageNativeAdaptersTests : HostFixture
{
    private const string Npc = "000A00:Fallout4.esm";

    private static readonly ScriptSpec ActorSpec = TestHosts.Stub("Actor", null, "Function EvaluatePackage(Bool abResetAI = false) native");

    private static readonly ScriptSpec UserSpec = new("PackageUser",
        "Scriptname PackageUser\n" +
        "Actor Property Target Auto\n" +
        "Event OnInit()\n" +
        "  Target.EvaluatePackage()\n" +
        "  Target.EvaluatePackage(true)\n" +
        "EndEvent\n");

    private ScriptInstance Setup(Func<string, bool> reevaluate, List<string> log, out ActorPackageNativeAdapters adapter, out RadicleScriptHost host, EngineObjectReference? target = null)
    {
        host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), ActorSpec, UserSpec));
        adapter = new ActorPackageNativeAdapters(host, TestHosts.DefaultGate(), reevaluate, log.Add);
        Assert.Equal(1, adapter.Bind().Bound);
        return host.Attach("PackageUser", new Dictionary<string, PapyrusValue> { ["Target"] = PapyrusValue.FromObject(target ?? new EngineObjectReference(Npc)) });
    }

    [Fact]
    public void EachCallForwardsTheReferenceKeyToTheRunner()
    {
        var asked = new List<string>();
        var instance = Setup(key => { asked.Add(key); return true; }, new List<string>(), out var adapter, out var host);

        Assert.True(host.Initialize(instance).Completed, instance.Failure);

        Assert.Equal(new[] { Npc, Npc }, asked);
        Assert.Equal(2, adapter.Reevaluated);
        Assert.Equal(0, adapter.Unanswered);
    }

    [Fact]
    public void ARequestThatReachesNoRunnerIsCountedAndLoggedWithoutStoppingTheScript()
    {
        var log = new List<string>();
        var instance = Setup(_ => false, log, out var adapter, out var host);

        Assert.True(host.Initialize(instance).Completed, instance.Failure);

        Assert.Equal(0, adapter.Reevaluated);
        Assert.Equal(2, adapter.Unanswered);
        Assert.Contains(log, line => line.Contains(Npc, StringComparison.Ordinal));
    }

    [Fact]
    public void AnOwnerBackedReferenceForwardsTheDiscriminatorTheActorRegistryStores()
    {
        var owner = PapyrusOwnerId.ForPlacedReference("000B00:Fallout4.esm", Npc);
        var asked = new List<string>();
        var instance = Setup(key => { asked.Add(key); return true; }, new List<string>(), out _, out var host, new EngineObjectReference(owner));

        Assert.True(host.Initialize(instance).Completed, instance.Failure);

        Assert.Equal(new[] { Npc, Npc }, asked);
    }
}
