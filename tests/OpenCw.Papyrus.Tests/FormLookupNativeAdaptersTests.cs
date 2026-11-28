using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class FormLookupNativeAdaptersTests : HostFixture
{
    private static readonly ScriptSpec FormSpec = TestHosts.Stub("Form");
    private static readonly ScriptSpec GameSpec = TestHosts.Stub("Game", null, "Form Function GetFormFromFile(Int aiFormID, String asFilename) global native");
    private static readonly ScriptSpec GlobalSpec = TestHosts.Stub("GlobalVariable", "Form", "Float Function GetValue() native", "Function SetValue(Float afNewValue) native");

    private static readonly ScriptSpec UserSpec = new("FormUser",
        "Scriptname FormUser\n" +
        "Int Property Found = -1 Auto\n" +
        "GlobalVariable Property Gv Auto\n" +
        "Float Property Value Auto\n" +
        "Event OnInit()\n" +
        "  Form f = Game.GetFormFromFile(0x0002058E, \"Fallout4.esm\")\n" +
        "  If f != None\n" +
        "    Found = 1\n" +
        "  Else\n" +
        "    Found = 0\n" +
        "  EndIf\n" +
        "  If Gv != None\n" +
        "    Value = Gv.GetValue()\n" +
        "  EndIf\n" +
        "EndEvent\n");

    private static readonly ScriptSpec CastSpec = new("CastUser",
        "Scriptname CastUser\n" +
        "Float Property Value Auto\n" +
        "Event OnInit()\n" +
        "  GlobalVariable g = Game.GetFormFromFile(0x00000123, \"Fallout4.esm\") as GlobalVariable\n" +
        "  Value = g.GetValue()\n" +
        "EndEvent\n");

    private static readonly ScriptSpec StructUserSpec = new("StructUser",
        "Scriptname StructUser\n" +
        "Struct Row\n" +
        "  GlobalVariable Gv\n" +
        "EndStruct\n" +
        "Row[] Property Rows Auto\n" +
        "Float Property Value Auto\n" +
        "Event OnInit()\n" +
        "  Value = Rows[0].Gv.GetValue()\n" +
        "EndEvent\n");

    [Fact]
    public void AnObjectMemberOfAStructInAnArrayIsTypedSoItsNativesAreFound()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, GameSpec, GlobalSpec, StructUserSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "GLOB"), _ => 3.0).Bind();
        var row = PapyrusValue.FromStruct(new Dictionary<string, PapyrusValue> { ["Gv"] = PapyrusValue.FromObject(new EngineObjectReference("000100:Fallout4.esm")) });
        var instance = host.Attach("StructUser", new Dictionary<string, PapyrusValue> { ["Rows"] = PapyrusValue.FromArray(new[] { row }) });

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Value", out var value));
        Assert.Equal(3f, value);
    }

    private static readonly ScriptSpec SetterSpec = new("SetterUser",
        "Scriptname SetterUser\n" +
        "GlobalVariable Property Gv Auto\n" +
        "Float Property Value Auto\n" +
        "Event OnInit()\n" +
        "  Gv.SetValue(6.5)\n" +
        "  Value = Gv.GetValue()\n" +
        "EndEvent\n");

    [Fact]
    public void GlobalVariableSetValueIsKeptAndReadBackByGetValue()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, GameSpec, GlobalSpec, SetterSpec));
        var live = new Dictionary<string, double>();
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "GLOB"), key => live.TryGetValue(key, out var held) ? held : 1.0, (key, value) => live[key] = value).Bind();
        var instance = host.Attach("SetterUser", new Dictionary<string, PapyrusValue> { ["Gv"] = PapyrusValue.FromObject(new EngineObjectReference("000100:Fallout4.esm")) });

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Value", out var value));
        Assert.Equal(6.5f, value);
        Assert.Equal(6.5, live["000100:Fallout4.esm"]);
    }

    private static readonly ScriptSpec QuestSpec = TestHosts.Stub("Quest", "Form");

    private static readonly ScriptSpec FollowersLikeSpec = new("FollowersLike",
        "Scriptname FollowersLike extends Quest\n" +
        "Int Property Marker = 7 Auto\n");

    private static readonly ScriptSpec ScriptCastSpec = new("ScriptCastUser",
        "Scriptname ScriptCastUser\n" +
        "Int Property Got Auto\n" +
        "Event OnInit()\n" +
        "  FollowersLike q = Game.GetFormFromFile(0x0289E4, \"Fallout4.esm\") as FollowersLike\n" +
        "  Got = q.Marker\n" +
        "EndEvent\n");

    [Fact]
    public void AFormCastToTheScriptAttachedToItReadsItsProperties()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, FollowersLikeSpec, GameSpec, ScriptCastSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("FollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("ScriptCastUser");

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Got", out var got));
        Assert.Equal(7, got);
    }

    private static readonly ScriptSpec OtherLikeSpec = new("OtherLike",
        "Scriptname OtherLike extends Quest\n");

    [Fact]
    public void AFormWithSeveralAttachedScriptsCastsToAnyOfThem()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, OtherLikeSpec, FollowersLikeSpec, GameSpec, ScriptCastSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("OtherLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        host.Attach("FollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("ScriptCastUser");

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Got", out var got));
        Assert.Equal(7, got);
    }

    private static readonly ScriptSpec CallingFollowersSpec = new("CallingFollowersLike",
        "Scriptname CallingFollowersLike extends Quest\n" +
        "Int Property Marker = 7 Auto\n" +
        "Int Function Twice(Int n)\n" +
        "  Return n * 2\n" +
        "EndFunction\n");

    private static readonly ScriptSpec CallUserSpec = new("CallUser",
        "Scriptname CallUser\n" +
        "Int Property Got Auto\n" +
        "Event OnInit()\n" +
        "  CallingFollowersLike q = Game.GetFormFromFile(0x0289E4, \"Fallout4.esm\") as CallingFollowersLike\n" +
        "  Got = q.Twice(4)\n" +
        "EndEvent\n");

    [Fact]
    public void AScriptFunctionCalledOnAFormWithOneAttachedScriptRuns()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, CallingFollowersSpec, GameSpec, CallUserSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("CallingFollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("CallUser");

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Got", out var got));
        Assert.Equal(8, got);
    }

    [Fact]
    public void AScriptFunctionCalledOnAFormWithSeveralAttachedScriptsRunsTheCastScript()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, OtherLikeSpec, CallingFollowersSpec, GameSpec, CallUserSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("OtherLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        host.Attach("CallingFollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("CallUser");

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Got", out var got));
        Assert.Equal(8, got);
    }

    [Fact]
    public void AFormCastToItsScriptTypeKeepsTheEngineIdentity()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, GameSpec, GlobalSpec, CastSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "GLOB"), _ => 4.0).Bind();
        var instance = host.Attach("CastUser");

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Value", out var value));
        Assert.Equal(4f, value);
    }

    private static readonly ScriptSpec QuestTwiceSpec = TestHosts.Stub("Quest", "Form", "Int Function Twice(Int n)\n  Return n * 2\nEndFunction");

    private static readonly ScriptSpec QuestLikeSpec = new("QuestLike",
        "Scriptname QuestLike extends Quest\n" +
        "Int Function Twice(Int n)\n" +
        "  Return n * 3\n" +
        "EndFunction\n");

    private static readonly ScriptSpec QuestCastSpec = new("QuestCastUser",
        "Scriptname QuestCastUser\n" +
        "Int Property Got Auto\n" +
        "Event OnInit()\n" +
        "  Quest q = Game.GetFormFromFile(0x0289E4, \"Fallout4.esm\") as Quest\n" +
        "  Got = q.Twice(4)\n" +
        "EndEvent\n");

    [Fact]
    public void ACastToANativeTypeDoesNotSwapForAScriptThatExtendsIt()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestTwiceSpec, QuestLikeSpec, OtherLikeSpec, GameSpec, QuestCastSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("QuestLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        host.Attach("OtherLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("QuestCastUser");

        var result = host.Initialize(instance);
        Assert.False(result.Completed);
        Assert.Contains("the VM stopped with status FunctionNotFound (Twice) after the native Game.GetFormFromFile answered 0289E4:Fallout4.esm (untyped handle: the scripts QuestLike, OtherLike are attached", result.Message);
    }

    [Fact]
    public void ACastToADiscardedScriptDoesNotResolveIt()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, FollowersLikeSpec, GameSpec, ScriptCastSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Discard(host.Attach("FollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm")));
        var instance = host.Attach("ScriptCastUser");

        var result = host.Initialize(instance);
        Assert.False(result.Completed);
        Assert.Contains("NullObject", result.Message);
    }

    private static readonly ScriptSpec UntypedCallSpec = new("UntypedCallUser",
        "Scriptname UntypedCallUser\n" +
        "Int Property Got Auto\n" +
        "Event OnInit()\n" +
        "  CallingFollowersLike q = Game.GetFormFromFile(0x0289E4, \"Fallout4.esm\") as CallingFollowersLike\n" +
        "  Got = q.Twice(4)\n" +
        "EndEvent\n");

    [Fact]
    public void AScriptFunctionCalledOnAFormWithSeveralAttachedScriptsNamesTheUntypedHandle()
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, QuestSpec, OtherLikeSpec, FollowersLikeSpec, CallingFollowersSpec, GameSpec, UntypedCallSpec));
        new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), key => (key, "QUST"), _ => null).Bind();
        host.Attach("OtherLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        host.Attach("FollowersLike", owner: PapyrusOwnerId.ForForm("0289E4:Fallout4.esm"));
        var instance = host.Attach("UntypedCallUser");

        var result = host.Initialize(instance);
        Assert.False(result.Completed);
        Assert.Contains("FunctionNotFound", result.Message);
        Assert.Contains("answered 0289E4:Fallout4.esm (untyped handle: the scripts OtherLike, FollowersLike are attached", result.Message);
    }

    private RadicleScriptHost Host(Func<string, (string Key, string? Signature)?> resolve, Func<string, double?> global)
    {
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, TestHosts.DefaultGate(), FormSpec, GameSpec, GlobalSpec, UserSpec));
        Assert.Equal(2, new FormLookupNativeAdapters(host, TestHosts.DefaultGate(), resolve, global).Bind().Bound);
        return host;
    }

    private static ScriptInstance User(RadicleScriptHost host, bool withGlobal = false) => host.Attach("FormUser",
        withGlobal ? new Dictionary<string, PapyrusValue> { ["Gv"] = PapyrusValue.FromObject(new EngineObjectReference("000100:Fallout4.esm")) } : null);

    [Fact]
    public void AFormTheRecordsHoldComesBackAsAReference()
    {
        string? asked = null;
        var host = Host(key => { asked = key; return (key, "GLOB"); }, _ => 2.5);
        var instance = User(host, withGlobal: true);

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.Equal("02058E:Fallout4.esm", asked);
        Assert.True(host.TryReadScalarProperty(instance, "Found", out var found));
        Assert.Equal(1, found);
        Assert.True(host.TryReadScalarProperty(instance, "Value", out var value));
        Assert.Equal(2.5f, value);
    }

    [Fact]
    public void AFormTheRecordsLackComesBackAsNone()
    {
        var host = Host(_ => null, _ => null);
        var instance = User(host);

        Assert.True(host.Initialize(instance).Completed, instance.Failure);
        Assert.True(host.TryReadScalarProperty(instance, "Found", out var found));
        Assert.Equal(0, found);
    }

    [Fact]
    public void AGlobalThatIsNotInTheRecordsStopsInsteadOfAnsweringZero()
    {
        var host = Host(key => (key, "GLOB"), _ => null);
        var instance = User(host, withGlobal: true);

        Assert.False(host.Initialize(instance).Completed);
        Assert.Contains("000100:Fallout4.esm", instance.Failure);
    }
}
