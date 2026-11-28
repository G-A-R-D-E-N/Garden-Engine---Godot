using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class AliasNativeAdaptersTests : HostFixture
{
    private const string Quest = "027411:Test.esm";
    private static readonly PapyrusOwnerId Placed = PapyrusOwnerId.ForPlacedReference("000800:Test.esm", "A1:Test.esm");

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void GetReferenceResolvesTheFillAndForceRefToAndClearOverrideIt(HostKind kind)
    {
        var gate = TestHosts.Gate((AliasNativeAdapters.SystemKey, "ENGINE_POLICY"));
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ReferenceAlias", null, "ObjectReference Function GetReference() native", "Function ForceRefTo(ObjectReference akRef) native", "Function Clear() native"),
            TestHosts.Stub("ObjectReference", null),
            TestHosts.Stub("Probe", null, "ReferenceAlias Function Alias() global native", "ObjectReference Function Other() global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nObjectReference Function Get()\n  Return Probe.Alias().GetReference()\nEndFunction\n" +
                "Function Force()\n  Probe.Alias().ForceRefTo(Probe.Other())\nEndFunction\nFunction Wipe()\n  Probe.Alias().Clear()\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Alias"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForQuestAlias(Quest, 1))));
        host.RegisterNative(new NativeId("Probe", "Other"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference("000801:Test.esm", "B2:Test.esm"))));
        var resolver = new QuestAliasResolver(quest => new[] { new AliasFillDef(quest, 1, false, "000800:Test.esm") },
            fill => fill.Target == "000800:Test.esm" ? "A1:Test.esm" : null);
        var report = new AliasNativeAdapters(host, gate, resolver, refKey => refKey == "A1:Test.esm" ? new EngineObjectReference(Placed) : null).Bind();
        Assert.Equal(3, report.Bound);
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var first = host.RunEvent(instance, "Get");
        Assert.True(first.Completed, TestHosts.Describe(host, first));
        Assert.Equal(Placed.Key, ((EngineObjectReference)first.Value.Reference!).Identity);

        Assert.True(host.RunEvent(instance, "Force").Completed);
        Assert.Equal("B2:Test.esm", resolver.Resolve(Quest, 1));
        Assert.True(host.RunEvent(instance, "Wipe").Completed);
        var cleared = host.RunEvent(instance, "Get");
        Assert.True(cleared.Completed);
        Assert.Null(cleared.Value.Reference);
    }
}
