using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PapyrusEventBridgeTests : HostFixture
{
    private const string Base = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (PapyrusEventBridge.KeyPrefix + "OnActivate", "PROVEN"),
        (PapyrusEventBridge.KeyPrefix + "OnUnproven", "UNPROVEN"),
    };

    private static PapyrusOwnerId Owner(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private (PapyrusEventBridge Bridge, IScriptHost Host, List<string> Seen) Setup(HostKind kind, params string[] references)
    {
        var gate = TestHosts.Gate(Rows);
        var host = Track(TestHosts.Create(kind, gate, TestHosts.Events("RefScript", "OnInit", "OnActivate(Int p)")));
        var seen = new List<string>();
        var overrides = new ScriptOverrideRegistry(gate);
        host.Overrides = overrides;
        overrides.Register("RefScript", "OnActivate", ScriptOverrideRegistry.AnyParameterCount, context =>
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

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AProvenEventReachesItsOwnerWithItsArguments(HostKind kind)
    {
        var (bridge, _, seen) = Setup(kind, "000A00");

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(42) }, Owner("000A00")));

        Assert.Equal(1, batch.Delivered);
        Assert.Equal(new[] { "42" }, seen);
        Assert.Equal(1, bridge.Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ABroadcastReachesEveryNonParkedOwner(HostKind kind)
    {
        var (bridge, _, seen) = Setup(kind, "000A00", "000A01", "000A02");

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(1) }));

        Assert.Equal(3, batch.Delivered);
        Assert.Equal(3, seen.Count);
        Assert.Equal(3, bridge.RegisteredOwners);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AParkedOwnerIsSkippedAndCounted(HostKind kind)
    {
        var (bridge, _, seen) = Setup(kind, "000A00", "000A01");
        bridge.Park(Owner("000A01"));

        var batch = bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(7) }));

        Assert.Equal(1, batch.Delivered);
        Assert.Equal(1, batch.SkippedParked);
        Assert.Equal(1, seen.Count);
        Assert.Equal(1, bridge.SkippedParked);

        bridge.Unpark(Owner("000A01"));
        Assert.Equal(2, bridge.Send(new EngineEvent("OnActivate", new[] { PapyrusValue.FromInt(8) })).Delivered);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenEventIsStillSentToItsHandlerAndCountedAsAStop(HostKind kind)
    {
        var gate = TestHosts.Gate(Rows);
        var host = Track(TestHosts.Create(kind, gate, TestHosts.Events("RefScript", "OnInit", "OnUnproven")));
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("RefScript");
        host.Initialize(instance);
        bridge.Register(Owner("000A00"), instance);

        var batch = bridge.Send(new EngineEvent("OnUnproven", Array.Empty<PapyrusValue>(), Owner("000A00")));

        Assert.Equal(1, batch.Delivered);
        Assert.Equal(1, gate.Stops[PapyrusEventBridge.KeyPrefix + "OnUnproven"]);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnOwnerWithoutTheHandlerIsSkipped(HostKind kind)
    {
        var gate = TestHosts.Gate(Rows);
        var host = Track(TestHosts.Create(kind, gate, TestHosts.Events("NoHandlerScript", "OnInit")));
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("NoHandlerScript");
        host.Initialize(instance);
        bridge.Register(Owner("000A00"), instance);

        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>()));

        Assert.Equal(1, batch.NoHandler);
        Assert.Equal(0, batch.Delivered);
        Assert.Equal(1, bridge.NoHandler);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnknownTargetIsReported(HostKind kind)
    {
        var (bridge, _, seen) = Setup(kind, "000A00");

        var batch = bridge.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>(), Owner("000A09")));

        Assert.Equal(1, batch.UnknownOwner);
        Assert.Empty(seen);
        Assert.Equal(1, bridge.UnknownOwner);
    }

}
