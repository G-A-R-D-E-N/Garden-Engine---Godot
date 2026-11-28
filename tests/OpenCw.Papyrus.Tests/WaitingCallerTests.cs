using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class WaitingCallerTests : HostFixture
{
    private static readonly string[] Keys =
    {
        EvidenceKeys.Frames,
        EvidenceKeys.Return,
        EvidenceKeys.Parameters,
        EvidenceKeys.NativeTarget,
        EvidenceKeys.OnInitBarrier,
        EvidenceKeys.SchedulerBudget,
        EvidenceKeys.Dispatch,
        NativeWait.Key,
        QuestRuntime.FragmentTimingKey,
        PapyrusEventBridge.KeyPrefix + "OnWait",
    };

    private static readonly ScriptSpec ExternalSpec = new("External",
        "Scriptname External\n" +
        "Int Function Answer() global native\n" +
        "Function Observe(String value) global native\n" +
        "Function Report(Int value) global native\n");

    private static readonly ScriptSpec LaterOwnerSpec = new("LaterOwner",
        "Scriptname LaterOwner\n" +
        Handler("OnWait", 0, "External.Observe(\"later\")"));

    private static EvidenceGate Gate() => TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray());

    private static string Parameters(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(index => $"Int arg{index}"));

    private static string Handler(string name, int parameters, params string[] body) =>
        $"Function {name}({Parameters(parameters)})\n" +
        string.Concat(body.Select(line => "  " + line + "\n")) +
        "EndFunction\n";

    private static string Caller(int parameters) =>
        "Scriptname Caller\n" +
        Handler("Wait", parameters, "Int got = External.Answer()", "External.Report(got)") +
        Handler("OnWait", parameters, "Int got = External.Answer()", "External.Report(got)");

    private (IScriptHost Host, ScriptInstance Instance, List<string> Seen) Setup(HostKind kind,
        int parameters = 0)
    {
        var host = Track(TestHosts.Create(kind, Gate(),
            new ScriptSpec("Caller", Caller(parameters)),
            LaterOwnerSpec, ExternalSpec));
        host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(1));
        var seen = new List<string>();
        host.RegisterNative(new NativeId("External", "Observe"), arguments =>
        {
            seen.Add(arguments[0].ToString());
            return PapyrusValue.None;
        });
        host.RegisterNative(new NativeId("External", "Report"), arguments =>
        {
            seen.Add(arguments[0].ToString());
            return PapyrusValue.None;
        });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return (host, instance, seen);
    }

    private static void Complete(IScriptHost host, List<string> seen, params string[] expected)
    {
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        Assert.Equal(1, host.RunStep(64));
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(expected, seen.Select(value => value.ToLowerInvariant()));
        Assert.Equal(0, host.RunStep(64));
    }




    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void BridgeWaitIsPendingWithoutStoppingDeliveryToLaterOwners(HostKind kind)
    {
        var (host, instance, seen) = Setup(kind);
        var bridge = new PapyrusEventBridge(host, Gate());
        var first = PapyrusOwnerId.ForPlacedReference("Actor", "a");
        var second = PapyrusOwnerId.ForPlacedReference("Actor", "b");
        bridge.Register(first, instance);
        var later = host.Attach("LaterOwner");
        Assert.True(host.Initialize(later).Completed);
        bridge.Register(second, later);
        var batch = bridge.Send(new EngineEvent("OnWait", Array.Empty<PapyrusValue>()));
        Assert.Equal(EventDelivery.Pending, batch.Outcomes[0].Result);
        Assert.Equal(new[] { first, second }, batch.Outcomes.Select(outcome => outcome.Owner));
        Assert.Equal(EventDelivery.Delivered, batch.Outcomes[1].Result);
        Assert.Contains("latent", batch.Outcomes[0].Reason);
        Assert.DoesNotContain("later owners", batch.Outcomes[0].Reason);
        Assert.Equal(1, bridge.Pending);
        Assert.Equal(1, batch.Pending);
        Assert.Equal(1, batch.Delivered);
        Assert.Equal(1, bridge.Delivered);
        Assert.Equal(0, bridge.Refused + bridge.SkippedParked + bridge.NoHandler + bridge.UnknownOwner);
        Assert.Equal("later", Assert.Single(seen));
        Complete(host, seen, "later", "42");
        Assert.Equal(1, bridge.Pending);
        Assert.Equal(1, bridge.Delivered);
        Assert.Equal(0, bridge.Refused + bridge.SkippedParked + bridge.NoHandler + bridge.UnknownOwner);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AMiddleBroadcastWaitDoesNotSuppressTheThirdRecipient(HostKind kind)
    {
        var (host, waiting, seen) = Setup(kind);
        var bridge = new PapyrusEventBridge(host, Gate());
        var owners = new[] { "a", "b", "c" }
            .Select(reference => PapyrusOwnerId.ForPlacedReference("Actor", reference)).ToArray();
        var first = host.Attach("LaterOwner");
        var third = host.Attach("LaterOwner");
        Assert.True(host.Initialize(first).Completed);
        Assert.True(host.Initialize(third).Completed);
        bridge.Register(owners[2], third);
        bridge.Register(owners[1], waiting);
        bridge.Register(owners[0], first);
        var batch = bridge.Send(new EngineEvent("OnWait", Array.Empty<PapyrusValue>()));
        Assert.Equal(new[] { EventDelivery.Delivered, EventDelivery.Pending, EventDelivery.Delivered },
            batch.Outcomes.Select(outcome => outcome.Result));
        Assert.Equal(owners, batch.Outcomes.Select(outcome => outcome.Owner));
        Assert.Equal(2, batch.Delivered);
        Assert.Equal(1, batch.Pending);
        Assert.Equal(0, batch.Refused + batch.SkippedParked + batch.NoHandler + batch.UnknownOwner);
        Assert.Equal(new[] { "later", "later" }, seen);
        Assert.Equal(2, bridge.Delivered);
        Assert.Equal(1, bridge.Pending);
        Complete(host, seen, "later", "later", "42");
        Assert.Equal(2, bridge.Delivered);
        Assert.Equal(1, bridge.Pending);
        Assert.Equal(0, bridge.Refused + bridge.SkippedParked + bridge.NoHandler + bridge.UnknownOwner);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void CellActivationAlreadyPreservesTheWaitingResult(HostKind kind)
    {
        var (host, _, seen) = Setup(kind);
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        var activation = session.Activate("reference", "Wait");
        Assert.True(activation.Reached);
        Assert.Equal(VmStopReason.Waiting, activation.Result!.Reason);
        Assert.Equal(0, session.RefusedActivations);
        Complete(host, seen, "42");
        Assert.Equal(1, session.Activations);
        Assert.Equal(0, session.RefusedActivations);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OrdinaryRefusalsStillCountInEveryCaller(HostKind kind)
    {
        var (host, _, _) = Setup(kind);
        var owner = PapyrusOwnerId.ForPlacedReference("Actor", "reference");
        var bridge = new PapyrusEventBridge(host, Gate());
        bridge.Register(owner, host.Attach("Caller"));
        Assert.Equal(EventDelivery.Refused, Assert.Single(bridge.Send(
            new EngineEvent("OnWait", Array.Empty<PapyrusValue>())).Outcomes).Result);
        Assert.Equal(1, bridge.Refused);
    }
}
