using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Acceptance;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Seq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ReviewRegressionTests
{
    private static EvidenceGate Gate(string? unproven = null)
    {
        var keys = typeof(EvidenceKeys).GetFields()
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Concat(new[] { QuestRuntime.StartupOrderingKey, ScriptOverrideRegistry.Key, ScriptOverrideRegistry.ReflectionKey })
            .Distinct();
        return new EvidenceGate(EvidenceRegistry.Load(JsonSerializer.Serialize(new
        {
            schema = EvidenceRegistry.SchemaName,
            rows = keys.Select(key => new { key, status = key == unproven ? "UNPROVEN" : "ENGINE_POLICY" }),
        })));
    }

    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts) library.Add(script);
        return library;
    }

    private static PapyrusScript Script(params PapyrusFunction[] functions) =>
        new("Review", functions, Array.Empty<string>());

    private static PapyrusFunction Function(string name, params PapyrusInstruction[] body) => new(name, body);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeqRecoveryRestoresTheLastStartupFile(bool hasIncoming)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencw_seq_review_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "startup.json");
        try
        {
            File.WriteAllText(path + ".obsolete", "last committed startup data");
            if (hasIncoming) File.WriteAllText(path + ".incoming", "interrupted replacement");
            SeqStartupConverter.Recover(path);
            Assert.True(File.Exists(path));
            Assert.Equal("last committed startup data", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".incoming"));
            Assert.False(File.Exists(path + ".obsolete"));
            SeqStartupConverter.Recover(path);
            Assert.Equal("last committed startup data", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SeqRecoveryKeepsTheCompletedSwapAndRemovesItsBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencw_seq_review_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "startup.json");
        try
        {
            File.WriteAllText(path, "committed replacement");
            File.WriteAllText(path + ".obsolete", "previous data");
            File.WriteAllText(path + ".incoming", "partial next write");
            SeqStartupConverter.Recover(path);
            Assert.Equal("committed replacement", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".obsolete"));
            Assert.False(File.Exists(path + ".incoming"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public sealed class AuditOverride
    {
        public int Calls { get; private set; }

        [ScriptOverride("Review", "Run")]
        public OverrideResult Run(OverrideContext context)
        {
            Calls++;
            return OverrideResult.Handled(PapyrusValue.None);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ReflectedOverridesRefuseWrongDeclaredArgumentCounts(int count)
    {
        var script = Script(new PapyrusFunction("Run", Array.Empty<PapyrusInstruction>(), ParameterCount: 1));
        var gate = Gate();
        var host = new ScriptHost(Library(script), gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        var target = new AuditOverride();
        host.Overrides.RegisterFromReflection(target);
        var wrong = Enumerable.Repeat(PapyrusValue.None, count).ToArray();
        var result = host.RunEvent(instance, "Run", wrong);
        Assert.False(result.Completed);
        Assert.Equal(ScriptOverrideRegistry.Key, result.Key);
        Assert.Equal(0, target.Calls);
        Assert.Equal(1, host.Overrides.RefusedCount);
        Assert.Equal(1, host.RefusedEvents);
        Assert.True(host.RunEvent(instance, "Run", PapyrusValue.None).Completed);
        Assert.Equal(1, target.Calls);
    }

    [Fact]
    public void RegisteredArityCannotOverrideTheDeclaredArgumentCount()
    {
        var script = Script(new PapyrusFunction("Run", Array.Empty<PapyrusInstruction>(), ParameterCount: 1));
        var gate = Gate();
        var host = new ScriptHost(Library(script), gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        var calls = 0;
        host.Overrides.Register(script.Name, "Run", 0, _ => { calls++; return OverrideResult.Handled(PapyrusValue.None); });
        Assert.False(host.RunEvent(instance, "Run").Completed);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RepeatedDetachKeepsParkedInstancesAndTheirReferenceBindings()
    {
        var script = Script();
        var host = new ScriptHost(Library(script), Gate());
        var session = new CellScriptSession(host);
        var first = session.Bind("Cell", "First", script.Name,
            new Dictionary<string, PapyrusValue> { ["Value"] = PapyrusValue.FromInt(7) });
        var second = session.Bind("Cell", "Second", script.Name);
        Assert.True(host.Initialize(first).Completed);
        session.DetachCell("Cell");
        session.DetachCell("Cell");
        Assert.Equal(2, session.ParkedInstances);
        Assert.Equal(2, session.ReattachCell("Cell").Count);
        Assert.Same(first, session.InstanceFor("First"));
        Assert.Same(second, session.InstanceFor("Second"));
        Assert.Equal(ScriptLifecycle.Ready, first.Lifecycle);
        Assert.Equal(7, first.Properties["Value"].Integer);
    }

    [Fact]
    public void FailedInitializationStaysFailedOnRetry()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Unsupported("deliberate failure")));
        var host = new ScriptHost(Library(script), Gate());
        var instance = host.Attach(script.Name);
        Assert.False(host.Initialize(instance).Completed);
        var failure = instance.Failure;
        var instructions = host.Vm.InstructionCount;
        var retry = host.Initialize(instance);
        Assert.False(retry.Completed);
        Assert.Equal(VmStopReason.ModelError, retry.Reason);
        Assert.Equal(EvidenceKeys.OnInitBarrier, retry.Key);
        Assert.Equal(failure, instance.Failure);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
        Assert.Equal(instructions, host.Vm.InstructionCount);
    }

    [Theory]
    [InlineData("Same.esm", "Same.esp")]
    [InlineData("Same.esp", "Same.esm")]
    [InlineData("Same.esl", "Same.esp")]
    public void AcceptanceDoesNotMatchADifferentPluginExtension(string owner, string loaded)
    {
        var report = LoadOrderAcceptance.Run(Array.Empty<AcceptanceScript>(), Array.Empty<string>(),
            new[] { new QuestStartupInput("Quest", true, Array.Empty<string>(), 0, 0, owner) },
            loadOrder: new[] { loaded });
        Assert.False(report.Clean);
        Assert.Equal(1, report.UnknownStartupPlugins);
    }

    [Fact]
    public void AcceptanceRefusesAnAmbiguousExtensionlessPlugin()
    {
        var report = LoadOrderAcceptance.Run(Array.Empty<AcceptanceScript>(), Array.Empty<string>(),
            new[] { new QuestStartupInput("Quest", true, Array.Empty<string>(), 0, 0, "Same") },
            loadOrder: new[] { "Same.esm", "Same.esp" });
        Assert.False(report.Clean);
        Assert.Equal(1, report.UnknownStartupPlugins);
    }

    [Fact]
    public void AcceptanceMatchesAnExactPluginEvenWhenItsStemCollides()
    {
        var report = LoadOrderAcceptance.Run(Array.Empty<AcceptanceScript>(), Array.Empty<string>(),
            new[] { new QuestStartupInput("Quest", true, Array.Empty<string>(), 0, 0, "SAME.ESP") },
            loadOrder: new[] { "Same.esm", "Same.esp" });
        Assert.True(report.Clean);
        Assert.Equal(0, report.UnknownStartupPlugins);
    }

    [Fact]
    public void NamedCallsSelectOnlyTheRequestedFunction()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return(PapyrusOperand.Constant(PapyrusValue.FromInt(1)))),
            Function("Other", PapyrusInstruction.Return(PapyrusOperand.Constant(PapyrusValue.FromInt(2)))));
        var vm = new PapyrusVm(Library(script), Gate());
        Assert.Equal(2, vm.Call("Review", "other").Value.Integer);
        Assert.Equal(VmStopReason.ModelError, vm.Call("Review", "Missing").Reason);
    }

    [Fact]
    public void InitializationWithoutOnInitDoesNotRunAnArbitraryEvent()
    {
        var script = Script(Function("OnActivate", PapyrusInstruction.Unsupported("must not run")));
        var host = new ScriptHost(Library(script), Gate());
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(0, host.Vm.InstructionCount);
    }

    [Fact]
    public void ReturnsAndFallthroughUseTheSemanticGate()
    {
        foreach (var body in new[] { Array.Empty<PapyrusInstruction>(), new[] { PapyrusInstruction.Return() } })
        {
            var script = Script(Function("Run", body));
            var gate = Gate("vm.control_flow.return");
            var result = new PapyrusVm(Library(script), gate).Call(script, script.FindFunction("Run"),
                PapyrusValue.None, Array.Empty<PapyrusValue>());
            Assert.Equal(VmStopReason.GateStopped, result.Reason);
            Assert.Equal("vm.control_flow.return", result.Key);
            Assert.Equal(1, gate.StopCount);
        }
    }

    [Fact]
    public void EntryArgumentsUseTheParameterGate()
    {
        var script = Script(new PapyrusFunction("Run", new[] { PapyrusInstruction.Return() }, ParameterCount: 1));
        var result = new PapyrusVm(Library(script), Gate("vm.parameters.passing"))
            .Call(script, script.FindFunction("Run"), PapyrusValue.None, new[] { PapyrusValue.FromInt(3) });
        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal("vm.parameters.passing", result.Key);
    }

    [Fact]
    public void NativeArgumentsUseTheParameterGateBeforeSideEffects()
    {
        var script = Script(Function("Run", PapyrusInstruction.CallStatic("Review", "Count",
            PapyrusOperand.Discard, new[] { PapyrusOperand.Constant(PapyrusValue.FromInt(3)) })));
        var host = new ScriptHost(Library(script), Gate(EvidenceKeys.Parameters));
        var calls = 0;
        host.RegisterNative(new NativeId("Review", "Count"), _ => { calls++; return PapyrusValue.None; });
        var result = host.Vm.Call("Review", "Run");
        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal(EvidenceKeys.Parameters, result.Key);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ThrowingNativeFailsInitializationClosed()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.CallStatic("Review", "Throw",
            PapyrusOperand.Discard, Array.Empty<PapyrusOperand>())));
        var host = new ScriptHost(Library(script), Gate());
        host.RegisterNative(new NativeId("Review", "Throw"), _ => throw new ArgumentException("native failed"));
        var instance = host.Attach(script.Name);
        Assert.Equal(VmStopReason.ModelError, host.Initialize(instance).Reason);
        Assert.Equal(ScriptLifecycle.Failed, instance.Lifecycle);
    }

    [Fact]
    public void HostBudgetDoesNotRepeatCompletedSideEffects()
    {
        var call = PapyrusInstruction.CallStatic("Review", "Count", PapyrusOperand.Discard, Array.Empty<PapyrusOperand>());
        var script = Script(Function("OnInit", PapyrusInstruction.Return()),
            Function("First", Enumerable.Repeat(PapyrusInstruction.Nop(), 59).Append(call).Append(PapyrusInstruction.Return()).ToArray()),
            Function("Second", Enumerable.Repeat(PapyrusInstruction.Nop(), 28).Append(call).Append(PapyrusInstruction.Return()).ToArray()));
        var host = new ScriptHost(Library(script), Gate());
        var calls = 0;
        host.RegisterNative(new NativeId("Review", "Count"), _ => { calls++; return PapyrusValue.None; });
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        host.Enqueue(instance, "First");
        host.Enqueue(instance, "Second");
        for (var tick = 0; tick < 10 && host.ReadyCount > 0; tick++) Assert.InRange(host.RunStep(100), 0, 100);
        Assert.Equal(2, calls);
        Assert.Equal(0, host.ReadyCount);
        Assert.Equal(0, host.RunStep(100));
    }

    [Fact]
    public void BudgetSlicesDoNotRestartNativeEffects()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()), Function("Run",
            PapyrusInstruction.CallStatic("Review", "Count", PapyrusOperand.Discard, Array.Empty<PapyrusOperand>()),
            PapyrusInstruction.Nop(), PapyrusInstruction.Nop(), PapyrusInstruction.Return()));
        var library = Library(script);
        var gate = Gate();
        var host = new ScriptHost(library, gate);
        var calls = 0;
        host.RegisterNative(new NativeId("Review", "Count"), _ => { calls++; return PapyrusValue.None; });
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        var scheduler = new ScriptScheduler(host, budget: 2);
        scheduler.Enqueue(instance, "Run");
        for (var tick = 0; tick < 4 && scheduler.ReadyCount > 0; tick++) scheduler.RunStep();
        Assert.Equal(1, calls);
        Assert.Equal(1, scheduler.CompletedEvents);
        Assert.Equal(0, scheduler.ReadyCount);
    }

    [Fact]
    public void SchedulerRefusesUndeclaredAndUninitializedEvents()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()));
        var library = Library(script);
        var gate = Gate();
        var host = new ScriptHost(library, gate);
        var instance = host.Attach(script.Name);
        var scheduler = new ScriptScheduler(host);
        scheduler.Enqueue(instance, "OnInit");
        Assert.Equal(1, scheduler.RunStep().Refused);
        Assert.True(host.Initialize(instance).Completed);
        scheduler.Enqueue(instance, "Missing");
        Assert.Equal(1, scheduler.RunStep().Refused);
        host.Enqueue(instance, "Missing");
        host.RunStep(100);
        Assert.Equal(0, host.ReadyCount);
    }

    [Fact]
    public void PropertyWritesAreRefusedAndCounted()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()));
        var host = new ScriptHost(Library(script), Gate());
        var instance = host.Attach(script.Name);
        Assert.ThrowsAny<Exception>(() => ((INativeTarget)host).SetProperty(PapyrusValue.FromObject(instance), "Value", PapyrusValue.FromInt(4)));
        Assert.Equal(1, host.NativeTargetRefusals);
    }

    [Fact]
    public void MissingQuestScriptsBlockAllAttachments()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()));
        var gate = Gate();
        var host = new ScriptHost(Library(script), gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[]
        {
            new QuestStartupInput("A", true, new[] { script.Name }, 0, 0),
            new QuestStartupInput("B", true, new[] { "Missing" }, 0, 0),
        }, host);
        Assert.False(result.Applied);
        Assert.Contains("Missing", string.Join(" ", result.Reasons), StringComparison.Ordinal);
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.StartedQuests);
    }

    [Fact]
    public void ReattachPreservesTwoReferencesWithTheSameScript()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()));
        var session = new CellScriptSession(new ScriptHost(Library(script), Gate()));
        var first = session.Bind("Cell", "First", script.Name);
        var second = session.Bind("Cell", "Second", script.Name);
        session.DetachCell("Cell");
        session.ReattachCell("Cell");
        Assert.Same(first, session.InstanceFor("First"));
        Assert.Same(second, session.InstanceFor("Second"));
    }

    [Fact]
    public void DepthExhaustionReturnsItsDedicatedStopReason()
    {
        var script = Script(Function("Run", PapyrusInstruction.Return()));
        var result = new PapyrusVm(Library(script), Gate(), frameDepthLimit: 0)
            .Call(script, script.FindFunction("Run"), PapyrusValue.None, Array.Empty<PapyrusValue>());
        Assert.Equal(VmStopReason.FrameDepthExceeded, result.Reason);
    }

    [Fact]
    public void NestedFramesAndReturnSlotsSurviveBudgetSlices()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()),
            Function("Run", PapyrusInstruction.CallStatic("Review", "Nested", PapyrusOperand.Local(0),
                new[] { PapyrusOperand.Constant(PapyrusValue.FromInt(7)) }),
                PapyrusInstruction.CallStatic("Review", "Record", PapyrusOperand.Discard, new[] { PapyrusOperand.Local(0) }),
                PapyrusInstruction.Return()),
            new PapyrusFunction("Nested", new[]
            {
                PapyrusInstruction.CallStatic("Review", "Count", PapyrusOperand.Discard, Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.Nop(), PapyrusInstruction.Return(PapyrusOperand.Local(0)),
            }, ParameterCount: 1));
        var host = new ScriptHost(Library(script), Gate());
        var calls = 0;
        var values = new List<long>();
        host.RegisterNative(new NativeId("Review", "Count"), _ => { calls++; return PapyrusValue.None; });
        host.RegisterNative(new NativeId("Review", "Record"), args => { values.Add(args[0].Integer); return PapyrusValue.None; });
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        host.Enqueue(instance, "Run");
        for (var tick = 0; tick < 8 && host.ReadyCount > 0; tick++) Assert.InRange(host.RunStep(2), 0, 2);
        Assert.Equal(1, calls);
        Assert.Equal(7, Assert.Single(values));
        Assert.Equal(0, host.ReadyCount);
    }

    [Fact]
    public void ParkLimitsBelongToEachEventInstance()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()), Function("Spin", PapyrusInstruction.Jump(0)));
        var host = new ScriptHost(Library(script), Gate());
        var first = host.Attach(script.Name);
        var second = host.Attach(script.Name);
        Assert.True(host.Initialize(first).Completed);
        Assert.True(host.Initialize(second).Completed);
        var scheduler = new ScriptScheduler(host, budget: 4, maxParksPerEvent: 2);
        scheduler.Enqueue(first, "Spin");
        scheduler.Enqueue(second, "Spin");
        Assert.Equal(new SchedulerStepResult(4, 0, 2, 0, 0, 0), scheduler.RunStep());
        Assert.Equal(new SchedulerStepResult(4, 0, 0, 0, 0, 2), scheduler.RunStep());
        Assert.Equal(0, scheduler.ReadyCount);
    }

    [Fact]
    public void QueuedOverridesUseTheHostEntryPathOnce()
    {
        var script = Script(Function("OnInit", PapyrusInstruction.Return()), Function("Run", PapyrusInstruction.Unsupported("must not run")));
        var gate = Gate();
        var host = new ScriptHost(Library(script), gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        var calls = 0;
        host.Overrides.Register(script.Name, "Run", 0, _ => { calls++; return OverrideResult.Handled(PapyrusValue.None); });
        host.Enqueue(instance, "Run");
        Assert.Equal(0, host.RunStep(1));
        Assert.Equal(1, calls);
        Assert.Equal(0, host.ReadyCount);
    }
}
