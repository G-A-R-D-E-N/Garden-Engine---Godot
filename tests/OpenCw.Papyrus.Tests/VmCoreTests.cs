using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class VmCoreTests
{
    private static readonly (string Key, string Status)[] VmPolicyRows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (EvidenceKeys.Assignment, "PROVEN"),
        (EvidenceKeys.Arithmetic, "PROVEN"),
        (EvidenceKeys.Types, "PROVEN"),
        (EvidenceKeys.Dispatch, "PROVEN"),
        (EvidenceKeys.Arrays, "PROVEN"),
        (EvidenceKeys.Structs, "PROVEN"),
        (EvidenceKeys.Waits, "UNPROVEN"),
    };

    private static EvidenceRegistry Registry(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return EvidenceRegistry.Load($"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}");
    }

    private static EvidenceRegistry Synthetic() => Registry(VmPolicyRows);

    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts)
        {
            library.Add(script);
        }
        return library;
    }

    private static PapyrusFunction Function(string name, params PapyrusInstruction[] instructions) =>
        new(name, instructions);

    private static PapyrusOperand L(int slot) => PapyrusOperand.Local(slot);

    private static PapyrusOperand[] Args(params int[] slots) => slots.Select(L).ToArray();

    [Fact]
    public void TheRecordedRegistryGatesTheVmCore()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json");
        var registry = EvidenceRegistry.Load(File.ReadAllText(path));

        Assert.Equal(EvidenceStatus.Unproven, registry.StatusFor(EvidenceKeys.Assignment));
        Assert.Equal(EvidenceStatus.Unproven, registry.StatusFor(EvidenceKeys.Arithmetic));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(EvidenceKeys.Frames));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor(EvidenceKeys.OnInitBarrier));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.EventDuringOnInit));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.Waits));
    }

    [Fact]
    public void TheRegistryRejectsAForeignSchema()
    {
        Assert.Throws<FormatException>(() => EvidenceRegistry.Load("{\"schema\":\"other\",\"rows\":[{\"key\":\"a\",\"status\":\"PROVEN\"}]}"));
        Assert.Throws<FormatException>(() => EvidenceRegistry.Load("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{\"key\":\"a\",\"status\":\"MAYBE\"}]}"));
    }

    [Fact]
    public void TheGateAllowsProvenAndPolicyAndCountsApproximations()
    {
        var gate = new EvidenceGate(Synthetic());

        Assert.Equal(GateOutcome.Allowed, gate.Gate(EvidenceKeys.Frames).Outcome);
        Assert.Equal(GateOutcome.Allowed, gate.Gate(EvidenceKeys.Assignment).Outcome);
        Assert.Equal(GateOutcome.Approximated, gate.Gate(EvidenceKeys.EventDuringOnInit).Outcome);
        Assert.Equal(1, gate.ApproximationCount);
        Assert.Equal(0, gate.StopCount);
    }

    [Fact]
    public void TheGateStopsUnprovenAndUnknownKeys()
    {
        var gate = new EvidenceGate(Synthetic());

        var unproven = gate.Gate(EvidenceKeys.Waits);
        Assert.Equal(GateOutcome.Stopped, unproven.Outcome);
        Assert.Equal(EvidenceStatus.Unproven, unproven.Status);

        var unknown = gate.Gate("vm.not.a.row");
        Assert.Equal(GateOutcome.Stopped, unknown.Outcome);
        Assert.Equal(2, gate.StopCount);
        Assert.Equal(1, gate.Stops[EvidenceKeys.Waits]);
    }

    [Fact]
    public void ProgramsRunTheirControlFlow()
    {
        var script = new PapyrusScript("Flow", new[]
        {
            Function("Run",
                PapyrusInstruction.JumpIf(L(0), 2, whenTrue: false),
                PapyrusInstruction.Jump(3),
                PapyrusInstruction.Assign(L(1), L(0)),
                PapyrusInstruction.Return(L(1))),
        }, Array.Empty<string>());

        var vm = new PapyrusVm(Library(script), new EvidenceGate(Synthetic()));

        var taken = vm.Call(script, script.FindFunction("Run"), PapyrusValue.None,
            new[] { PapyrusValue.FromBool(false) });
        Assert.True(taken.Completed);
        Assert.False(taken.Value.Flag);

        var skipped = vm.Call(script, script.FindFunction("Run"), PapyrusValue.None,
            new[] { PapyrusValue.FromBool(true) });
        Assert.True(skipped.Completed);
        Assert.Equal(PapyrusValueKind.None, skipped.Value.Kind);
    }

    [Fact]
    public void EverySemanticIsGatedByTheRegistry()
    {
        var script = new PapyrusScript("Gated", new[]
        {
            Function("Run",
                PapyrusInstruction.Assign(L(1), L(0)),
                PapyrusInstruction.Return(L(1))),
        }, Array.Empty<string>());
        var library = Library(script);
        var arguments = new[] { PapyrusValue.FromInt(41) };

        var recorded = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(TestRepository.FindRoot()!,
            "docs", "proof", "papyrus-evidence-registry.json")));
        var gated = new PapyrusVm(library, new EvidenceGate(recorded))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None, arguments);
        Assert.Equal(VmStopReason.GateStopped, gated.Reason);
        Assert.Equal(EvidenceKeys.Parameters, gated.Key);

        var allowed = new PapyrusVm(library, new EvidenceGate(Synthetic()))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None, arguments);
        Assert.True(allowed.Completed);
        Assert.Equal(41, allowed.Value.Integer);
    }

    [Fact]
    public void TheFrameStackIsExplicitAndGuarded()
    {
        var functions = new List<PapyrusFunction>();
        const int depth = 300;
        for (var index = 0; index < depth - 1; ++index)
        {
            functions.Add(Function($"F{index}",
                PapyrusInstruction.CallStatic("Chain", $"F{index + 1}", L(0), Array.Empty<PapyrusOperand>())));
        }
        functions.Add(Function($"F{depth - 1}", PapyrusInstruction.Return(L(0))));
        var script = new PapyrusScript("Chain", functions, Array.Empty<string>());
        var library = Library(script);

        var deep = new PapyrusVm(library, new EvidenceGate(Synthetic()), frameDepthLimit: 512)
            .Call(script, script.FindFunction("F0"), PapyrusValue.None, Array.Empty<PapyrusValue>());
        Assert.True(deep.Completed);

        var limited = new PapyrusVm(library, new EvidenceGate(Synthetic()), frameDepthLimit: 256)
            .Call(script, script.FindFunction("F0"), PapyrusValue.None, Array.Empty<PapyrusValue>());
        Assert.Equal(VmStopReason.FrameDepthExceeded, limited.Reason);
        Assert.Contains("frames", limited.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBudgetStopsARunawayScript()
    {
        var script = new PapyrusScript("Spin", new[]
        {
            Function("Run", PapyrusInstruction.Jump(0)),
        }, Array.Empty<string>());

        var result = new PapyrusVm(Library(script), new EvidenceGate(Synthetic()), budget: 32)
            .Call(script, script.FindFunction("Run"), PapyrusValue.None, Array.Empty<PapyrusValue>());

        Assert.Equal(VmStopReason.BudgetExhausted, result.Reason);
        Assert.Equal(32, result.Instructions);
    }

    [Fact]
    public void ArithmeticAndCastsFollowTheGatedSemantics()
    {
        var script = new PapyrusScript("Math", new[]
        {
            Function("Run",
                PapyrusInstruction.Binary(PapyrusOpcode.Add, L(2), L(0), L(1)),
                PapyrusInstruction.Binary(PapyrusOpcode.Divide, L(3), L(2), L(1)),
                PapyrusInstruction.Binary(PapyrusOpcode.CompareGreater, L(4), L(2), L(1)),
                PapyrusInstruction.Cast(L(5), L(3), "Float"),
                PapyrusInstruction.Return(L(4))),
        }, Array.Empty<string>());

        var result = new PapyrusVm(Library(script), new EvidenceGate(Synthetic()))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None,
                new[] { PapyrusValue.FromInt(9), PapyrusValue.FromInt(4) });

        Assert.True(result.Completed);
        Assert.True(result.Value.Flag);
    }

    [Fact]
    public void UndefinedOperationsFailClosed()
    {
        var script = new PapyrusScript("Bad", new[]
        {
            Function("Run",
                PapyrusInstruction.Binary(PapyrusOpcode.Divide, L(2), L(0), L(1)),
                PapyrusInstruction.Return(L(2))),
        }, Array.Empty<string>());

        var result = new PapyrusVm(Library(script), new EvidenceGate(Synthetic()))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None,
                new[] { PapyrusValue.FromInt(1), PapyrusValue.FromInt(0) });

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("division by zero", result.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ArraysAndStructsStayInsideTheirBounds()
    {
        var script = new PapyrusScript("Data", new[]
        {
            Function("Run",
                PapyrusInstruction.ArrayCreate(L(2), PapyrusOperand.Constant(PapyrusValue.FromInt(0))),
                PapyrusInstruction.ArraySet(L(2), L(1), L(0)),
                PapyrusInstruction.ArrayGet(L(3), L(2), L(1)),
                PapyrusInstruction.StructCreate(L(4)),
                PapyrusInstruction.StructSet(L(4), "Member", L(3)),
                PapyrusInstruction.StructGet(L(5), L(4), "member"),
                PapyrusInstruction.Return(L(5))),
        }, Array.Empty<string>());

        var result = new PapyrusVm(Library(script), new EvidenceGate(Synthetic()))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None,
                new[] { PapyrusValue.FromInt(7), PapyrusValue.FromInt(0) });

        Assert.True(result.Completed);
        Assert.Equal(7, result.Value.Integer);
    }

    [Fact]
    public void TheNativeTargetContractRefusesGuesses()
    {
        var script = new PapyrusScript("Native", new[]
        {
            Function("Run",
                PapyrusInstruction.CallStatic("Debug", "Ping", L(1), Args()),
                PapyrusInstruction.CallMethod(L(0), "Touch", L(2), Args(), receiverType: "Native"),
                PapyrusInstruction.Return(L(2))),
        }, Array.Empty<string>());

        var host = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        host.RegisterNative(new NativeId("Debug", "Ping"), _ => PapyrusValue.FromInt(1));
        host.RegisterNative(new NativeId("Native", "Touch"), _ => PapyrusValue.FromInt(2),
            NativeTargetKind.ScriptInstance, "Native");
        var instance = host.Attach("Native");

        var withInstance = host.Vm.Call(instance.Script, instance.Script.FindFunction("Run"),
            PapyrusValue.None, new[] { PapyrusValue.FromObject(instance) });
        Assert.True(withInstance.Completed);
        Assert.Equal(2, withInstance.Value.Integer);

        var bare = host.Vm.Call(instance.Script, instance.Script.FindFunction("Run"),
            PapyrusValue.None, new[] { PapyrusValue.FromObject(new EngineObjectReference("00012345")) });
        Assert.Equal(VmStopReason.ModelError, bare.Reason);
        Assert.Contains("never guesses", bare.Message!, StringComparison.Ordinal);
        Assert.True(host.NativeTargetRefusals > 0);
    }

    [Fact]
    public void CallMethodCarriesReceiverResultAndArguments()
    {
        var script = new PapyrusScript("Order", new[]
        {
            Function("Run",
                PapyrusInstruction.CallMethod(L(0), "Echo", L(3), Args(1, 2)),
                PapyrusInstruction.Return(L(3))),
        }, Array.Empty<string>());
        var host = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        IReadOnlyList<PapyrusValue>? seen = null;
        host.RegisterNative(new NativeId("Order", "Echo"), arguments =>
        {
            seen = arguments;
            return PapyrusValue.FromInt(99);
        }, NativeTargetKind.ScriptInstance, "Order");
        var instance = host.Attach("Order");

        var result = host.Vm.Call(instance.Script, instance.Script.FindFunction("Run"), PapyrusValue.None,
            new[] { PapyrusValue.FromObject(instance), PapyrusValue.FromInt(11), PapyrusValue.FromInt(22) });

        Assert.True(result.Completed, result.Message);
        Assert.Equal(99, result.Value.Integer);
        Assert.Equal(new long[] { 11, 22 }, seen!.Select(value => value.Integer).ToArray());
    }

    [Fact]
    public void CallStaticCarriesScriptFunctionResultAndArguments()
    {
        var script = new PapyrusScript("Order", new[]
        {
            Function("Run",
                PapyrusInstruction.CallStatic("Debug", "Echo", L(3), Args(0, 1)),
                PapyrusInstruction.Return(L(3))),
        }, Array.Empty<string>());
        var host = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        IReadOnlyList<PapyrusValue>? seen = null;
        host.RegisterNative(new NativeId("Debug", "Echo"), arguments =>
        {
            seen = arguments;
            return PapyrusValue.FromInt(99);
        });

        var result = host.Vm.Call(script, script.FindFunction("Run"), PapyrusValue.None,
            new[] { PapyrusValue.FromInt(11), PapyrusValue.FromInt(22) });

        Assert.True(result.Completed, result.Message);
        Assert.Equal(99, result.Value.Integer);
        Assert.Equal(new long[] { 11, 22 }, seen!.Select(value => value.Integer).ToArray());
    }

    [Fact]
    public void CallParentRunsTheParentFunction()
    {
        var parent = new PapyrusScript("Base", new[]
        {
            Function("Twice", PapyrusInstruction.Binary(PapyrusOpcode.Add, L(1), L(0), L(0)),
                PapyrusInstruction.Return(L(1))) with { ParameterCount = 1 },
        }, Array.Empty<string>());
        var child = new PapyrusScript("Child", new[]
        {
            Function("Run",
                PapyrusInstruction.CallParent("Twice", L(3), Args(0)),
                PapyrusInstruction.Return(L(3))),
        }, Array.Empty<string>(), "Base");
        var vm = new PapyrusVm(Library(parent, child), new EvidenceGate(Synthetic()));

        var result = vm.Call(child, child.FindFunction("Run"), PapyrusValue.None, new[] { PapyrusValue.FromInt(21) });

        Assert.True(result.Completed, result.Message);
        Assert.Equal(42, result.Value.Integer);
    }

    [Fact]
    public void CallParentWithoutAKnownParentIsAModelError()
    {
        var orphan = new PapyrusScript("Orphan", new[]
        {
            Function("Run", PapyrusInstruction.CallParent("Run", L(0), Args())),
        }, Array.Empty<string>(), "Missing");
        var vm = new PapyrusVm(Library(orphan), new EvidenceGate(Synthetic()));

        Assert.Equal(VmStopReason.ModelError, vm.Call(orphan, orphan.FindFunction("Run"), PapyrusValue.None, Array.Empty<PapyrusValue>()).Reason);
    }

    [Fact]
    public void CallParentStopsAtTheUnprovenDispatchFamily()
    {
        var script = new PapyrusScript("Child", new[]
        {
            Function("Run",
                PapyrusInstruction.CallParent("Run", L(0), Args()),
                PapyrusInstruction.Return()),
        }, Array.Empty<string>());
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        var vm = new PapyrusVm(Library(script), new EvidenceGate(registry));

        var result = vm.Call(script, script.FindFunction("Run"), PapyrusValue.None, Array.Empty<PapyrusValue>());

        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal(EvidenceKeys.Dispatch, result.Key);
        Assert.Equal(EvidenceKeys.Dispatch, EvidenceKeys.For(PapyrusOpcode.CallParent));
    }

    [Fact]
    public void TheOnInitBarrierRefusesEventsUntilItCompletes()
    {
        var script = new PapyrusScript("Lifecycle", new[]
        {
            Function("OnInit", PapyrusInstruction.Return()),
            Function("Event", PapyrusInstruction.Return()),
        }, Array.Empty<string>());

        var host = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        var instance = host.Attach("Lifecycle");

        var before = host.RunEvent(instance, "Event");
        Assert.False(before.Completed);
        Assert.Equal(1, host.RefusedEvents);

        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.True(host.RunEvent(instance, "Event").Completed);
    }

    [Fact]
    public void AFailedOnInitLeavesTheScriptClosed()
    {
        var script = new PapyrusScript("Broken", new[]
        {
            Function("OnInit", PapyrusInstruction.Wait()),
            Function("Event", PapyrusInstruction.Return()),
        }, Array.Empty<string>());

        var host = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        var instance = host.Attach("Broken");

        var initialized = host.Initialize(instance);
        Assert.False(initialized.Completed);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.Equal(EvidenceKeys.Waits, initialized.Key);

        var after = host.RunEvent(instance, "Event");
        Assert.False(after.Completed);
        Assert.Contains("stays closed", after.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEventDuringOnInitIsOnlyQueuedAsACountedApproximation()
    {
        var script = new PapyrusScript("Reentrant", new[]
        {
            Function("OnInit",
                PapyrusInstruction.CallStatic("Debug", "Ping", L(1), Args()),
                PapyrusInstruction.Return()),
            Function("Event", PapyrusInstruction.Return()),
        }, Array.Empty<string>());

        ScriptHost? host = null;
        ScriptInstance? instance = null;
        var queueing = new ScriptHost(Library(script), new EvidenceGate(Synthetic()),
            onInitArrivalPolicy: OnInitArrivalPolicy.QueueAsApproximation);
        queueing.RegisterNative(new NativeId("Debug", "Ping"), _ =>
        {
            host!.RunEvent(instance!, "Event");
            return PapyrusValue.None;
        });
        host = queueing;
        instance = queueing.Attach("Reentrant");

        Assert.True(queueing.Initialize(instance).Completed);
        Assert.Equal(1, queueing.QueuedDuringOnInit);
        Assert.Equal(1, queueing.ReadyCount);
        Assert.True(queueing.RunStep(64) > 0);
        Assert.Equal(0, queueing.ReadyCount);

        var refusing = new ScriptHost(Library(script), new EvidenceGate(Synthetic()));
        refusing.RegisterNative(new NativeId("Debug", "Ping"), _ =>
        {
            refusing.RunEvent(refusing.Instances[0], "Event");
            return PapyrusValue.None;
        });
        var refusingInstance = refusing.Attach("Reentrant");
        Assert.True(refusing.Initialize(refusingInstance).Completed);
        Assert.Equal(0, refusing.QueuedDuringOnInit);
        Assert.Equal(1, refusing.RefusedEvents);
    }

    [Fact]
    public void TheLibraryIsCaseInsensitiveAndRefusesDuplicates()
    {
        var script = new PapyrusScript("QuestScript", new[] { Function("OnInit") }, Array.Empty<string>());
        var library = Library(script);

        Assert.True(library.TryGet("questscript", out var found));
        Assert.Same(script, found);
        Assert.Throws<InvalidOperationException>(() => library.Add(script));
        Assert.Throws<KeyNotFoundException>(() => library.Get("missing"));
        Assert.Single(library.Names);
    }

    [Fact]
    public void ValuesKeepTheirKindAndRenderInvariantly()
    {
        Assert.Equal(PapyrusValueKind.None, PapyrusValue.None.Kind);
        Assert.Equal("7", PapyrusValue.FromInt(7).ToString());
        Assert.Equal("1.5", PapyrusValue.FromFloat(1.5).ToString());
        Assert.Equal("True", PapyrusValue.FromBool(true).ToString());
        Assert.Equal("text", PapyrusValue.FromString("text").ToString());
        Assert.False(PapyrusValue.None.AsBool());
        Assert.Equal(3.5, PapyrusValue.FromFloat(3.5).AsNumber());
        Assert.Throws<InvalidOperationException>(() => PapyrusValue.FromString("x").AsNumber());
    }
}
