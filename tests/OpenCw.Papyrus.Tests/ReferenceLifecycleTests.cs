using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceLifecycleTests : HostFixture
{
    private const string Base = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (ReferenceLifecycle.LifecycleKey, "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnLoad", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCellAttach", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCellDetach", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnUnload", "PROVEN"),
    };

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private (ReferenceLifecycle Lifecycle, PapyrusEventBridge Bridge, List<string> Seen) Setup(
        HostKind kind, EvidenceGate gate, string reference = "000A00")
    {
        var host = Track(TestHosts.Create(kind, gate, TestHosts.Events("RefScript",
            "OnInit", "OnLoad", "OnUnload", "OnCellAttach", "OnCellDetach")));
        var seen = new List<string>();
        var overrides = new ScriptOverrideRegistry(gate);
        host.Overrides = overrides;
        foreach (var name in new[] { "OnLoad", "OnUnload", "OnCellAttach", "OnCellDetach" })
        {
            overrides.Register("RefScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
            {
                seen.Add(context.Function);
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }

        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("RefScript");
        host.Initialize(instance);
        bridge.Register(Owner(reference), instance);
        return (new ReferenceLifecycle(bridge, gate), bridge, seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void LoadingDeliversOnLoadThroughTheBridge(HostKind kind)
    {
        var (lifecycle, bridge, seen) = Setup(kind, TestHosts.Gate(Rows));

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Loaded);

        Assert.True(outcome.Delivered);
        Assert.Equal(new[] { "OnLoad" }, seen);
        Assert.Equal(1, bridge.Delivered);
        Assert.Equal(1, lifecycle.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void CellAttachAndDetachDeliverTheirEvents(HostKind kind)
    {
        var (lifecycle, bridge, seen) = Setup(kind, TestHosts.Gate(Rows));

        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.CellAttached).Delivered);
        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.CellDetached).Delivered);

        Assert.Equal(new[] { "OnCellAttach", "OnCellDetach" }, seen);
        Assert.Equal(2, bridge.Delivered);
    }


    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AProvenUnloadDeliversAndRemovesTheUncertainty(HostKind kind)
    {
        var (lifecycle, bridge, seen) = Setup(kind,
            TestHosts.Gate(Rows.Append((ReferenceLifecycle.UnloadedKey, "PROVEN")).ToArray()));

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Unloaded);

        Assert.True(outcome.Delivered);
        Assert.False(outcome.Parked);
        Assert.Equal(new[] { "OnUnload" }, seen);
        Assert.Equal(0, lifecycle.UnloadedUncertainty);
        Assert.Equal(0, lifecycle.Parked);
        Assert.Equal(1, bridge.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnloadedOwnerStillReceivesLifecycleEventsAndNothingIsParked(HostKind kind)
    {
        var (lifecycle, bridge, seen) = Setup(kind,
            TestHosts.Gate(Rows.Append((ReferenceLifecycle.UnloadedKey, "UNPROVEN")).ToArray()));

        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.Unloaded).Delivered);
        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.Loaded).Delivered);

        Assert.Equal(new[] { "OnUnload", "OnLoad" }, seen);
        Assert.Equal(0, lifecycle.UnloadedUncertainty);
        Assert.Equal(0, lifecycle.Parked);
        Assert.Equal(2, lifecycle.Delivered);
        Assert.Equal(2, bridge.Delivered);
    }
}
