using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CellOwnerScriptsTests
{
    private static (ScriptHost Host, EvidenceGate Gate) Setup()
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.Host, EvidenceKeys.FailClosed,
            EvidenceKeys.SchedulerBudget, ScriptOverrideRegistry.Key,
            PapyrusEventBridge.KeyPrefix + "OnActivate" };
        var rows = string.Join(",", keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}"));
        var gate = new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{rows}]}}"));
        var library = new ScriptLibrary();
        foreach (var name in new[] { "First", "Second" })
            library.Add(new PapyrusScript(name, new[]
            {
                new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
                new PapyrusFunction("OnActivate", new[] { PapyrusInstruction.Return() }),
            }, Array.Empty<string>()));
        return (new ScriptHost(library, gate), gate);
    }

    [Fact]
    public void EveryScriptOnAReferenceSurvivesDetachAndReattach()
    {
        var (host, _) = Setup();
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "Reference", "First");
        var second = session.Bind("Cell", "Reference", "Second");
        Assert.True(host.Initialize(first).Completed);
        Assert.True(host.Initialize(second).Completed);

        var parked = session.DetachCell("Cell");
        Assert.Equal(new[] { first, second }, parked);
        Assert.Equal(2, session.ParkedInstances);
        Assert.Equal(0, session.AttachedCells);
        Assert.Equal(new[] { first, second }, session.ReattachCell("Cell"));
        Assert.Equal(2, session.BoundInstances);
        Assert.Equal(2, host.Instances.Count);
    }

    [Fact]
    public void DuplicateBindingIsRefusedWithoutOrphaningTheExistingInstance()
    {
        var (host, _) = Setup();
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "Reference", "First");
        Assert.Throws<InvalidOperationException>(() => session.Bind("Cell", "Reference", "first"));
        Assert.Same(first, session.InstanceFor("Reference"));
        Assert.Single(host.Instances);
    }

    [Fact]
    public void AnEventReachesEveryAttachedScriptOnItsOwner()
    {
        var (host, gate) = Setup();
        var bridge = new PapyrusEventBridge(host, gate);
        var owner = PapyrusOwnerId.ForPlacedReference("000900:Probe.esp", "000800:Probe.esp");
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in new[] { "First", "Second" })
        {
            host.Overrides.Register(name, "OnActivate", 0, _ =>
            {
                seen.Add(name);
                return OverrideResult.Handled(PapyrusValue.None);
            });
            var instance = host.Attach(name);
            Assert.True(host.Initialize(instance).Completed);
            bridge.Register(owner, instance);
        }

        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>(), owner));
        Assert.Equal(new[] { "First", "Second" }, seen);
        Assert.Equal(2, batch.Delivered);
        Assert.Equal(2, bridge.Delivered);
        Assert.Equal(1, bridge.RegisteredOwners);
    }

    [Fact]
    public void ParkingAnOwnerReportsEveryAttachedScript()
    {
        var (host, gate) = Setup();
        var bridge = new PapyrusEventBridge(host, gate);
        var owner = PapyrusOwnerId.ForPlacedReference("000900:Probe.esp", "000800:Probe.esp");
        foreach (var name in new[] { "First", "Second" })
        {
            var instance = host.Attach(name);
            Assert.True(host.Initialize(instance).Completed);
            bridge.Register(owner, instance);
            bridge.Register(owner, instance);
        }
        bridge.Park(owner);
        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>(), owner));
        Assert.Equal(2, batch.SkippedParked);
        Assert.Equal(2, batch.Outcomes.Count);
        Assert.Equal(0, batch.Delivered);
        Assert.Equal(1, bridge.ParkedOwners);
    }
}
