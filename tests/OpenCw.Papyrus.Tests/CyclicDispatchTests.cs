using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class CyclicDispatchTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void CyclicParentLookupStopsWithoutInvokingANative(int length, bool nativeDeclaration)
    {
        var library = new ScriptLibrary();
        for (var index = 0; index < length; index++)
        {
            var functions = new List<PapyrusFunction>();
            if (index == 0) functions.Add(Run());
            if (nativeDeclaration && index == length - 1) functions.Add(new PapyrusFunction("Missing", [], IsNative: true));
            library.Add(new PapyrusScript($"Node{index}", functions, [], $"Node{(index + 1) % length}"));
        }
        var host = new ScriptHost(library, Gate());
        var nativeCalls = 0;
        host.RegisterNative(new NativeId("Node0", "Missing"), _ =>
        {
            nativeCalls++;
            return PapyrusValue.None;
        }, NativeTargetKind.ScriptInstance, "Node0");
        var instance = host.Attach("Node0");
        Assert.True(host.Initialize(instance).Completed);
        VmResult? result = null;
        var execution = new Thread(() => result = host.RunEvent(instance, "Run")) { IsBackground = true };
        execution.Start();
        Assert.True(execution.Join(TimeSpan.FromSeconds(1)), "cyclic method lookup did not return");
        Assert.Equal(VmStopReason.ModelError, result!.Reason);
        Assert.Contains("cyclic", result.Message);
        Assert.Equal(1, result.Instructions);
        Assert.Equal(0, nativeCalls);
    }

    [Fact]
    public void AcyclicParentScriptDispatchStillRuns()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Node0", [Run()], [], "Parent"));
        library.Add(new PapyrusScript("Parent", [new PapyrusFunction("Missing",
            [PapyrusInstruction.Return(PapyrusOperand.Constant(PapyrusValue.FromInt(17)))])], []));
        var host = new ScriptHost(library, Gate());
        var instance = host.Attach("Node0");
        Assert.True(host.Initialize(instance).Completed);
        var result = host.RunEvent(instance, "Run");
        Assert.True(result.Completed, result.Message);
        Assert.Equal(17, result.Value.Integer);
    }

    [Fact]
    public void TheMethodTargetGateStopsBeforeACyclicLookup()
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Node0", [Run()], [], "Parent"));
        library.Add(new PapyrusScript("Parent", [], [], "Node0"));
        var host = new ScriptHost(library, Gate(nativeTargetAllowed: false));
        var instance = host.Attach("Node0");
        Assert.True(host.Initialize(instance).Completed);
        var result = host.RunEvent(instance, "Run");
        Assert.Equal(VmStopReason.GateStopped, result.Reason);
        Assert.Equal(EvidenceKeys.NativeTarget, result.Key);
        Assert.Equal(0, result.Instructions);
    }

    private static PapyrusFunction Run() => new("Run",
        [PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Missing", PapyrusOperand.Local(0), [], "Node0"),
         PapyrusInstruction.Return(PapyrusOperand.Local(0))]);

    private static EvidenceGate Gate(bool nativeTargetAllowed = true)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.OnInitBarrier,
            EvidenceKeys.NativeTarget, EvidenceKeys.Dispatch };
        var rows = keys.Select(key => new { key, status = key == EvidenceKeys.NativeTarget && !nativeTargetAllowed ? "UNPROVEN" : "ENGINE_POLICY" });
        return new EvidenceGate(EvidenceRegistry.Load(JsonSerializer.Serialize(new { schema = EvidenceRegistry.SchemaName, rows })));
    }
}
