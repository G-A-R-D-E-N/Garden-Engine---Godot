using OpenCommonwealth.Services.Papyrus.Census;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class NativeCallSiteCollectorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Combat")]
    [InlineData(null)]
    public void EncodedNativeDeclarationsAreCollectedFromEveryCallerState(string? state)
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            PapyrusInstruction.CallStatic("utility", "WAIT", PapyrusOperand.Discard, []),
            PapyrusInstruction.CallStatic("Utility", "Ordinary", PapyrusOperand.Discard, []),
            PapyrusInstruction.Nop(),
        }, State: state));
        var library = Library(caller, Script("Utility", new PapyrusFunction("Wait", [], IsNative: true),
            new PapyrusFunction("Ordinary", [])));
        var result = NativeCallSiteCensus.Collect(caller, library);
        var census = result.Script;
        var site = Assert.Single(census.Sites);
        Assert.Equal("utility", site.Target);
        Assert.Equal("WAIT", site.CalledFunction);
        Assert.Equal(0, site.Instruction);
        Assert.Equal(1, result.ScriptCalls);
        Assert.Empty(result.Unresolved);
        var report = NativeCallSiteCensus.Run([census], library);
        Assert.Equal(1, report.CallSites);
        Assert.Equal(1, Assert.Single(report.Natives).Static);
        Assert.Equal(0, Assert.Single(report.Natives).Method);
    }

    [Fact]
    public void MethodDeclarationsResolveThroughTheDeclaredReceiverParentChain()
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.Local(0), "AddItem", PapyrusOperand.Discard, [], "ACTOR"),
        }));
        var library = Library(caller, Script("Actor", parent: "ObjectReference"),
            Script("ObjectReference", new PapyrusFunction("AddItem", [], IsNative: true)));
        var result = NativeCallSiteCensus.Collect(caller, library);
        var site = Assert.Single((result.Script).Sites);
        Assert.Equal("ACTOR", site.Target);
        Assert.Equal("AddItem", site.CalledFunction);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void AParentScriptBodyTakesPrecedenceOverANativeDeclarationDuringMethodDispatch()
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Run", PapyrusOperand.Discard, [], "Caller"),
        }), new PapyrusFunction("Run", [], IsNative: true)) with { Parent = "Parent" };
        var result = NativeCallSiteCensus.Collect(caller, Library(caller, Script("Parent", new PapyrusFunction("Run", []))));
        Assert.Empty(result.Script.Sites);
        Assert.Equal(1, result.ScriptCalls);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ParentCallsUseTheEnclosingScriptParent()
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            PapyrusInstruction.CallParent("Run", PapyrusOperand.Discard, []),
        }), "Parent");
        var result = NativeCallSiteCensus.Collect(caller, Library(caller, Script("Parent", new PapyrusFunction("Run", [], IsNative: true))));
        Assert.Equal("Parent", Assert.Single((result.Script).Sites).Target);
        Assert.Equal("Run", Assert.Single(result.Script.Sites).CalledFunction);
        Assert.Empty(result.Unresolved);
    }

    [Theory]
    [InlineData(null, "Run")]
    [InlineData("Missing", "Run")]
    [InlineData("Caller", "Missing")]
    [InlineData("Caller", null)]
    public void MissingDeclarationInputsStayUnresolved(string? target, string? name)
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            new PapyrusInstruction(PapyrusOpcode.CallMethod) { Script = target, Name = name },
        }, State: "Combat"));
        var result = NativeCallSiteCensus.Collect(caller, Library(caller));
        Assert.Empty((result.Script).Sites);
        Assert.Equal(0, result.ScriptCalls);
        Assert.Contains("Caller[Combat].Work+0", Assert.Single(result.Unresolved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicDeclarationChainsStayUnresolved(bool hasNative)
    {
        var caller = Script("Caller", new PapyrusFunction("Work", new[]
        {
            PapyrusInstruction.CallMethod(PapyrusOperand.SelfRef, "Missing", PapyrusOperand.Discard, [], "Caller"),
        }), "Parent");
        var parent = Script("Parent", hasNative ? new PapyrusFunction("Missing", [], IsNative: true) : null, "Caller");
        var result = NativeCallSiteCensus.Collect(caller, Library(caller, parent));
        Assert.Single(result.Unresolved);
        Assert.Empty((result.Script).Sites);
    }

    private static PapyrusScript Script(string name, PapyrusFunction? function = null, string? parent = null) =>
        new(name, function is null ? [] : [function], [], parent);

    private static PapyrusScript Script(string name, PapyrusFunction first, PapyrusFunction second) =>
        new(name, [first, second], []);

    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts) library.Add(script);
        return library;
    }
}
