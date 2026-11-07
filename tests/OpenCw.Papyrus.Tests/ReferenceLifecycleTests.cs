using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ReferenceLifecycleTests
{
    private const string Base = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (ReferenceLifecycle.LifecycleKey, "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnLoad", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCellAttach", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnCellDetach", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnUnload", "PROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (ReferenceLifecycle Lifecycle, PapyrusEventBridge Bridge, List<string> Seen) Setup(
        EvidenceGate gate, string reference = "000A00")
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("RefScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnLoad", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnUnload", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnCellAttach", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnCellDetach", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        foreach (var name in new[] { "OnLoad", "OnUnload", "OnCellAttach", "OnCellDetach" })
        {
            host.Overrides.Register("RefScript", name, ScriptOverrideRegistry.AnyParameterCount, context =>
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

    [Fact]
    public void LoadingDeliversOnLoadThroughTheBridge()
    {
        var (lifecycle, bridge, seen) = Setup(Gate());

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Loaded);

        Assert.True(outcome.Delivered);
        Assert.Equal(new[] { "OnLoad" }, seen);
        Assert.Equal(1, bridge.Delivered);
        Assert.Equal(1, lifecycle.Delivered);
    }

    [Fact]
    public void CellAttachAndDetachDeliverTheirEvents()
    {
        var (lifecycle, bridge, seen) = Setup(Gate());

        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.CellAttached).Delivered);
        Assert.True(lifecycle.Transition(Owner("000A00"), LifecycleTransition.CellDetached).Delivered);

        Assert.Equal(new[] { "OnCellAttach", "OnCellDetach" }, seen);
        Assert.Equal(2, bridge.Delivered);
    }

    [Fact]
    public void AnUnprovenUnloadParksTheOwnerAndCountsTheUncertainty()
    {
        var (lifecycle, bridge, seen) = Setup(Gate((ReferenceLifecycle.UnloadedKey, "UNPROVEN")));

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Unloaded);

        Assert.False(outcome.Delivered);
        Assert.True(outcome.Parked);
        Assert.Contains("unproven", outcome.Reason!, StringComparison.Ordinal);
        Assert.Empty(seen);
        Assert.Equal(1, lifecycle.UnloadedUncertainty);
        Assert.Equal(1, lifecycle.Parked);
        Assert.Equal(0, bridge.Delivered);
    }

    [Fact]
    public void AProvenUnloadDeliversAndRemovesTheUncertainty()
    {
        var (lifecycle, bridge, seen) = Setup(Gate((ReferenceLifecycle.UnloadedKey, "PROVEN")));

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Unloaded);

        Assert.True(outcome.Delivered);
        Assert.False(outcome.Parked);
        Assert.Equal(new[] { "OnUnload" }, seen);
        Assert.Equal(0, lifecycle.UnloadedUncertainty);
        Assert.Equal(0, lifecycle.Parked);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void TheUncertaintyCountIsExactAndOnlyProofRemovesIt()
    {
        var unproven = Setup(Gate((ReferenceLifecycle.UnloadedKey, "UNPROVEN"))).Lifecycle;
        Assert.Equal(0, unproven.UnloadedUncertainty);
        unproven.Transition(Owner("000A00"), LifecycleTransition.Unloaded);
        unproven.Transition(Owner("000A00"), LifecycleTransition.Unloaded);
        Assert.Equal(2, unproven.UnloadedUncertainty);

        var proven = Setup(Gate((ReferenceLifecycle.UnloadedKey, "PROVEN"))).Lifecycle;
        proven.Transition(Owner("000A00"), LifecycleTransition.Unloaded);
        Assert.Equal(0, proven.UnloadedUncertainty);
    }

    [Fact]
    public void AnUnprovenLifecycleFamilyDeliversNothing()
    {
        var (lifecycle, bridge, seen) = Setup(Gate((ReferenceLifecycle.LifecycleKey, "UNPROVEN")));

        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Loaded);

        Assert.False(outcome.Delivered);
        Assert.Empty(seen);
        Assert.Equal(0, bridge.Delivered);
    }

    [Fact]
    public void AParkedOwnerStopsReceivingLifecycleEvents()
    {
        var (lifecycle, bridge, seen) = Setup(Gate((ReferenceLifecycle.UnloadedKey, "UNPROVEN")));

        lifecycle.Transition(Owner("000A00"), LifecycleTransition.Unloaded);
        var outcome = lifecycle.Transition(Owner("000A00"), LifecycleTransition.Loaded);

        Assert.False(outcome.Delivered);
        Assert.Empty(seen);
        Assert.Equal(1, lifecycle.UnloadedUncertainty);
        Assert.Equal(0, bridge.Delivered);
    }
}
