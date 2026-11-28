using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class CellOwnerScriptsTests : HostFixture
{
    private static readonly (string Key, string Status)[] ExtraRows =
    {
        (PapyrusEventBridge.KeyPrefix + "OnActivate", "ENGINE_POLICY"),
    };

    private static EvidenceGate Gate() => TestHosts.Gate(ExtraRows);

    private IScriptHost Setup(HostKind kind, EvidenceGate gate) => Track(TestHosts.Create(kind, gate,
        TestHosts.Stub("First", null, "Function OnInit()\nEndFunction", "Function OnActivate()\nEndFunction"),
        TestHosts.Stub("Second", null, "Function OnInit()\nEndFunction", "Function OnActivate()\nEndFunction")));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void EveryScriptOnAReferenceSurvivesDetachAndReattach(HostKind kind)
    {
        var host = Setup(kind, Gate());
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "Reference", "First");
        var second = session.Bind("Cell", "Reference", "Second");
        Assert.True(host.Initialize(first).Completed);
        Assert.True(host.Initialize(second).Completed);

        var parked = session.DetachCell("Cell");
        Assert.Equal(new[] { first, second }, parked);
        Assert.Equal(2, session.ParkedInstances);
        Assert.Equal(new[] { first, second }, session.ReattachCell("Cell"));
        Assert.Equal(2, session.BoundInstances);
        Assert.Equal(2, host.Instances.Count);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DuplicateBindingIsRefusedWithoutOrphaningTheExistingInstance(HostKind kind)
    {
        var host = Setup(kind, Gate());
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "Reference", "First");
        Assert.Throws<InvalidOperationException>(() => session.Bind("Cell", "Reference", "first"));
        Assert.Same(first, session.InstanceFor("Reference"));
        Assert.Single(host.Instances);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnEventReachesEveryAttachedScriptOnItsOwner(HostKind kind)
    {
        var gate = Gate();
        var host = Setup(kind, gate);
        var bridge = new PapyrusEventBridge(host, gate);
        var owner = PapyrusOwnerId.ForPlacedReference("000900:Probe.esp", "000800:Probe.esp");
        var seen = new List<string>();
        var overrides = new ScriptOverrideRegistry(gate);
        host.Overrides = overrides;
        foreach (var name in new[] { "First", "Second" })
        {
            overrides.Register(name, "OnActivate", 0, _ =>
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

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ParkingAnOwnerReportsEveryAttachedScript(HostKind kind)
    {
        var gate = Gate();
        var host = Setup(kind, gate);
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
    }
}
