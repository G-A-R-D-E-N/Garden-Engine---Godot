using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PapyrusEventBridgeTests
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
        (PapyrusEventBridge.KeyPrefix + "OnActivate", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnUnproven", "UNPROVEN"),
    };

    private static EvidenceGate Gate()
    {
        var entries = Rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static PapyrusScript Script(string name, params string[] handlers) =>
        new(name, handlers.Select(handler => new PapyrusFunction(handler, new[] { PapyrusInstruction.Return() })
            { ParameterCount = handler == "OnActivate" ? 1 : 0 })
            .ToList(), Array.Empty<string>());

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (PapyrusEventBridge Bridge, ScriptHost Host, List<string> Seen) Setup(params string[] references)
    {
        var gate = Gate();
        var library = new ScriptLibrary();
        library.Add(Script("RefScript", "OnInit", "OnActivate"));
        library.Add(Script("NoHandlerScript", "OnInit"));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("RefScript", "OnActivate", ScriptOverrideRegistry.AnyParameterCount, context =>
        {
            seen.Add(context.Arguments.Count > 0 ? context.Arguments[0].ToString() : "");
            return OverrideResult.Handled(PapyrusValue.None);
        });

        var bridge = new PapyrusEventBridge(host, gate);
        foreach (var reference in references)
        {
            var instance = host.Attach("RefScript");
            host.Initialize(instance);
            bridge.Register(Owner(reference), instance);
        }
        return (bridge, host, seen);
    }

    [Fact]
    public void AProvenEventReachesItsOwnerWithItsArguments()
    {
        var (bridge, _, seen) = Setup("000A00");

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(42) }, Owner("000A00")));

        Assert.Equal(1, batch.Delivered);
        Assert.Equal(new[] { "42" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Fact]
    public void ABroadcastReachesEveryNonParkedOwner()
    {
        var (bridge, _, seen) = Setup("000A00", "000A01", "000A02");

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(1) }));

        Assert.Equal(3, batch.Delivered);
        Assert.Equal(3, seen.Count);
        Assert.Equal(3, bridge.RegisteredOwners);
    }

    [Fact]
    public void AParkedOwnerIsSkippedAndCounted()
    {
        var (bridge, _, seen) = Setup("000A00", "000A01");
        bridge.Park(Owner("000A01"));

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(7) }));

        Assert.Equal(1, batch.Delivered);
        Assert.Equal(1, batch.SkippedParked);
        Assert.Equal(1, seen.Count);
        Assert.Equal(1, bridge.SkippedParked);

        bridge.Unpark(Owner("000A01"));
        Assert.Equal(2, bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(8) })).Delivered);
    }

    [Fact]
    public void AnUnprovenEventIsRefusedRatherThanSent()
    {
        var (bridge, _, seen) = Setup("000A00", "000A01");

        var batch = bridge.Send(new EngineEvent("OnUnproven", new[] { PapyrusValue.FromInt(1) }));

        Assert.Equal(0, batch.Delivered);
        Assert.Equal(2, batch.Refused);
        Assert.Empty(seen);
        Assert.Equal(2, bridge.Refused);
    }

    [Fact]
    public void AnOwnerWithoutTheHandlerIsSkipped()
    {
        var gate = Gate();
        var library = new ScriptLibrary();
        library.Add(Script("NoHandlerScript", "OnInit"));
        var host = new ScriptHost(library, gate);
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("NoHandlerScript");
        host.Initialize(instance);
        bridge.Register(Owner("000A00"), instance);

        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>()));

        Assert.Equal(1, batch.NoHandler);
        Assert.Equal(0, batch.Delivered);
        Assert.Equal(1, bridge.NoHandler);
    }

    [Fact]
    public void AnUnknownTargetIsReported()
    {
        var (bridge, _, seen) = Setup("000A00");

        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>(), Owner("000A09")));

        Assert.Equal(1, batch.UnknownOwner);
        Assert.Empty(seen);
        Assert.Equal(1, bridge.UnknownOwner);
    }
}
