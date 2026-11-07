using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ActorNativeSelfTests
{
    private const string Base = "000800:ActorProbe.esp";
    private const string Perk = "000801:ActorProbe.esp";

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

    private static (ScriptHost Host, ActorScriptOwners Actors, MagicPerkRuntime Magic) Runtime(
        bool onInit = false, string? denied = null)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Actor", new[]
        {
            new PapyrusFunction("AddPerk", Array.Empty<PapyrusInstruction>(), ParameterCount: 1, IsNative: true),
        }, Array.Empty<string>()));
        library.Add(new PapyrusScript("ActorProbe", new[]
        {
            new PapyrusFunction(onInit ? "OnInit" : "OnProbe", new[]
            {
                PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "AddPerk", PapyrusOperand.Discard,
                    new[] { PapyrusOperand.Constant(PapyrusValue.FromObject(new EngineObjectReference(Perk))) },
                    receiverType: "Actor"),
                PapyrusInstruction.Return(),
            }),
        }, Array.Empty<string>(), Parent: "Actor"));
        var gate = Gate(denied);
        var host = new ScriptHost(library, gate);
        var magic = new MagicPerkRuntime(host, gate);
        var inventory = new InventoryRuntime(new PapyrusEventBridge(host, gate), new ScriptRegistrationRegistry(), gate);
        new GameplayNativeAdapters(host, inventory, magic, gate).Bind();
        return (host, new ActorScriptOwners(host, gate), magic);
    }

    private static ActorInput Input(ActorOrigin origin, string discriminator = "first") =>
        new(origin, Base, discriminator, "ActorProbe", null);

    [Theory]
    [InlineData(ActorOrigin.Placed, false)]
    [InlineData(ActorOrigin.Placed, true)]
    [InlineData(ActorOrigin.Spawned, false)]
    [InlineData(ActorOrigin.Spawned, true)]
    public void AnActorSuppliesItsExplicitOwnerToSelfAddPerk(ActorOrigin origin, bool onInit)
    {
        var runtime = Runtime(onInit);
        var attached = runtime.Actors.Attach(new[] { Input(origin) });
        var actor = Assert.Single(attached.Actors);
        var instance = Assert.IsType<ScriptInstance>(runtime.Actors.InstanceOf(actor.Owner));
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        if (!onInit) Assert.True(runtime.Host.RunEvent(instance, "OnProbe").Completed);
        Assert.Equal(actor.Owner, instance.Owner);
        Assert.True(runtime.Magic.HasPerk(actor.Owner, Perk));
        Assert.Equal(1, runtime.Magic.Applied);
        Assert.Equal(0, runtime.Magic.Refused);
        Assert.Equal(0, runtime.Host.NativeTargetRefusals);
    }

    [Fact]
    public void PlacedAndSpawnedActorsOfOneBaseKeepIndependentPerks()
    {
        var runtime = Runtime();
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

    [Fact]
    public void RetainingAnActorOwnerDoesNotBypassTheIdentityGate()
    {
        var runtime = Runtime(denied: "owner.engine_identity");
        var actor = Assert.Single(runtime.Actors.Attach(new[] { Input(ActorOrigin.Placed) }).Actors);
        var result = runtime.Host.RunEvent(runtime.Actors.InstanceOf(actor.Owner)!, "OnProbe");
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("owner.engine_identity", result.Message);
        Assert.False(runtime.Magic.HasPerk(actor.Owner, Perk));
        Assert.Equal(0, runtime.Magic.Applied);
    }

    [Theory]
    [InlineData(ActorScriptOwners.PrecedenceKey)]
    [InlineData(ActorScriptOwners.SpawnKey)]
    public void ExistingActorGatesStillRefuseBeforeAttachment(string denied)
    {
        var runtime = Runtime(denied: denied);
        var report = runtime.Actors.Attach(new[] { Input(ActorOrigin.Spawned) });
        Assert.Equal(0, report.Attached);
        Assert.Empty(runtime.Host.Instances);
        Assert.Equal(0, runtime.Magic.Applied);
    }
}
