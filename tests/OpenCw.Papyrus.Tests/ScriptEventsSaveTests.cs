using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ScriptEventsSaveTests : HostFixture
{
    private static readonly PapyrusOwnerId OwnerA = PapyrusOwnerId.ForPlacedReference("00002A:Test.esm", "0000A1:Test.esm");
    private static readonly PapyrusOwnerId OwnerB = PapyrusOwnerId.ForPlacedReference("00002A:Test.esm", "0000B2:Test.esm");

    private sealed record Tables(ScriptTimers Timers, EventRegistrations Registrations, CustomEventRegistry Custom)
    {
        public Tables() : this(new ScriptTimers(), new EventRegistrations(), new CustomEventRegistry()) { }

        public IReadOnlyDictionary<string, byte[]> Save() => ScriptEventsSave.Sections(Timers, Registrations, Custom);

        public int Load(IReadOnlyDictionary<string, byte[]> sections, IScriptHost host) =>
            ScriptEventsSave.Restore(ScriptEventsSave.Read(sections)!, host.Instances, Timers, Registrations, Custom);
    }

    private IScriptHost NewHost() => Track(TestHosts.Create(HostKind.Radicle, TestHosts.Gate(("system.event_registrations", "APPROXIMATED")),
        TestHosts.Stub("ScriptObject", null),
        new ScriptSpec("Plain", "Scriptname Plain extends ScriptObject\n")));

    private static ScriptInstance Attach(IScriptHost host, PapyrusOwnerId? owner) => host.Attach("Plain", null, owner);

    [Fact]
    public void ATimerRegistrationAndReceiverSavedBeforeAreRestoredOntoTheLiveInstanceAndTheTimerFiresOnce()
    {
        var host = NewHost();
        var instance = Attach(host, OwnerA);
        var before = new Tables();
        Assert.True(before.Timers.Start(instance, 5, 7));
        before.Timers.Advance(2);
        before.Registrations.Add(instance, "HitEvent", "none");
        before.Custom.Register(instance, "SenderKey", "Boom");
        var save = before.Save();

        var after = new Tables();
        var dropped = after.Load(save, host);

        Assert.Equal(0, dropped);
        Assert.Equal(new[] { (instance, 7, 3.0) }, after.Timers.Scheduled);
        Assert.Equal(new[] { (instance, "HitEvent", "none") }, after.Registrations.All);
        Assert.Equal(new[] { (instance, "senderkey", "boom") }, after.Custom.Receivers);
        Assert.Empty(after.Timers.Advance(2.9));
        Assert.Equal(new[] { (instance, 7) }, after.Timers.Advance(0.2));
        Assert.Empty(after.Timers.Advance(10));
    }

    [Fact]
    public void ALoadReplacesTheTimersAndRegistrationsOfTheWorldItReplaces()
    {
        var host = NewHost();
        var instance = Attach(host, OwnerA);
        var saved = new Tables().Save();
        var live = new Tables();
        live.Timers.Start(instance, 1, 3);
        live.Registrations.Add(instance, "PlayerSleep", "none");
        live.Custom.Register(instance, "s", "e");

        live.Load(saved, host);

        Assert.Equal(0, live.Timers.Count);
        Assert.Equal(0, live.Registrations.Count);
        Assert.Empty(live.Custom.Receivers);
    }

    [Fact]
    public void AnEntryWhoseScriptIsNotLiveIsDroppedAndCounted()
    {
        var host = NewHost();
        var instance = Attach(host, OwnerA);
        var before = new Tables();
        before.Timers.Start(instance, 4, 1);
        before.Registrations.Add(instance, "HitEvent", "none");
        before.Custom.Register(instance, "s", "e");
        var save = before.Save();

        var otherHost = NewHost();
        Attach(otherHost, OwnerB);
        var after = new Tables();

        Assert.Equal(3, after.Load(save, otherHost));
        Assert.Equal(0, after.Timers.Count);
    }

    [Fact]
    public void AScriptWithNoOwnerCannotBeNamedSoItIsNotSaved()
    {
        var host = NewHost();
        var tables = new Tables();
        tables.Timers.Start(Attach(host, null), 4, 1);

        Assert.Empty(ScriptEventsSave.Read(tables.Save())!.Timers);
    }

    [Fact]
    public void AnEmptySaveStillWritesTheSectionSoALoadCanTellItFromAnOlderSave()
    {
        var tables = new Tables();

        Assert.Contains(ScriptEventsSave.SectionName, tables.Save().Keys);
        Assert.Null(ScriptEventsSave.Read(new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void TheSectionRoundTripsAndRefusesMalformedInput()
    {
        var snapshot = new ScriptEventsSnapshot(new[] { new SavedTimer("placed:A#B", "Plain", 2, 1.5) }, new[] { new SavedRegistration("placed:A#B", "Plain", "HitEvent", "") }, new[] { new SavedReceiver("placed:A#B", "Plain", "s", "e") });

        var read = ScriptEventsSave.Deserialize(ScriptEventsSave.Serialize(snapshot));

        Assert.Equal(snapshot.Timers, read.Timers);
        Assert.Equal(snapshot.Registrations, read.Registrations);
        Assert.Equal(snapshot.Receivers, read.Receivers);
        Assert.Throws<FormatException>(() => ScriptEventsSave.Deserialize("{\"version\":2,\"timers\":[],\"registrations\":[],\"receivers\":[]}"));
        Assert.Throws<FormatException>(() => ScriptEventsSave.Deserialize("{\"version\":1,\"timers\":[{\"owner\":\"o\",\"script\":\"s\",\"id\":1,\"remaining\":-1}],\"registrations\":[],\"receivers\":[]}"));
        Assert.Throws<FormatException>(() => ScriptEventsSave.Deserialize("{\"version\":1,\"timers\":[],\"registrations\":[]}"));
        Assert.Throws<FormatException>(() => ScriptEventsSave.Deserialize("not json"));
    }
}
