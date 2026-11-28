using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class PlaceAtMeNativeAdaptersTests : HostFixture
{
    private const string Lever = "000A00";
    private const string Hazard = "000B00:Base.esm";
    private const string Chair = "000B01:Base.esm";

    private sealed class Placer : IFormPlacer
    {
        public List<string> Placed { get; } = new();

        public bool Supports(string baseFormKey) => baseFormKey == Hazard;

        public string? Place(string receiverRefKey, string baseFormKey)
        {
            Placed.Add($"{receiverRefKey} {baseFormKey}");
            return $"t{Placed.Count}";
        }
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string form, string call, List<string> made)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Form"),
            TestHosts.Stub("ObjectReference", null,
                "ObjectReference Function PlaceAtMe(Form akFormToPlace, int aiCount = 1, bool abForcePersist = false, bool abInitiallyDisabled = false, bool abDeleteWhenAble = true) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Self() global native",
                "Form Function Thing() global native",
                "Function Made(ObjectReference value) global native"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Self()\n  Form thing = Probe.Thing()\n  Probe.Made({call})\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Self"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForPlacedReference("000800:Base.esm", Lever))));
        host.RegisterNative(new NativeId("Probe", "Thing"), _ => PapyrusValue.FromObject(new EngineObjectReference(form)));
        host.RegisterNative(new NativeId("Probe", "Made"), arguments =>
        {
            made.Add(arguments[0].Reference is EngineObjectReference reference ? reference.Identity : "none");
            return PapyrusValue.None;
        });
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private static EvidenceGate Gate() => TestHosts.Gate((PlaceAtMeNativeAdapters.SystemKey, "APPROXIMATED"));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlaceAtMeOfAHazardPlacesEachCopyAtTheReceiverAndReturnsTheLastAsADynamicReference(HostKind kind)
    {
        var made = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Hazard, "target.PlaceAtMe(thing, 3)", made);
        var placer = new Placer();
        var report = new PlaceAtMeNativeAdapters(host, gate, placer).Bind();

        var result = Run(host);

        Assert.Equal(1, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { $"{Lever} {Hazard}", $"{Lever} {Hazard}", $"{Lever} {Hazard}" }, placer.Placed.ToArray());
        Assert.Equal(new[] { PapyrusOwnerId.ForDynamicReference(Hazard, "t3").Key }, made.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlaceAtMeOfAnyOtherFormIsRefusedAsTheUnboundNativeWasAndPlacesNothing(HostKind kind)
    {
        var made = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Chair, "target.PlaceAtMe(thing)", made);
        var placer = new Placer();
        var adapters = new PlaceAtMeNativeAdapters(host, gate, placer);
        adapters.Bind();

        var result = Run(host);

        Assert.False(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(placer.Placed);
        Assert.Empty(made);
        Assert.Equal(1, adapters.Refused);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ACountOfZeroPlacesNothingAndAnswersNone(HostKind kind)
    {
        var made = new List<string>();
        var gate = Gate();
        var host = Host(kind, gate, Hazard, "target.PlaceAtMe(thing, 0)", made);
        var placer = new Placer();
        new PlaceAtMeNativeAdapters(host, gate, placer).Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Empty(placer.Placed);
        Assert.Equal(new[] { "none" }, made.ToArray());
    }
}
