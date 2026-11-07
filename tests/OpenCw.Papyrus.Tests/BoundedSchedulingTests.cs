using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class BoundedSchedulingTests
{
    private static readonly (string Key, string Status)[] PolicyRows =
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
        (EvidenceKeys.Waits, "APPROXIMATED"),
        (NativeSet.TraceKey, "ENGINE_POLICY"),
        (NativeSet.NotificationKey, "ENGINE_POLICY"),
        (NativeSet.MessageBoxKey, "ENGINE_POLICY"),
    };

    private static EvidenceRegistry Registry(params (string Key, string Status)[] rows)
    {
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Status}\"}}");
        return EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}");
    }

    private static EvidenceGate Gate() => new(Registry(PolicyRows));

    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts)
        {
            library.Add(script);
        }
        return library;
    }

    private static ScriptInstance Instance(PapyrusScript script)
    {
        var host = new ScriptHost(Library(script), Gate());
        var instance = host.Attach(script.Name);
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    private static PapyrusScript Spin() => new("Spin", new[]
    {
        new PapyrusFunction("Run", new[] { PapyrusInstruction.Jump(0) }),
    }, Array.Empty<string>());

    private static PapyrusScript Quick(string name) => new(name, new[]
    {
        new PapyrusFunction("Run", new[] { PapyrusInstruction.Return() }),
    }, Array.Empty<string>());

    [Fact]
    public void RunawayLoopCannotExceedTheBudget()
    {
        var library = Library(Spin());
        var scheduler = new ScriptScheduler(new ScriptHost(library, Gate()), budget: 16);
        scheduler.Enqueue(Instance(library.Get("Spin")), "Run");

        var step = scheduler.RunStep();

        Assert.True(step.Spent <= 16, $"spent {step.Spent}");
        Assert.Equal(1, step.Parked);
        Assert.Equal(1, scheduler.ReadyCount);
    }

    [Fact]
    public void OneRunawayDoesNotStarveTheOthers()
    {
        var library = Library(Spin(), Quick("First"), Quick("Second"));
        var scheduler = new ScriptScheduler(new ScriptHost(library, Gate()), budget: 32);
        scheduler.Enqueue(Instance(library.Get("Spin")), "Run");
        scheduler.Enqueue(Instance(library.Get("First")), "Run");
        scheduler.Enqueue(Instance(library.Get("Second")), "Run");

        var step = scheduler.RunStep();

        Assert.Equal(2, step.Completed);
        Assert.Equal(1, step.Parked);
        Assert.Equal(1, scheduler.ReadyCount);
        Assert.True(step.Spent <= 32);
    }

    [Fact]
    public void ExactBudgetIsRespected()
    {
        var script = new PapyrusScript("Three", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                PapyrusInstruction.Nop(),
                PapyrusInstruction.Nop(),
                PapyrusInstruction.Return(),
            }),
        }, Array.Empty<string>());
        var library = Library(script);

        foreach (var budget in new[] { 1, 2, 3, 4, 64 })
        {
            var scheduler = new ScriptScheduler(new ScriptHost(library, Gate()), budget: budget);
            scheduler.Enqueue(Instance(script), "Run");
            var step = scheduler.RunStep();
            Assert.True(step.Spent <= budget, $"budget {budget} spent {step.Spent}");
        }

        var exact = new ScriptScheduler(new ScriptHost(library, Gate()), budget: 3);
        exact.Enqueue(Instance(script), "Run");
        Assert.Equal(3, exact.RunStep().Spent);
    }

    [Fact]
    public void WaitIsCountedAsAnApproximation()
    {
        var script = new PapyrusScript("Waiter", new[]
        {
            new PapyrusFunction("Run", new[] { PapyrusInstruction.Wait() }),
        }, Array.Empty<string>());
        var library = Library(script);
        var gate = Gate();
        var scheduler = new ScriptScheduler(new ScriptHost(library, gate), budget: 16);
        scheduler.Enqueue(Instance(script), "Run");

        var step = scheduler.RunStep();

        Assert.Equal(1, step.Paused);
        Assert.Equal(0, step.Completed);
        Assert.Equal(1, scheduler.PausedEvents);
        Assert.Equal(1, gate.ApproximationCount);
        Assert.Equal(0, scheduler.ReadyCount);
    }

    [Fact]
    public void TheRecordedRegistryCountsWaitAsApproximated()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(
            Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));

        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(EvidenceKeys.Waits));
    }

    [Fact]
    public void BaseNativesAreBoundAndGated()
    {
        var script = new PapyrusScript("Caller", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                PapyrusInstruction.CallStatic("Debug", "Trace", PapyrusOperand.Local(1), new[] { PapyrusOperand.Local(0) }),
                PapyrusInstruction.Return(),
            }),
        }, Array.Empty<string>());
        var library = Library(script);
        var gate = Gate();
        var natives = new NativeSet(gate);
        natives.BindBaseSet();

        var host = new ScriptHost(library, gate);
        natives.Install(host);
        var result = host.Vm.Call(script, script.FindFunction("Run"), PapyrusValue.None,
            new[] { PapyrusValue.FromString("hello") });

        Assert.True(result.Completed);
        Assert.Equal(3, natives.Count);
        Assert.Equal(new[] { "hello" }, natives.Traces);
        Assert.Equal(new NativeId("Debug", "MessageBox"), natives.Descriptors[0].Native);
        Assert.All(natives.Descriptors, descriptor => Assert.False(string.IsNullOrWhiteSpace(descriptor.EvidenceKey)));
    }

    [Fact]
    public void EveryNativeMustDeclareItsEvidenceKey()
    {
        var natives = new NativeSet(Gate());

        Assert.Throws<ArgumentException>(() =>
            natives.Bind(NativeSet.DebugTrace, NativeTargetKind.Static, "",
                _ => PapyrusValue.None));
        Assert.Equal(0, natives.Count);
    }

    [Fact]
    public void AnUnboundNativeIsRefusedAndCounted()
    {
        var natives = new NativeSet(Gate());
        natives.BindBaseSet();

        Assert.Throws<InvalidOperationException>(() => natives.Invoke(new NativeId("Game", "GetPlayer"), Array.Empty<PapyrusValue>()));
        Assert.Equal(1, natives.Refusals);
    }

    [Fact]
    public void ARunawayIsDroppedAfterItsSliceLimit()
    {
        var library = Library(Spin());
        var scheduler = new ScriptScheduler(new ScriptHost(library, Gate()), budget: 8, maxParksPerEvent: 2);
        scheduler.Enqueue(Instance(library.Get("Spin")), "Run");

        for (var step = 0; step < 3; ++step)
        {
            Assert.True(scheduler.RunStep().Spent <= 8);
        }

        Assert.Equal(1, scheduler.DroppedEvents);
        Assert.Equal(0, scheduler.ReadyCount);
    }
}
