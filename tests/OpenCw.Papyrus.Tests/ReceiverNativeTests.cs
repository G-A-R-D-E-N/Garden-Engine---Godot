using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReceiverNativeTests : HostFixture
{
    private static readonly PapyrusOwnerId Actor = PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00");
    private static readonly PapyrusValue Item = PapyrusValue.FromObject(new EngineObjectReference("000900:Items.esp"));

    private static readonly ScriptSpec FormStub = new("Form", "Scriptname Form\n");

    private static readonly ScriptSpec SourceStub = new("ReceiverSource",
        "Scriptname ReceiverSource\n" +
        "Actor Function Target() global native\n" +
        "Form Function Thing() global native\n" +
        "ObjectReference Function Holder() global native\n" +
        "ObjectReference Function ObjectTarget() global native\n");

    private static ScriptSpec[] Stubs() => new[]
    {
        FormStub,
        new ScriptSpec("ObjectReference", "Scriptname ObjectReference\n"),
        new ScriptSpec("Actor", "Scriptname Actor extends ObjectReference\n" +
            "Int Function Echo(Form akItem) native\n" +
            "Form Function Pass(Form akItem) native\n"),
        SourceStub,
    };

    private static EvidenceGate Gate() => TestHosts.Gate();

    private static PapyrusValue Receiver() => PapyrusValue.FromObject(new EngineObjectReference(Actor));

    private static ScriptSpec Caller(string receiverType, string returnType, params string[] body) => new("Caller",
        "Scriptname Caller\n" +
        (returnType.Length == 0 ? string.Empty : returnType + " ") +
        "Function Run()\n" +
        $"  {receiverType} target = ReceiverSource.{(receiverType == "ObjectReference" ? "ObjectTarget" : "Target")}()\n" +
        string.Concat(body.Select(line => "  " + line + "\n")) +
        "EndFunction\n");

    private IScriptHost PsHost(HostKind kind, EvidenceGate gate, ScriptSpec caller, PapyrusValue receiver,
        PapyrusValue thing, PapyrusValue holder)
    {
        var host = Track(TestHosts.Create(kind, gate, Stubs().Append(caller).ToArray()));
        host.RegisterNative(new NativeId("ReceiverSource", "Target"), _ => receiver);
        host.RegisterNative(new NativeId("ReceiverSource", "ObjectTarget"), _ => receiver);
        host.RegisterNative(new NativeId("ReceiverSource", "Thing"), _ => thing);
        host.RegisterNative(new NativeId("ReceiverSource", "Holder"), _ => holder);
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AReceiverNativeGetsTheValidatedReceiverThroughBytecode(HostKind kind)
    {
        var receiver = PapyrusValue.FromObject(new EngineObjectReference("000800:Npcs.esp"));
        var host = PsHost(kind, Gate(), Caller("Actor", "Int",
            "Int got = target.Echo(ReceiverSource.Thing())", "Return got"), receiver, Item, PapyrusValue.None);
        PapyrusValue seen = default;
        host.RegisterNative(new NativeId("Actor", "Echo"), (target, arguments) =>
        {
            seen = target;
            Assert.Equal(Item, Assert.Single(arguments));
            return PapyrusValue.FromInt(17);
        }, NativeTargetKind.EngineObject);

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(receiver, seen);
        Assert.Equal(17, result.Value.Integer);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TryRegisterReceiverNativeDoesNotOverwriteABody(HostKind kind)
    {
        var receiver = PapyrusValue.FromObject(new EngineObjectReference("000800:Npcs.esp"));
        var host = PsHost(kind, Gate(), Caller("Actor", "Int",
            "Int got = target.Echo(ReceiverSource.Thing())", "Return got"), receiver, Item, PapyrusValue.None);
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> first = (_, _) => PapyrusValue.FromInt(1);
        Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> second = (_, _) => PapyrusValue.FromInt(2);
        Assert.True(host.TryRegisterNative(new NativeId("Actor", "Echo"), first, NativeTargetKind.EngineObject));
        Assert.False(host.TryRegisterNative(new NativeId("Actor", "Echo"), second, NativeTargetKind.EngineObject));
        Assert.Equal(1, Run(host).Value.Integer);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ArgumentOnlyRegistrationsKeepTheirArgumentsAndResult(HostKind kind)
    {
        var host = PsHost(kind, Gate(), Caller("Actor", "Form", "Return target.Pass(ReceiverSource.Thing())"),
            Receiver(), Item, PapyrusValue.None);
        host.RegisterNative(new NativeId("Actor", "Pass"), args => Assert.Single(args), NativeTargetKind.EngineObject);

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(Item, result.Value);
    }
}
