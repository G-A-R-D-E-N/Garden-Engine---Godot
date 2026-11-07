using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Conditions;
using OpenCommonwealth.Services.Papyrus.Dialogue;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class WaitingCallerTests
{
    private static EvidenceGate Gate()
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.NativeTarget, EvidenceKeys.OnInitBarrier, EvidenceKeys.SchedulerBudget,
            EvidenceKeys.Dispatch, NativeWait.Key, QuestStageDispatch.TimingKey,
            DialogueFragments.TimingKey, DialogueFragments.DispatchKey, DialogueFragments.NativesKey,
            SceneRuntime.ControlKey, SceneRuntime.DispatchKey, MagicPerkRuntime.PerkFragmentKey,
            PapyrusEventBridge.KeyPrefix + "OnWait", UiServices.FragmentKey };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
    }

    private static (ScriptHost Host, ScriptInstance Instance, List<string> Seen) Setup(int parameters = 0,
        int? beginParameters = null)
    {
        var library = new ScriptLibrary();
        PapyrusFunction Observe(string function, string value, int count = 0) => new(function, new[]
        {
            PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard,
                new[] { PapyrusOperand.Constant(PapyrusValue.FromString(value)) }),
            PapyrusInstruction.Return(),
        }, ParameterCount: count);
        PapyrusFunction Wait(string name) => new(name, new[]
        {
            PapyrusInstruction.CallStatic("External", "Answer", PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>()),
            PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard, new[] { PapyrusOperand.Local(0) }),
            PapyrusInstruction.Return(),
        }, ParameterCount: parameters);
        library.Add(new PapyrusScript("Caller", new[]
        {
            Observe("Before", "before", beginParameters ?? parameters), Wait("Wait"), Wait("OnWait"),
            Observe("After", "after"),
        }, Array.Empty<string>()));
        library.Add(new PapyrusScript("LaterOwner", new[] { Observe("OnWait", "later") }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(1));
        var seen = new List<string>();
        host.RegisterNative(new NativeId("External", "Observe"), args =>
        {
            seen.Add(args[0].ToString());
            return PapyrusValue.None;
        });
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return (host, instance, seen);
    }

    private static void Reason(string? reason)
    {
        Assert.Contains("latent", reason);
        Assert.Contains("External.Answer", reason);
        Assert.Contains("no automatic continuation", reason);
    }

    private static void Complete(ScriptHost host, List<string> seen, params string[] expected)
    {
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        Assert.True(host.RunStep(64) > 0);
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(expected, seen);
        Assert.Equal(0, host.RunStep(64));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StageStopsAtPendingAndKeepsEarlierCompletedFragments(bool earlier)
    {
        var (host, instance, seen) = Setup();
        var dispatch = new QuestStageDispatch(host, Gate());
        var names = earlier ? new[] { "Before", "Wait", "After" } : new[] { "Wait", "After" };
        var input = new StageDispatchInput("Quest", "Caller", names.Select((name, order) =>
            new StageFragment(10, order, "Caller", name, true)).ToArray());
        var report = dispatch.Dispatch(input, 10, instance);
        Assert.Equal(FragmentOutcome.Pending, report.Results.Last().Outcome);
        Assert.Equal("Wait", report.Results.Last().Function);
        Reason(report.Results.Last().Reason);
        Assert.False(report.Applied);
        Assert.Equal(earlier ? 1 : 0, report.Ran);
        Assert.Equal(earlier ? 2 : 1, report.Results.Count);
        if (earlier) Assert.Equal(FragmentOutcome.Ran, report.Results[0].Outcome);
        Assert.Equal(1, dispatch.Pending);
        Assert.Equal(1, report.Pending);
        Assert.Equal(0, dispatch.RefusedStages);
        Assert.Equal(0, dispatch.Deferred);
        Assert.Equal(earlier ? new[] { "before" } : Array.Empty<string>(), seen);
        Complete(host, seen, earlier ? new[] { "before", "42" } : new[] { "42" });
        Assert.Equal(1, dispatch.Pending);
        Assert.Equal(earlier ? 1 : 0, dispatch.Ran);
        Assert.Equal(0, dispatch.RefusedStages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogueBeginOrEndWaitIsPendingWithoutPlayingOrBlocking(bool endWait)
    {
        var (host, instance, seen) = Setup(endWait ? 0 : 1, 1);
        var fragments = new DialogueFragments(host, new ConditionEvaluator(Gate()), Gate());
        var arguments = new[] { PapyrusValue.FromInt(7) };
        var line = new DialogueLine("Topic", "Info", "Caller", endWait ? "Before" : "Wait",
            endWait ? "Wait" : "After", arguments, Array.Empty<ConditionInput>());
        var outcome = fragments.Play(line, instance);
        Assert.Equal(DialogueState.Pending, outcome.State);
        Assert.Equal(endWait ? 1 : 0, outcome.FragmentsRun);
        Reason(outcome.Reason);
        Assert.Equal(1, fragments.Pending);
        Assert.Equal(1, fragments.Report(new[] { outcome }).Pending);
        Assert.Equal(0, fragments.Played);
        Assert.Equal(0, fragments.Blocked);
        Assert.Equal(endWait ? arguments : Array.Empty<PapyrusValue>(), fragments.SeenArguments);
        Assert.Equal(endWait ? new[] { "before" } : Array.Empty<string>(), seen);
        Complete(host, seen, endWait ? new[] { "before", "42" } : new[] { "42" });
        Assert.Equal(1, fragments.Pending);
        Assert.Equal(0, fragments.Played);
        Assert.Equal(0, fragments.Blocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SceneWaitIsPendingAndStopsRemainingActions(bool earlier)
    {
        var (host, instance, seen) = Setup();
        var runtime = new SceneRuntime(host, Gate());
        var names = earlier ? new[] { "Before", "Wait", "After" } : new[] { "Wait", "After" };
        var scene = new SceneInput("Scene", "Caller", new[] { new ScenePhase(0,
            names.Select((name, order) => new SceneAction(order, name)).ToArray()) });
        Assert.True(runtime.Control(scene, "Start").Applied);
        var outcome = runtime.RunPhase(scene, instance);
        Assert.True(outcome.Pending);
        Assert.False(outcome.Applied);
        Assert.Equal(earlier ? 1 : 0, outcome.FragmentsRun);
        Reason(outcome.Reason);
        Assert.Contains("Wait", outcome.Reason);
        Assert.Equal(1, runtime.Pending);
        Assert.Equal(1, runtime.Report(new[] { outcome }).Pending);
        Assert.Equal(1, runtime.Applied);
        Assert.Equal(0, runtime.Refused);
        Assert.Equal(earlier ? new[] { "before" } : Array.Empty<string>(), seen);
        Complete(host, seen, earlier ? new[] { "before", "42" } : new[] { "42" });
        Assert.Equal(1, runtime.Pending);
        Assert.Equal(1, runtime.Applied);
        Assert.Equal(0, runtime.Refused);
    }

    [Fact]
    public void PerkFragmentWaitIsPendingInsteadOfAppliedRefusedOrBlocked()
    {
        var (host, instance, seen) = Setup();
        var runtime = new MagicPerkRuntime(host, Gate());
        var owner = PapyrusOwnerId.ForPlacedReference("Actor", "reference");
        var outcome = runtime.RunFragment(owner, "Wait", instance, false);
        Assert.Equal(MagicState.Pending, outcome.State);
        Assert.Equal("Wait", outcome.Operation);
        Reason(outcome.Reason);
        Assert.Equal(1, runtime.Pending);
        Assert.Equal(1, runtime.Report(new[] { outcome }).Pending);
        Assert.Equal(0, runtime.Applied);
        Assert.Equal(0, runtime.Refused);
        Assert.Equal(0, runtime.Blocked);
        Assert.Empty(seen);
        Complete(host, seen, "42");
        Assert.Equal(1, runtime.Pending);
        Assert.Equal(0, runtime.Applied);
        Assert.Equal(0, runtime.Refused);
        Assert.Equal(0, runtime.Blocked);
    }

    [Fact]
    public void BridgeWaitIsPendingWithoutStoppingDeliveryToLaterOwners()
    {
        var (host, instance, seen) = Setup();
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
        Assert.Contains("External.Answer", batch.Outcomes[0].Reason);
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

    [Fact]
    public void AMiddleBroadcastWaitDoesNotSuppressTheThirdRecipient()
    {
        var (host, waiting, seen) = Setup();
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

    [Fact]
    public void UiFragmentWaitReturnsFalseWithoutRefusal()
    {
        var (host, instance, seen) = Setup();
        var services = new UiServices(host, new RecordingUiService(), Gate());
        Assert.False(services.RunFragment(instance, "Wait"));
        Assert.Equal(0, services.Refused);
        Assert.Equal(0, services.Blocked);
        Assert.Equal(1, services.Pending);
        Reason(services.LastFragmentReason);
        Assert.Empty(seen);
        Complete(host, seen, "42");
        Assert.Equal(1, services.Pending);
        Assert.Equal(0, services.Refused);
        Assert.Equal(0, services.Blocked);
    }

    [Fact]
    public void CellActivationAlreadyPreservesTheWaitingResult()
    {
        var (host, _, seen) = Setup();
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

    [Fact]
    public void OrdinaryRefusalsStillCountInEveryCaller()
    {
        var (host, instance, _) = Setup();
        var dispatch = new QuestStageDispatch(host, Gate());
        Assert.False(dispatch.Dispatch(new StageDispatchInput("Quest", "Caller",
            new[] { new StageFragment(10, 0, "Caller", "Missing", true) }), 10, instance).Applied);
        Assert.Equal(1, dispatch.RefusedStages);
        var dialogue = new DialogueFragments(host, new ConditionEvaluator(Gate()), Gate());
        Assert.Equal(DialogueState.Refused, dialogue.Play(new DialogueLine("Topic", "Info", "Caller", "Missing",
            null, Array.Empty<PapyrusValue>(), Array.Empty<ConditionInput>()), instance).State);
        Assert.Equal(1, dialogue.Blocked);
        var scene = new SceneRuntime(host, Gate());
        var input = new SceneInput("Scene", "Caller", new[] { new ScenePhase(0, new[] { new SceneAction(0, "Missing") }) });
        scene.Control(input, "Start");
        Assert.False(scene.RunPhase(input, instance).Applied);
        Assert.Equal(1, scene.Refused);
        var magic = new MagicPerkRuntime(host, Gate());
        var owner = PapyrusOwnerId.ForPlacedReference("Actor", "reference");
        Assert.Equal(MagicState.Refused, magic.RunFragment(owner, "Missing", instance, false).State);
        Assert.Equal(1, magic.Refused);
        var bridge = new PapyrusEventBridge(host, Gate());
        bridge.Register(owner, host.Attach("Caller"));
        Assert.Equal(EventDelivery.Refused, Assert.Single(bridge.Send(
            new EngineEvent("OnWait", Array.Empty<PapyrusValue>())).Outcomes).Result);
        Assert.Equal(1, bridge.Refused);
        var ui = new UiServices(host, new RecordingUiService(), Gate());
        Assert.False(ui.RunFragment(instance, "Missing"));
        Assert.Equal(1, ui.Refused);
    }
}
