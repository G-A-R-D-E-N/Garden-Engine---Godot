using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class LatentNativeTests
{
    private const string WaitKey = "vm.native.latent";
    private static readonly NativeId Answer = new("External", "Answer");
    private static readonly NativeId Observe = new("External", "Observe");

    private static EvidenceGate Gate(bool allowWait = true)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.NativeTarget, EvidenceKeys.OnInitBarrier, EvidenceKeys.SchedulerBudget,
            EvidenceKeys.Dispatch, ScriptOverrideRegistry.Key, PapyrusEventBridge.KeyPrefix + "OnQuick" };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}").ToList();
        if (allowWait) rows.Add($"{{\"key\":\"{WaitKey}\",\"status\":\"ENGINE_POLICY\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
    }

    private static ScriptHost Host(out List<PapyrusValue> observed, Func<long>? token = null,
        bool allowWait = true, bool method = false)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Caller", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                method
                    ? PapyrusInstruction.CallMethod(PapyrusOperand.Constant(PapyrusValue.FromObject(new EngineObjectReference("message"))), "Answer",
                        PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>(), "External")
                    : PapyrusInstruction.CallStatic("External", "Answer", PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard, new[] { PapyrusOperand.Local(0) }),
                PapyrusInstruction.Return(),
            }),
            new PapyrusFunction("Quick", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("OnQuick", new[]
            {
                PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard,
                    new[] { PapyrusOperand.Constant(PapyrusValue.FromInt(99)) }),
                PapyrusInstruction.Return(),
            }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate(allowWait));
        host.RegisterNative(Answer, _ => throw new NativeWait(token?.Invoke() ?? 1),
            method ? NativeTargetKind.EngineObject : NativeTargetKind.Static);
        var values = new List<PapyrusValue>();
        host.RegisterNative(Observe, args => { values.Add(args[0]); return PapyrusValue.None; });
        observed = values;
        return host;
    }

    private static ScriptInstance Attach(ScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectEventWaitsAndResumesPastTheNativeCall(bool method)
    {
        var host = Host(out var observed, method: method);
        var result = host.RunEvent(Attach(host), "Run");
        Assert.Equal(VmStopReason.Waiting, result.Reason);
        Assert.Empty(observed);
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(7)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(7), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.FromInt(9)));
    }

    [Fact]
    public void WaitingSurvivesMoreThanEightStepsWithoutSpendingOrParking()
    {
        var host = Host(out var observed);
        var scheduler = new ScriptScheduler(host, budget: 16, maxParksPerEvent: 2);
        scheduler.Enqueue(Attach(host), "Run");
        var first = scheduler.RunStep();
        Assert.Equal(1, first.Waiting);
        for (var index = 0; index < 12; index++)
        {
            var step = scheduler.RunStep();
            Assert.Equal(0, step.Spent);
            Assert.Equal(1, step.Waiting);
        }
        Assert.Equal(1, scheduler.WaitingEvents);
        Assert.Equal(0, scheduler.ParkedEvents);
        Assert.Equal(0, scheduler.DroppedEvents);
        Assert.Equal(0, scheduler.ReadyCount);
        Assert.Empty(observed);
        Assert.True(scheduler.CompleteLatent(1, PapyrusValue.FromInt(42)));
        Assert.Equal(1, scheduler.RunStep().Completed);
        Assert.Equal(PapyrusValue.FromInt(42), Assert.Single(observed));
    }

    [Fact]
    public void UnknownAndDuplicateCompletionsAreRefusedAndCounted()
    {
        var host = Host(out var observed);
        host.RunEvent(Attach(host), "Run");
        Assert.False(host.CompleteLatent(99, PapyrusValue.None));
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(3)));
        Assert.False(host.CompleteLatent(1, PapyrusValue.FromInt(4)));
        host.RunStep(32);
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
        Assert.Equal(3, host.RefusedCompletions);
        Assert.Contains("unknown or already completed", host.LastCompletionRefusal);
        Assert.Equal(PapyrusValue.FromInt(3), Assert.Single(observed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADetachedCompletionIsHeldUntilTheSameInstanceReattaches(bool completeBeforeDetach)
    {
        var host = Host(out var observed);
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, session.Activate("reference", "Run").Result!.Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        Assert.Same(instance, Assert.Single(session.DetachCell("cell")));
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
        for (var index = 0; index < 12; index++) Assert.Equal(0, session.Tick(32));
        Assert.Empty(observed);
        Assert.Equal(1, host.WaitingCount);
        Assert.Same(instance, Assert.Single(session.ReattachCell("cell")));
        session.Tick(32);
        Assert.Equal(PapyrusValue.FromInt(5), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectEventsRefuseDetachedInstancesWithoutLosingLatentAnswers(bool completeBeforeDetach, bool useOverride)
    {
        var host = Host(out var observed);
        if (useOverride)
        {
            host.Overrides = new ScriptOverrideRegistry(Gate());
            host.Overrides.Register("Caller", "OnQuick", 0, _ =>
            {
                observed.Add(PapyrusValue.FromInt(99));
                return OverrideResult.Handled(PapyrusValue.None);
            });
        }
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        session.DetachCell("cell");

        foreach (var function in new[] { "OnQuick", "Run" })
        {
            var result = host.RunEvent(instance, function);
            Assert.Equal(VmStopReason.ModelError, result.Reason);
            Assert.Contains("detached", result.Message!);
            Assert.Equal(0, result.Instructions);
        }
        Assert.Equal(2, host.RefusedEvents);
        Assert.Equal(0, host.Overrides?.HandledCount ?? 0);
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        for (var index = 0; index < 12; index++) Assert.Equal(0, session.Tick(32));
        Assert.Empty(observed);
        Assert.Equal(1, host.WaitingCount);
        Assert.Equal(1, host.WaitingEvents);
        Assert.Equal(0, host.ParkedEvents);
        Assert.Equal(0, host.ReadyCount);
        Assert.Equal(0, host.RefusedCompletions);

        Assert.Same(instance, Assert.Single(session.ReattachCell("cell")));
        Assert.True(session.Tick(32) > 0);
        Assert.Equal(PapyrusValue.FromInt(5), Assert.Single(observed));
        Assert.Equal(0, host.WaitingCount);
        Assert.True(host.RunEvent(instance, "OnQuick").Completed);
        Assert.Equal(new[] { PapyrusValue.FromInt(5), PapyrusValue.FromInt(99) }, observed);
        Assert.Equal(2, host.RefusedEvents);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BridgeSkipsDetachedRecipientsAndRetainsTheirLatentAnswers(bool completeBeforeDetach, bool broadcast)
    {
        var host = Host(out var observed);
        var session = new CellScriptSession(host);
        var detached = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(detached).Completed);
        var active = Attach(host);
        var owner = PapyrusOwnerId.ForPlacedReference("Base", "a");
        var other = PapyrusOwnerId.ForPlacedReference("Base", "b");
        var bridge = new PapyrusEventBridge(host, Gate());
        bridge.Register(owner, detached);
        bridge.Register(other, active);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(detached, "Run").Reason);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        session.DetachCell("cell");

        var batch = bridge.Send(new EngineEvent("OnQuick", Array.Empty<PapyrusValue>(), broadcast ? null : owner));

        Assert.Equal(EventDelivery.SkippedParked, batch.Outcomes[0].Result);
        Assert.Equal(1, batch.SkippedParked);
        Assert.Equal(broadcast ? 1 : 0, batch.Delivered);
        Assert.Equal(0, batch.Refused);
        Assert.Equal(0, batch.Pending);
        Assert.Equal(0, host.RefusedEvents);
        Assert.Equal(broadcast ? new[] { PapyrusValue.FromInt(99) } : Array.Empty<PapyrusValue>(), observed);
        if (broadcast) Assert.Equal(EventDelivery.Delivered, batch.Outcomes[1].Result);
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(5)));
        for (var index = 0; index < 12; index++) Assert.Equal(0, session.Tick(32));
        Assert.Equal(1, host.WaitingCount);
        Assert.Equal(1, host.WaitingEvents);
        Assert.Equal(0, host.ParkedEvents);
        Assert.Equal(0, host.RefusedCompletions);

        Assert.Same(detached, Assert.Single(session.ReattachCell("cell")));
        Assert.True(session.Tick(32) > 0);
        Assert.Equal(PapyrusValue.FromInt(5), observed[^1]);
        Assert.Equal(broadcast ? 2 : 1, observed.Count);
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(broadcast ? 1 : 0, bridge.Delivered);
        Assert.Equal(1, bridge.SkippedParked);
        Assert.Equal(0, bridge.Pending);
        Assert.Equal(0, bridge.Refused);
        var attachedBatch = bridge.Send(new EngineEvent("OnQuick", Array.Empty<PapyrusValue>(), owner));
        Assert.Equal(1, attachedBatch.Delivered);
        Assert.Equal(0, attachedBatch.SkippedParked);
        Assert.Equal(broadcast ? 2 : 1, bridge.Delivered);
        Assert.Equal(1, bridge.SkippedParked);
    }

    [Fact]
    public void WaitingDoesNotStarveReadyEvents()
    {
        var host = Host(out var observed);
        var scheduler = new ScriptScheduler(host);
        var instance = Attach(host);
        scheduler.Enqueue(instance, "Run");
        scheduler.Enqueue(instance, "Quick");
        var result = scheduler.RunStep();
        Assert.Equal(1, result.Completed);
        Assert.Equal(1, result.Waiting);
        Assert.Empty(observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWaitingThreadRoundTripsAndRestoreRejectsItsExternalAnswer(bool completedWhileDetached)
    {
        var host = Host(out _);
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Caller");
        Assert.True(host.Initialize(instance).Completed);
        host.RunEvent(instance, "Run");
        session.DetachCell("cell");
        if (completedWhileDetached) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(2)));
        var thread = Assert.Single(host.CaptureWaitingThreads(_ => "owner", _ => "fingerprint"));
        Assert.Equal(1L, thread.WaitingToken);
        Assert.Equal("External.Answer", thread.WaitingNative);
        Assert.Equal(1, thread.Pc);
        var state = PapyrusSaveStates.Capture(new[] { thread }, Array.Empty<SavedRegistration>(),
            Array.Empty<SavedTimer>(), Array.Empty<SavedObjective>());
        var restored = PapyrusSaveState.Deserialize(state.Serialize());
        Assert.Equal(state.Digest(), restored.Digest());
        var report = PapyrusSaveStates.Restore(restored, new Dictionary<string, string> { ["Caller"] = "fingerprint" });
        Assert.Equal(0, report.Resumed);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(ThreadRestoreState.RejectedLatentWait, Assert.Single(report.Threads).State);
        Assert.Contains("External.Answer", report.Threads[0].Reason);
        Assert.Contains("token 1", report.Threads[0].Reason);
    }

    [Fact]
    public void AReusedTokenCannotAcquireASecondWaitingEvent()
    {
        var host = Host(out var observed);
        var instance = Attach(host);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(1)));
        host.RunStep(32);
        Assert.Equal(VmStopReason.ModelError, host.RunEvent(instance, "Run").Reason);
        Assert.Equal(0, host.WaitingCount);
        Assert.Single(observed);
    }

    [Fact]
    public void AnUnprovenWaitStopsInsteadOfCapturingAContinuation()
    {
        var host = Host(out var observed, allowWait: false);
        var result = host.RunEvent(Attach(host), "Run");
        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal(WaitKey, result.Key);
        Assert.Empty(observed);
        Assert.Equal(0, host.WaitingCount);
    }

    [Fact]
    public void VersionOneCannotBeReadAsTheNewWaitingThreadShape()
    {
        var state = PapyrusSaveStates.Capture(Array.Empty<SavedThread>(), Array.Empty<SavedRegistration>(),
            Array.Empty<SavedTimer>(), Array.Empty<SavedObjective>()) with { Version = 1 };
        Assert.Throws<FormatException>(() => PapyrusSaveState.Deserialize(state.Serialize()));
    }

    [Fact]
    public void CompletionTargetsOnlyItsOwnWaitingFrame()
    {
        long next = 0;
        var host = Host(out var observed, () => ++next);
        host.RunEvent(Attach(host), "Run");
        host.RunEvent(Attach(host), "Run");
        Assert.Equal(2, host.WaitingCount);
        Assert.True(host.CompleteLatent(2, PapyrusValue.FromInt(22)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(22), Assert.Single(observed));
        Assert.Equal(1, host.WaitingCount);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(11)));
        host.RunStep(32);
        Assert.Equal(new[] { PapyrusValue.FromInt(22), PapyrusValue.FromInt(11) }, observed);
    }

    [Fact]
    public void AWaitInsideAScriptCallRetainsTheCallerAndReturnDestination()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Nested", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                PapyrusInstruction.CallStatic("Nested", "Inner", PapyrusOperand.Local(1), Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard, new[] { PapyrusOperand.Local(1) }),
                PapyrusInstruction.Return(),
            }),
            new PapyrusFunction("Inner", new[]
            {
                PapyrusInstruction.CallStatic("External", "Answer", PapyrusOperand.Local(0), Array.Empty<PapyrusOperand>()),
                new PapyrusInstruction(PapyrusOpcode.Return) { Left = PapyrusOperand.Local(0) },
            }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        PapyrusValue? observed = null;
        host.RegisterNative(Answer, _ => throw new NativeWait(1));
        host.RegisterNative(Observe, args => { observed = args[0]; return PapyrusValue.None; });
        var instance = host.Attach("Nested");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(8)));
        host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(8), observed);
    }

    [Theory]
    [InlineData(PapyrusOperandKind.Slot)]
    [InlineData(PapyrusOperandKind.Self)]
    public void AWaitWithAnInvalidReturnDestinationStopsInsteadOfThrowingOnCompletion(PapyrusOperandKind kind)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Bad", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                PapyrusInstruction.CallStatic("External", "Answer", new PapyrusOperand(kind, 99), Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.Return(),
            }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        host.RegisterNative(Answer, _ => throw new NativeWait(1));
        var instance = host.Attach("Bad");
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(VmStopReason.ModelError, host.RunEvent(instance, "Run").Reason);
        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
        Assert.Equal(0, host.RunStep(32));
    }
}
