using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PartialAttachTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private const string Metadata = "[{\"name\":\"First\",\"properties\":[]},{\"name\":\"Second\",\"properties\":[]}]";

    private static readonly string[] Keys =
    {
        "vm.session.cell_lifecycle", "vm.session.activation_routing", "vmad.reference_compiled_attachments",
        "owner.engine_identity", "event.OnActivate", "vmad.reference_source", "vmad.value_binding", ScriptOverrideRegistry.Key,
    };

    private static ScriptSpec Handler(string name) => new(name,
        $"Scriptname {name}\nFunction OnInit()\nEndFunction\nFunction OnActivate(ObjectReference akActionRef)\nEndFunction\n");

    private static GameSession Session()
    {
        var gate = TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray());
        var session = new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"), Handler("First"), Handler("Second")), gate);
        session.Host.Overrides = new ScriptOverrideRegistry(gate);
        session.Host.Overrides.Register("First", "OnInit", 0, _ => OverrideResult.Refused("synthetic initialization failure"));
        return session;
    }

    // Detects: one failed script initialization keeping the healthy sibling scripts of the same reference out of event delivery, both now and after the cell streams out and back; the healthy one must keep working.
    [Fact]
    public void AFailedSiblingScriptDoesNotStopTheHealthyOneAcrossAStream()
    {
        using var session = Session();
        var attached = session.AttachReference("Played", Owner, Metadata);
        Assert.Null(attached.Refusal);
        Assert.Equal(new[] { false, true }, attached.Initialization.Select(result => result.Completed).ToArray());
        var batch = session.Activate(Owner, Activator);
        Assert.Equal(1, batch.Delivered);
        Assert.Single(batch.Outcomes);
        session.DetachCell("Played");
        Assert.Equal(0, session.Activate(Owner, Activator).Delivered);
        session.ReattachCell("Played", new HashSet<PapyrusOwnerId> { Owner });
        Assert.Equal(1, session.Activate(Owner, Activator).Delivered);
    }
}
