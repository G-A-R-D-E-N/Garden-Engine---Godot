using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ItemEventsTests : HostFixture
{
    private static readonly PapyrusOwnerId Chest = PapyrusOwnerId.ForPlacedReference("000800:Chests.esp", "000A00");

    private static EvidenceGate Gate() => TestHosts.Gate(
        (ActorEventFamilies.DirectKey, "ENGINE_POLICY"),
        (PapyrusEventBridge.KeyPrefix + ItemEvents.Added, "ENGINE_POLICY"),
        (PapyrusEventBridge.KeyPrefix + ItemEvents.Removed, "ENGINE_POLICY"));

    private (ItemEvents Items, List<string> Seen) Setup(HostKind kind)
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ScriptObject"), TestHosts.Stub("Form"), TestHosts.Stub("ObjectReference"),
            new ScriptSpec("ChestScript",
                "Scriptname ChestScript extends ObjectReference\n" +
                "Function Report(String tag, Form item, Int count) global native\n" +
                "Event OnItemAdded(Form akBaseItem, int aiItemCount, ObjectReference akItemReference, ObjectReference akSourceContainer)\n  Report(\"added\", akBaseItem, aiItemCount)\nEndEvent\n" +
                "Event OnItemRemoved(Form akBaseItem, int aiItemCount, ObjectReference akItemReference, ObjectReference akDestContainer)\n  Report(\"removed\", akBaseItem, aiItemCount)\nEndEvent\n")));
        var seen = new List<string>();
        host.RegisterNative(new NativeId("ChestScript", "Report"), arguments =>
        {
            seen.Add($"{arguments[0]}:{(arguments[1].Reference is EngineObjectReference item ? item.Identity : "None")}:{arguments[2].Integer}");
            return PapyrusValue.None;
        });
        var bridge = new PapyrusEventBridge(host, gate);
        var instance = host.Attach("ChestScript");
        host.Initialize(instance);
        bridge.Register(Chest, instance);
        return (new ItemEvents(new ActorEventFamilies(bridge, new ScriptRegistrationRegistry(), gate)), seen);
    }

    // Detects: a container whose item counts changed delivering nothing, or an unchanged item delivering an event; each grown item must send OnItemAdded and each shrunk item OnItemRemoved with the changed count.
    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void GrownAndShrunkItemsAreDeliveredWithTheirCounts(HostKind kind)
    {
        var (items, seen) = Setup(kind);
        var before = new Dictionary<string, int> { ["Item:A"] = 2, ["Item:B"] = 5, ["Item:C"] = 1 };
        var after = new Dictionary<string, int> { ["Item:A"] = 5, ["Item:B"] = 1, ["Item:C"] = 1, ["Item:D"] = 3 };

        var reports = items.Changed(Chest, before, after);

        Assert.All(reports, report => Assert.Equal(1, report.Delivered));
        Assert.Equal(new[] { "added:Item:A:3", "removed:Item:B:4", "added:Item:D:3" }, seen);
    }

    // Detects: an item that left the container entirely being missed because it is absent from the after counts; it must be reported removed with its whole count.
    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnItemAbsentAfterwardsIsReportedRemoved(HostKind kind)
    {
        var (items, seen) = Setup(kind);

        items.Changed(Chest, new Dictionary<string, int> { ["Item:A"] = 2 }, new Dictionary<string, int>());

        Assert.Equal(new[] { "removed:Item:A:2" }, seen);
    }

    [Fact]
    public void BothItemEventsAreListedAsEmittedNames()
    {
        Assert.Contains(PapyrusEventContract.Emitters, emitter => emitter.Name == ItemEvents.Added);
        Assert.Contains(PapyrusEventContract.Emitters, emitter => emitter.Name == ItemEvents.Removed);
    }
}
