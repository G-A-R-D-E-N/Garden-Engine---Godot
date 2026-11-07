using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ParentNativeDispatchTests
{
    private static readonly NativeId Answer = new("Base", "Answer");

    private static EvidenceGate Gate(string? denied = null)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.NativeTarget, EvidenceKeys.OnInitBarrier, EvidenceKeys.SchedulerBudget,
            EvidenceKeys.Dispatch, NativeWait.Key };
        var rows = keys.Where(key => key != denied)
            .Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
    }

    private static ScriptHost Host(string? denied = null)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Base", new[]
        {
            new PapyrusFunction("Answer", Array.Empty<PapyrusInstruction>(), ParameterCount: 1, IsNative: true),
        }, Array.Empty<string>()));
        library.Add(new PapyrusScript("Child", new[]
        {
            new PapyrusFunction("Run", new[]
            {
                PapyrusInstruction.CallParent("Answer", PapyrusOperand.Local(0),
                    new[] { PapyrusOperand.Constant(PapyrusValue.FromInt(21)) }),
                PapyrusInstruction.CallStatic("Observer", "Record", PapyrusOperand.Discard,
                    new[] { PapyrusOperand.Local(0) }),
                PapyrusInstruction.Return(PapyrusOperand.Local(0)),
            }),
            new PapyrusFunction("Answer", new[]
            {
                PapyrusInstruction.Return(PapyrusOperand.Constant(PapyrusValue.FromInt(-1))),
            }, ParameterCount: 1),
        }, Array.Empty<string>(), Parent: "Base"));
        var host = new ScriptHost(library, Gate(denied));
        host.RegisterNative(new NativeId("Observer", "Record"), _ => PapyrusValue.None);
        return host;
    }

    private static ScriptInstance Attach(ScriptHost host)
    {
        var instance = host.Attach("Child");
        Assert.True(host.Initialize(instance).Completed);
        return instance;
    }

    [Fact]
    public void ParentNativeReceivesChildSelfArgumentsAndReturnsToTheCaller()
    {
        var host = Host();
        var instance = Attach(host);
        var calls = 0;
        host.RegisterNative(Answer, (self, arguments) =>
        {
            calls++;
            Assert.Same(instance, self.Reference);
            Assert.Equal(PapyrusValue.FromInt(21), Assert.Single(arguments));
            return PapyrusValue.FromInt(42);
        }, NativeTargetKind.ScriptInstance);

        var result = host.RunEvent(instance, "Run");

        Assert.True(result.Completed, result.Message);
        Assert.Equal(1, calls);
        Assert.Equal(PapyrusValue.FromInt(42), result.Value);
    }

    [Fact]
    public void AnUnboundParentNativeRefusesInsteadOfRunningAnEmptyBody()
    {
        var host = Host();
        var result = host.RunEvent(Attach(host), "Run");
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("no native named Base.Answer is registered", result.Message);
        Assert.Equal(1, host.NativeTargetRefusals);
    }

    [Theory]
    [InlineData(EvidenceKeys.Dispatch)]
    [InlineData(EvidenceKeys.NativeTarget)]
    public void ParentNativeDoesNotBypassDispatchOrNativeGates(string denied)
    {
        var host = Host(denied);
        var calls = 0;
        host.RegisterNative(Answer, _ => { calls++; return PapyrusValue.None; }, NativeTargetKind.ScriptInstance);

        var result = host.RunEvent(Attach(host), "Run");

        Assert.False(result.Completed);
        Assert.Contains(denied, result.Key ?? result.Message);
        Assert.DoesNotContain("Record", result.Message ?? "");
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AParentNativeWaitRetainsItsAnswerAcrossDetach(bool completeBeforeDetach)
    {
        var host = Host();
        var calls = 0;
        var observed = new List<PapyrusValue>();
        host.RegisterNative(Answer, _ => { calls++; throw new NativeWait(1); }, NativeTargetKind.ScriptInstance);
        host.RegisterNative(new NativeId("Observer", "Record"), args =>
        {
            observed.Add(Assert.Single(args));
            return PapyrusValue.None;
        });
        var session = new CellScriptSession(host);
        var instance = session.Bind("cell", "reference", "Child");
        Assert.True(host.Initialize(instance).Completed);

        Assert.Equal(VmStopReason.Waiting, host.RunEvent(instance, "Run").Reason);
        Assert.Equal(1, host.WaitingCount);
        Assert.Empty(observed);
        if (completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        session.DetachCell("cell");
        if (!completeBeforeDetach) Assert.True(host.CompleteLatent(1, PapyrusValue.FromInt(42)));
        Assert.Equal(0, session.Tick(32));
        Assert.Empty(observed);
        session.ReattachCell("cell");
        session.Tick(32);

        Assert.Equal(PapyrusValue.FromInt(42), Assert.Single(observed));
        Assert.Equal(1, calls);
        Assert.Equal(0, host.WaitingCount);
        Assert.False(host.CompleteLatent(1, PapyrusValue.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectNativeDeclarationCannotExecuteAsAnEmptyBytecodeFrame(bool onInit)
    {
        var library = new ScriptLibrary();
        var name = onInit ? "OnInit" : "Run";
        library.Add(new PapyrusScript("Native", new[]
        {
            new PapyrusFunction(name, Array.Empty<PapyrusInstruction>(), IsNative: true),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, Gate());
        var instance = host.Attach("Native");
        var initialized = host.Initialize(instance);
        var result = onInit ? initialized : host.RunEvent(instance, name);

        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains("native declaration", result.Message);
        Assert.Equal(onInit ? ScriptLifecycle.Failed : ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(0, result.Instructions);
    }
}
