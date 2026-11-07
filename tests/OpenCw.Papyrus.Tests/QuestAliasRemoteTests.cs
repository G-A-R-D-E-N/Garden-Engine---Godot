using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestAliasRemoteTests
{
    private const string Quest = "000800:Quests.esp";
    private static readonly PapyrusOwnerId Sender = PapyrusOwnerId.ForForm(Quest);
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForQuestAlias(Quest, 1);
    private const string Event = QuestAliasRuntime.StageEventName;

    private static (QuestAliasRuntime Runtime, ScriptRegistrationRegistry Registry,
        PapyrusEventBridge Bridge, List<string> Seen) Setup(bool eventAllowed = true)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.Host, EvidenceKeys.FailClosed,
            ScriptOverrideRegistry.Key, QuestAliasRuntime.RuntimeKey };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}").ToList();
        rows.Add($"{{\"key\":\"event.{Event}\",\"status\":\"{(eventAllowed ? "ENGINE_POLICY" : "UNPROVEN")}\"}}");
        var gate = new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
        var handler = PapyrusEventContract.HandlerName(Event);
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("AliasScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction(handler, new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var seen = new List<string>();
        host.Overrides.Register("AliasScript", handler, ScriptOverrideRegistry.AnyParameterCount, context =>
        {
            seen.Add(context.Function);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var registry = new ScriptRegistrationRegistry();
        var bridge = new PapyrusEventBridge(host, gate);
        var runtime = new QuestAliasRuntime(host, bridge, registry, gate);
        var fills = new AliasFillReport(new[]
        {
            new AliasOutcome(Owner, AliasFillKind.ForcedReference, AliasState.Filled, "000A00", "filled"),
        }, 1, 0, Array.Empty<string>());
        Assert.Equal(1, runtime.Attach(fills, new[]
        {
            new QuestAliasScript(Quest, 1, "AliasScript", Array.Empty<OpenCommonwealth.Services.Papyrus.Vmad.VmadValueShape>(), Array.Empty<int>()),
        }).Attached);
        return (runtime, registry, bridge, seen);
    }

    [Fact]
    public void ACustomSubscriptionCannotAuthorizeAnUnidentifiedRemoteSource()
    {
        var (runtime, registry, bridge, seen) = Setup();
        registry.Register(Owner, ScriptRegistration.ForCustomEvent(Event));
        var report = runtime.StageSet(new[] { Owner });
        Assert.Equal(0, report.StageDelivered);
        Assert.Equal(1, report.StageRefused);
        Assert.Equal(0, bridge.Delivered);
        Assert.Empty(seen);
    }

    private static string SavedRemote(string? source) =>
        "{\"version\":2,\"threads\":[],\"timers\":[],\"objectives\":[],\"registrations\":[" +
        "{\"owner\":\"alias:000800:Quests.esp#1\",\"kind\":\"RemoteEvent\",\"event\":\"Quest.OnStageSet\",\"interval\":0,\"repeats\":false,\"clock\":\"RealTime\",\"source\":" +
        JsonSerializer.Serialize(source) + "}]}";

    [Fact]
    public void SavedRemoteMetadataPreservesTheExactSource()
    {
        var state = PapyrusSaveState.Deserialize(SavedRemote(Sender.Key));
        using var json = JsonDocument.Parse(state.Serialize());
        Assert.True(json.RootElement.GetProperty("registrations")[0].TryGetProperty("source", out var source));
        Assert.Equal(Sender.Key, source.GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void SavedRemoteMetadataWithoutASourceRefuses(string? source) =>
        Assert.Throws<FormatException>(() => PapyrusSaveState.Deserialize(SavedRemote(source)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLessRemoteMetadataCannotBypassValidation(bool restoring)
    {
        var state = PapyrusSaveStates.Capture(Array.Empty<SavedThread>(), new[]
        {
            new SavedRegistration(Owner.Key, "RemoteEvent", Event, 0, false, "RealTime"),
        }, Array.Empty<SavedTimer>(), Array.Empty<SavedObjective>());
        if (restoring)
            Assert.Throws<FormatException>(() => PapyrusSaveStates.Restore(state, new Dictionary<string, string>()));
        else
            Assert.Throws<FormatException>(() => state.Serialize());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void RemoteDeliveryNeedsTheExactSourceEventAndRecipient(int condition, bool delivered)
    {
        var (runtime, registry, bridge, seen) = Setup(condition != 8);
        var source = condition == 1 ? PapyrusOwnerId.ForForm("000801:Quests.esp") : Sender;
        var name = condition == 2 ? "Quest.OnOtherStage" : Event.ToLowerInvariant();
        if (condition != 5)
            registry.Register(condition == 4 ? PapyrusOwnerId.ForQuestAlias(Quest, 2) : Owner,
                condition == 3 ? ScriptRegistration.ForCustomEvent(Event) : ScriptRegistration.ForRemoteEvent(source, name));
        if (condition == 9)
            registry.Register(Owner, ScriptRegistration.ForRemoteEvent(Sender, Event));
        PapyrusOwnerId? supplied = condition == 6 ? null : condition == 7 ? default(PapyrusOwnerId) : Sender;
        var report = runtime.StageSet(new[] { Owner }, supplied);
        Assert.Equal(delivered ? 1 : 0, report.StageDelivered);
        Assert.Equal(delivered ? 0 : 1, report.StageRefused);
        Assert.Equal(report.StageDelivered, runtime.StageDelivered);
        Assert.Equal(report.StageRefused, runtime.StageRefused);
        Assert.Equal(delivered, Assert.Single(report.Stages).Delivered);
        Assert.Equal(report.StageDelivered, bridge.Delivered);
        Assert.Equal(condition == 8 ? 1 : 0, bridge.Refused);
        Assert.Equal(report.StageDelivered, seen.Count);
    }

    [Fact]
    public void RemoteMatchingPreservesOwnerIdentityKindHandlesAndUnregistration()
    {
        var registry = new ScriptRegistrationRegistry();
        var first = registry.Register(Owner, ScriptRegistration.ForRemoteEvent(Sender, Event));
        var second = registry.Register(Owner, ScriptRegistration.ForRemoteEvent(Sender, Event.ToLowerInvariant()));
        var dynamicSource = PapyrusOwnerId.ForDynamicReference(Quest, "other-source");
        registry.Register(Owner, ScriptRegistration.ForRemoteEvent(dynamicSource, Event));
        registry.Register(Owner, ScriptRegistration.ForCustomEvent(Event));
        Assert.Equal(new[] { first.Handle, second.Handle }, registry.MatchRemoteEvent(Sender, Event).Select(entry => entry.Handle));
        Assert.Single(registry.MatchRemoteEvent(dynamicSource, Event));
        Assert.Single(registry.MatchCustomEvent(Event));
        Assert.Equal(0, registry.Advance(ScriptRegistrationRegistry.SessionOwner, 100, ClockSource.RealTime));
        Assert.Equal(4, registry.Count);
        Assert.True(registry.Unregister(first.Handle));
        Assert.Equal(second.Handle, Assert.Single(registry.MatchRemoteEvent(Sender, Event)).Handle);
        Assert.Equal(3, registry.UnregisterAll(Owner));
        Assert.Empty(registry.MatchRemoteEvent(Sender, Event));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidRemotePayloadsRefuseBeforeRegistryMutation(int defect)
    {
        var registry = new ScriptRegistrationRegistry();
        if (defect == 0)
            Assert.Throws<ArgumentException>(() => ScriptRegistration.ForRemoteEvent(default, Event));
        else if (defect == 1)
            Assert.Throws<ArgumentException>(() => ScriptRegistration.ForRemoteEvent(Sender, " "));
        else
            Assert.Throws<ArgumentException>(() => registry.Register(Owner,
                new ScriptRegistration(RegistrationKind.RemoteEvent, defect == 2 ? Event : " ", 0, false, ClockSource.RealTime)
                { Source = defect == 2 ? null : Sender }));
        Assert.Empty(registry.Entries);
        Assert.Equal(1, registry.Register(Owner, ScriptRegistration.ForRemoteEvent(Sender, Event)).Handle);
    }

    [Fact]
    public void RemoteSourceSurvivesCaptureSerializationAndRestoreReporting()
    {
        var registry = new ScriptRegistrationRegistry();
        var entry = registry.Register(Owner, ScriptRegistration.ForRemoteEvent(Sender, Event));
        var registration = entry.Registration;
        var state = PapyrusSaveStates.Capture(Array.Empty<SavedThread>(), new[]
        {
            new SavedRegistration(entry.Owner.Key, registration.Kind.ToString(), registration.EventName,
                registration.IntervalSeconds, registration.Repeats, registration.Clock.ToString(), registration.Source!.Value.Key),
        }, Array.Empty<SavedTimer>(), Array.Empty<SavedObjective>());
        var decoded = PapyrusSaveState.Deserialize(state.Serialize());
        var restored = PapyrusSaveStates.Restore(decoded, new Dictionary<string, string>());
        Assert.Equal(Sender.Key, Assert.Single(restored.Registrations).SourceOwnerKey);
        Assert.Equal(state.Serialize(), decoded.Serialize());
        Assert.Equal(state.Digest(), decoded.Digest());
    }
}
