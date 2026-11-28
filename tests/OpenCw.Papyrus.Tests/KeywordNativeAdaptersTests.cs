using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class KeywordNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec KeywordSpec = TestHosts.Stub("Keyword");
    private static readonly ScriptSpec FormSpec = TestHosts.Stub("Form", null, "Bool Function HasKeyword(Keyword akKeyword) native");
    private static readonly ScriptSpec RefSpec = TestHosts.Stub("ObjectReference", "Form",
        "Function AddKeyword(Keyword akKeyword) native", "Function RemoveKeyword(Keyword akKeyword) native");

    private static readonly ScriptSpec UserSpec = new("KeywordUser",
        "Scriptname KeywordUser\n" +
        "ObjectReference Property Target Auto\n" +
        "Keyword Property Kw Auto\n" +
        "Int Property Before Auto\n" +
        "Int Property Added Auto\n" +
        "Int Property Removed Auto\n" +
        "Event OnInit()\n" +
        "  If Target.HasKeyword(Kw)\n    Before = 1\n  EndIf\n" +
        "  Target.RemoveKeyword(Kw)\n" +
        "  If Target.HasKeyword(Kw)\n    Removed = 0\n  Else\n    Removed = 1\n  EndIf\n" +
        "  Target.AddKeyword(Kw)\n" +
        "  If Target.HasKeyword(Kw)\n    Added = 1\n  EndIf\n" +
        "EndEvent\n");

    [Fact]
    public void AnOwnedReferenceReadsTheKeywordsOfItsPlacedReferenceKey()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), KeywordSpec, FormSpec, RefSpec, UserSpec));
        var overlay = new KeywordOverlay();
        Assert.Equal(4, new KeywordNativeAdapters(host, TestHosts.DefaultGate(), overlay,
            form => form == "R:T.esm" ? new[] { "K:T.esm" } : null).Bind().Bound);
        var instance = host.Attach("KeywordUser", new Dictionary<string, PapyrusValue>
        {
            ["Target"] = PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference("B:T.esm", "R:T.esm"))),
            ["Kw"] = PapyrusValue.FromObject(new EngineObjectReference("K:T.esm")),
        });

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Before", out var before));
        Assert.Equal(1, before);
        Assert.Null(overlay.Changed("B:T.esm", "K:T.esm"));
    }

    [Fact]
    public void RuntimeChangesWinOverTheRecordKeywordsAndStayPerForm()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), KeywordSpec, FormSpec, RefSpec, UserSpec));
        var overlay = new KeywordOverlay();
        Assert.Equal(4, new KeywordNativeAdapters(host, TestHosts.DefaultGate(), overlay,
            form => form == "A:T.esm" ? new[] { "K:T.esm" } : null).Bind().Bound);
        var instance = host.Attach("KeywordUser", new Dictionary<string, PapyrusValue>
        {
            ["Target"] = PapyrusValue.FromObject(new EngineObjectReference("A:T.esm")),
            ["Kw"] = PapyrusValue.FromObject(new EngineObjectReference("K:T.esm")),
        });

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        foreach (var (name, expected) in new[] { ("Before", 1), ("Removed", 1), ("Added", 1) })
        {
            Assert.True(host.TryReadScalarProperty(instance, name, out var value));
            Assert.Equal(expected, value);
        }
        Assert.Null(overlay.Changed("B:T.esm", "K:T.esm"));
    }
}
