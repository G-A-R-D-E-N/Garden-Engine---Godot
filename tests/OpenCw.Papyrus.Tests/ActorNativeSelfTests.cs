using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorNativeSelfTests : IDisposable
{
    private readonly List<IScriptHost> _hosts = new();

    private const string Base = "000800:ActorProbe.esp";
    private const string Perk = "000801:ActorProbe.esp";

    private const string FormStub = "Scriptname Form\n";

    private const string ActorStub =
        "Scriptname Actor\n" +
        "Function AddPerk(Form akPerk) native\n";

    private const string PerkSourceStub =
        "Scriptname PerkSource\n" +
        "Form Function Get() global native\n";

    private const string OnProbeScript =
        "Scriptname ActorProbe extends Actor\n" +
        "Function OnProbe()\n" +
        "  AddPerk(PerkSource.Get())\n" +
        "EndFunction\n";

    private const string OnInitScript =
        "Scriptname ActorProbeOnInit extends Actor\n" +
        "Function OnInit()\n" +
        "  AddPerk(PerkSource.Get())\n" +
        "EndFunction\n";

    private static EvidenceGate Gate(string? denied = null)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch,
            ActorScriptOwners.PrecedenceKey, ActorScriptOwners.SpawnKey, "owner.engine_identity",
            "system.inventory", "system.magic_perk", MagicPerkRuntime.PerkKey };
        return new EvidenceGate(EvidenceRegistry.Load(System.Text.Json.JsonSerializer.Serialize(new
        {
            schema = EvidenceRegistry.SchemaName,
            rows = keys.Select(key => new { key, status = key == denied ? "UNPROVEN" : "ENGINE_POLICY" }),
        })));
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            TestHosts.Dispose(host);
        }
    }

    private (IScriptHost Host, ActorScriptOwners Actors, MagicPerkRuntime Magic) Runtime(
        HostKind kind, string? denied = null)
    {
        var gate = Gate(denied);
        var host = TestHosts.Create(kind, gate,
            new ScriptSpec("Form", FormStub),
            new ScriptSpec("Actor", ActorStub),
            new ScriptSpec("PerkSource", PerkSourceStub),
            new ScriptSpec("ActorProbe", OnProbeScript),
            new ScriptSpec("ActorProbeOnInit", OnInitScript));
        _hosts.Add(host);
        host.RegisterNative(new NativeId("PerkSource", "Get"),
            _ => PapyrusValue.FromObject(new EngineObjectReference(Perk)));
        var magic = new MagicPerkRuntime(host, gate);
        var inventory = new InventoryRuntime(new PapyrusEventBridge(host, gate), new ScriptRegistrationRegistry(), gate);
        new GameplayNativeAdapters(host, inventory, magic, gate).Bind();
        return (host, new ActorScriptOwners(host, gate), magic);
    }

    private static ActorInput Input(ActorOrigin origin, bool onInit = false, string discriminator = "first") =>
        new(origin, Base, discriminator, onInit ? "ActorProbeOnInit" : "ActorProbe", null);

    private static void AssertStopped(VmResult result, HostKind kind)
    {
        Assert.False(result.Completed);
        if (kind == HostKind.CSharp)
        {
            Assert.Equal(VmStopReason.ModelError, result.Reason);
        }
    }

    public static IEnumerable<object[]> PlacementCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var origin in new[] { ActorOrigin.Placed, ActorOrigin.Spawned })
            {
                foreach (var onInit in new[] { false, true })
                {
                    yield return new object[] { host[0], origin, onInit };
                }
            }
        }
    }

    public static IEnumerable<object[]> GateCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var denied in new[] { ActorScriptOwners.PrecedenceKey, ActorScriptOwners.SpawnKey })
            {
                yield return new object[] { host[0], denied };
            }
        }
    }

    [Theory]
    [MemberData(nameof(PlacementCases))]
    public void AnActorSuppliesItsExplicitOwnerToSelfAddPerk(HostKind kind, ActorOrigin origin, bool onInit)
    {
        var runtime = Runtime(kind);
        var attached = runtime.Actors.Attach(new[] { Input(origin, onInit) });
        var actor = Assert.Single(attached.Actors);
        var instance = Assert.IsType<ScriptInstance>(runtime.Actors.InstanceOf(actor.Owner));
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        if (!onInit) Assert.True(runtime.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Equal(actor.Owner, instance.Owner);
        Assert.True(runtime.Magic.HasPerk(actor.Owner, Perk));
        Assert.Equal(1, runtime.Magic.Applied);
        Assert.Equal(0, runtime.Magic.Refused);
        Assert.Equal(0, TestHosts.NativeTargetRefusals(runtime.Host));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlacedAndSpawnedActorsOfOneBaseKeepIndependentPerks(HostKind kind)
    {
        var runtime = Runtime(kind);
        var actors = runtime.Actors.Attach(new[] { Input(ActorOrigin.Placed), Input(ActorOrigin.Spawned) }).Actors;
        var first = actors[0].Owner;
        var second = actors[1].Owner;
        Assert.NotEqual(first, second);
        Assert.True(runtime.Host.RunEvent(runtime.Actors.InstanceOf(first)!, "OnProbe").Completed);
        Assert.True(runtime.Magic.HasPerk(first, Perk));
        Assert.False(runtime.Magic.HasPerk(second, Perk));
        Assert.True(runtime.Host.RunEvent(runtime.Actors.InstanceOf(second)!, "OnProbe").Completed);
        Assert.True(runtime.Magic.HasPerk(second, Perk));
        Assert.Equal(2, runtime.Magic.Applied);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RetainingAnActorOwnerDoesNotBypassTheIdentityGate(HostKind kind)
    {
        var runtime = Runtime(kind, denied: "owner.engine_identity");
        var actor = Assert.Single(runtime.Actors.Attach(new[] { Input(ActorOrigin.Placed) }).Actors);
        var result = runtime.Host.RunEvent(runtime.Actors.InstanceOf(actor.Owner)!, "OnProbe");
        AssertStopped(result, kind);
        if (kind == HostKind.CSharp)
        {
            Assert.Contains("owner.engine_identity", result.Message!, StringComparison.Ordinal);
        }
        Assert.False(runtime.Magic.HasPerk(actor.Owner, Perk));
        Assert.Equal(0, runtime.Magic.Applied);
    }

    [Theory]
    [MemberData(nameof(GateCases))]
    public void ExistingActorGatesStillRefuseBeforeAttachment(HostKind kind, string denied)
    {
        var runtime = Runtime(kind, denied);
        var report = runtime.Actors.Attach(new[] { Input(ActorOrigin.Spawned) });
        Assert.Equal(0, report.Attached);
        Assert.Empty(runtime.Host.Instances);
        Assert.Equal(0, runtime.Magic.Applied);
    }
}
