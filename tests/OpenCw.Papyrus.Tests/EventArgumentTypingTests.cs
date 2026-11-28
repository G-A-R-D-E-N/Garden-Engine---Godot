using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EventArgumentTypingTests : HostFixture
{
    private const string PlayerRef = "000014:Fallout4.esm";

    private static readonly PapyrusOwnerId Player = PapyrusOwnerId.ForPlacedReference("000007:Fallout4.esm", PlayerRef);

    private static readonly ScriptSpec CheckerSpec = new("Checker",
        "Scriptname Checker\n" +
        "Bool Property Same Auto\n" +
        "Event OnTriggerEnter(ObjectReference akActionRef)\n" +
        "  Same = akActionRef == Game.GetPlayer()\n" +
        "EndEvent\n");

    private RadicleScriptHost Host(bool ownedResult = false)
    {
        var gate = TestHosts.Gate((CoreNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("Form"), TestHosts.Stub("ObjectReference", "Form"), TestHosts.Stub("Actor", "ObjectReference"),
            TestHosts.Stub("Game", null, "Actor Function GetPlayer() global native"),
            TestHosts.Stub("PlayerOne", "Actor"), TestHosts.Stub("PlayerTwo", "Actor"), CheckerSpec));
        if (ownedResult) host.RegisterNative(new NativeId("Game", "GetPlayer"), _ => PapyrusValue.FromObject(new EngineObjectReference(Player) { ScriptType = "Actor" }));
        new CoreNativeAdapters(host, gate, () => 0.5, () => 3.0, () => PlayerRef, _ => { }).Bind();
        return host;
    }

    private static bool SameAsPlayer(RadicleScriptHost host, string? scriptType)
    {
        var checker = host.Attach("Checker");
        Assert.True(host.Initialize(checker).Completed, checker.Failure);
        var result = host.RunEvent(checker, "OnTriggerEnter", PapyrusValue.FromObject(new EngineObjectReference(Player) { ScriptType = scriptType }));
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.True(host.TryReadScalarProperty(checker, "Same", out var same));
        return (bool)same!;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AnEventArgumentEqualsTheNativePlayerWhateverNumberOfScriptsTheReferenceHolds(int scripts) => Compare(scripts, false);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnEventArgumentEqualsANativeResultThatCarriesTheOwnerWhateverNumberOfScripts(int scripts) => Compare(scripts, true);

    private void Compare(int scripts, bool ownedResult)
    {
        var host = Host(ownedResult);
        foreach (var name in new[] { "PlayerOne", "PlayerTwo" }.Take(scripts)) host.Attach(name, owner: Player);

        Assert.True(SameAsPlayer(host, "Actor"));
    }
}
