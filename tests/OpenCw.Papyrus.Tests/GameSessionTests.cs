using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class GameSessionTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private const string Metadata = "[{\"name\":\"First\",\"properties\":[{\"name\":\"Count\",\"type\":\"Int\",\"value\":\"7\"}]},{\"name\":\"Second\",\"properties\":[]}]";

    private static readonly string[] SessionKeys =
    {
        "vm.session.cell_lifecycle", "vm.session.activation_routing", "vmad.reference_compiled_attachments",
        "owner.engine_identity", "event.OnActivate",
    };

    private static ScriptSpec Handler(string name, bool waiting, string members) => new(name,
        $"Scriptname {name}\n" + members +
        "Function OnActivate(ObjectReference akActionRef)\n" +
        (waiting ? "  Int got = External.Answer()\n  External.Observe(got)\n" : "") +
        "EndFunction\n");

    internal static GameSession Session(bool sourceAllowed = true, bool waiting = false,
        string? bindingStatus = "ENGINE_POLICY")
    {
        var rows = SessionKeys.Select(key => (key, "ENGINE_POLICY")).ToList();
        rows.Add(("vmad.reference_source", sourceAllowed ? "ENGINE_POLICY" : "UNPROVEN"));
        if (bindingStatus is not null) rows.Add(("vmad.value_binding", bindingStatus));
        var gate = TestHosts.Gate(rows.ToArray());
        return new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            TestHosts.Stub("External", null, "Int Function Answer() global native", "Function Observe(Int value) global native"),
            Handler("First", waiting, "Int Property Count Auto\n"),
            Handler("Second", waiting, "")), gate);
    }

    private static GameSession RealRegistrySession(bool waiting)
    {
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")!;
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
        return new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            TestHosts.Stub("External", null, "Int Function Answer() global native", "Function Observe(Int value) global native"),
            Handler("First", waiting, "Int Property Count Auto\n"),
            Handler("Second", waiting, "")), gate);
    }

    [Fact]
    public void TheEmbeddedRegistryAttachesAScriptAndDeliversOnActivate()
    {
        using var session = RealRegistrySession(waiting: false);
        var attached = session.AttachReference("Played", Owner, Metadata);
        Assert.Null(attached.Refusal);
        Assert.Equal(PapyrusValue.FromInt(7), attached.Instances[0].Properties["Count"]);
        Assert.Equal(2, session.Activate(Owner, Activator).Delivered);
    }

    [Fact]
    public void AMissingNativeDegradesToANonDeliveryAndTheSessionKeepsRunning()
    {
        using var session = RealRegistrySession(waiting: true);
        Assert.Null(session.AttachReference("Played", Owner, Metadata).Refusal);
        var batch = session.Activate(Owner, Activator);
        Assert.Equal(0, batch.Delivered);
        Assert.Equal(2, batch.Outcomes.Count);
        var other = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Other:Probe.esp");
        Assert.Null(session.AttachReference("Played", other, Metadata).Refusal);
    }

    [Fact]
    public void ATriggerEntryReachesTheAreaScriptWithTheEnteringReference()
    {
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")!;
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
        using var session = new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            new ScriptSpec("Zone", "Scriptname Zone\nFunction OnTriggerEnter(ObjectReference akActionRef)\nEndFunction\n")), gate);
        Assert.Null(session.AttachReference("Played", Owner, "[{\"name\":\"Zone\",\"properties\":[]}]").Refusal);
        Assert.Equal(1, session.Trigger(Owner, Activator, true).Delivered);
        Assert.Equal(1, session.Trigger(Owner, Activator, false).NoHandler);
    }

    [Fact]
    public void AReattachedCellOwnerReceivesCellAttachOnlyAfterReattach()
    {
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")!;
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
        using var session = new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            new ScriptSpec("Zone", "Scriptname Zone\nFunction OnCellAttach()\nEndFunction\n")), gate);
        Assert.Null(session.AttachReference("Cell", Owner, "[{\"name\":\"Zone\",\"properties\":[]}]").Refusal);
        session.DetachCell("Cell");
        Assert.False(session.Lifecycle.Transition(Owner, LifecycleTransition.CellAttached).Delivered);
        session.ReattachCell("Cell", new HashSet<PapyrusOwnerId> { Owner });
        Assert.True(session.Lifecycle.Transition(Owner, LifecycleTransition.CellAttached).Delivered);
    }

    [Fact]
    public void ANestedEventFromAHandlerStaysBounded()
    {
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")!;
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
        using var session = new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            TestHosts.Stub("External", null, "Function Ping() global native"),
            new ScriptSpec("Loop", "Scriptname Loop\nFunction OnActivate(ObjectReference akActionRef)\n  External.Ping()\nEndFunction\n")), gate);
        var calls = 0;
        session.Host.RegisterNative(new NativeId("External", "Ping"), _ =>
        {
            calls++;
            session.Events.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>(), Owner));
            session.Events.Send(new EngineEvent("OnActivate", Array.Empty<PapyrusValue>()));
            return PapyrusValue.None;
        });
        Assert.Null(session.AttachReference("Cell", Owner, "[{\"name\":\"Loop\",\"properties\":[]}]").Refusal);
        session.Activate(Owner, Activator);
        Assert.InRange(calls, 1, 9);
    }

    [Fact]
    public void TheManifestNamesEveryFieldThatDiffersFromThePin()
    {
        var stale = new ScriptCacheManifest(1, RadiclePin.Repository, "oldcommit", RadiclePin.AbiVersion - 1, "Data", 2, 0, 0, 0,
            Array.Empty<string>(), Array.Empty<ScriptCacheEntry>());
        var text = stale.DescribeMismatch();
        Assert.Contains("format 1", text);
        Assert.Contains("count 2", text);
        Assert.Contains($"ABI {RadiclePin.AbiVersion - 1} (pin {RadiclePin.AbiVersion})", text);
        Assert.Contains("commit oldcommit", text);
        var current = new ScriptCacheManifest(ScriptCacheManifest.CurrentFormatVersion, RadiclePin.Repository, RadiclePin.Commit,
            RadiclePin.AbiVersion, "Data", 0, 0, 0, 0, Array.Empty<string>(), Array.Empty<ScriptCacheEntry>());
        Assert.Null(current.DescribeMismatch());
    }

    [Fact]
    public void TwoCellsShareOneHostAndReattachTheExactInstances()
    {
        using var session = Session();
        var first = session.AttachReference("Played", Owner, Metadata);
        var streamed = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Streamed:Probe.esp");
        var second = session.AttachReference("Streamed", streamed, Metadata);
        Assert.Null(first.Refusal);
        Assert.Null(second.Refusal);
        Assert.Equal(4, session.Host.Instances.Count);
        Assert.Equal(PapyrusValue.FromInt(7), first.Instances[0].Properties["Count"]);
        Assert.Equal(2, session.Activate(Owner, Activator).Delivered);
        session.DetachCell("Streamed");
        Assert.Equal(2, session.Activate(streamed, Activator).SkippedParked);
        session.ReattachCell("Streamed");
        var restored = session.AttachReference("Streamed", streamed, Metadata);
        Assert.Same(second, restored);
        Assert.Same(second.Instances[0], session.Cells.InstanceFor(streamed.Key));
        Assert.Equal(2, session.Activate(streamed, Activator).Delivered);
        Assert.Equal(4, session.Host.Instances.Count);
    }

    [Theory]
    [InlineData("[{\"name\":\"First\",\"properties\":[]},{\"name\":\"first\",\"properties\":[]}]", false, "duplicate script attachments are unsupported")]
    [InlineData("[{\"name\":\"First\",\"properties\":[]},{\"name\":\"Missing\",\"properties\":[]}]", false, "the cache has no script named Missing")]
    [InlineData("[{\"name\":\"First\",\"properties\":[{\"name\":\"X\",\"type\":\"Array\"}]}]", false, "an attachment carries an unsupported property value")]
    [InlineData("[{\"name\":\"First\",\"properties\":[]}]", true, "Radicle reported a partial reference VMAD decode")]
    [InlineData("[42]", false, "compiled script metadata has an invalid attachment or property shape")]
    public void ReferencePreflightRefusesWithoutAttachingAnything(string metadata, bool partial, string refusal)
    {
        using var session = Session();
        Assert.Equal(refusal, session.AttachReference("Cell", Owner, metadata, partial).Refusal);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(1, session.RefusedReferences);
    }



    [Fact]
    public void AClosedValueBindingGateDoesNotRefusePropertyFreeAttachments()
    {
        using var session = Session(bindingStatus: "UNPROVEN");
        var result = session.AttachReference("Cell", Owner, "[{\"name\":\"First\",\"properties\":[]}]");
        Assert.Null(result.Refusal);
        Assert.Single(result.Instances);
        Assert.Equal(0, session.RefusedReferences);
    }

    [Fact]
    public void DetachingCellsWithoutScriptsDoesNotAccumulateParkedEntries()
    {
        using var session = Session();
        foreach (var cell in new[] { "EmptyFirst", "EmptySecond", "EmptyThird" }) session.DetachCell(cell);
        Assert.Equal(0, session.Cells.ParkedInstances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADetachedWaitingHandlerResumesOnTheRadicleHostAndIsNotRerunOnReattach(bool completeFirst)
    {
        using var session = Session(waiting: true);
        session.AttachReference("Cell", Owner, "[{\"name\":\"First\",\"properties\":[]}]");
        var observed = new List<PapyrusValue>();
        session.Host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(42));
        session.Host.RegisterNative(new NativeId("External", "Observe"), values =>
        {
            observed.Add(values[0]);
            return PapyrusValue.None;
        });
        Assert.Equal(1, session.Activate(Owner, Activator).Pending);
        if (completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.DetachCell("Cell");
        if (!completeFirst) Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        session.ReattachCell("Cell");
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.Equal(1, session.Events.Pending);
        Assert.Equal(0, session.Events.Delivered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AResumedHandlerSettlesAsDeliveredOrFailedInsteadOfStayingPending(bool observeFails)
    {
        using var session = Session(waiting: true);
        session.AttachReference("Cell", Owner, "[{\"name\":\"First\",\"properties\":[]}]");
        session.Host.RegisterNative(new NativeId("External", "Answer"), _ => throw new NativeWait(42));
        session.Host.RegisterNative(new NativeId("External", "Observe"), _ =>
            observeFails ? throw new InvalidOperationException("observe failed") : PapyrusValue.None);
        var settled = new List<SettledCall>();
        session.CallSettled += settled.Add;
        Assert.Equal(1, session.Activate(Owner, Activator).Pending);
        Assert.Equal(1, session.Events.PendingOutstanding);
        Assert.True(session.Host.CompleteLatent(42, PapyrusValue.FromInt(9)));
        session.Step(32);
        Assert.Equal(0, session.Events.PendingOutstanding);
        Assert.Equal(observeFails ? 0 : 1, session.Events.PendingDelivered);
        Assert.Equal(observeFails ? 1 : 0, session.Events.PendingFailed);
        Assert.Equal(!observeFails, Assert.Single(settled).Result.Completed);
        session.Step(32);
        Assert.Single(settled);
    }

    [Fact]
    public void LoadingValidatesTheCachePin()
    {
        using var cache = new TempCache();
        var built = CacheFixture.Build(cache.Root, TestHosts.Stub("First"));
        using (var session = GameSession.Load(cache.Root)) Assert.True(session.Host.HasScript("First"));
        File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with { RadicleAbiVersion = 7 }).ToJson());
        Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingRecoversAnInterruptedSwapInsteadOfRefusing(bool currentMovedAside)
    {
        using var cache = new TempCache();
        CacheFixture.Build(cache.Root, TestHosts.Stub("First"));
        File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "pending");
        if (currentMovedAside) Directory.Move(ScriptCacheLayout.CurrentDirectory(cache.Root), ScriptCacheLayout.ObsoleteDirectory(cache.Root));
        using (var session = GameSession.Load(cache.Root)) Assert.True(session.Host.HasScript("First"));
        Assert.False(File.Exists(ScriptCacheLayout.MarkerPath(cache.Root)));
    }

    [Fact]
    public void RecoveryWaitsForARunningWriterInsteadOfDeletingItsIncomingDirectory()
    {
        using var cache = new TempCache();
        CacheFixture.Build(cache.Root, TestHosts.Stub("First"));
        var incoming = ScriptCacheLayout.IncomingDirectory(cache.Root);
        Directory.CreateDirectory(incoming);
        var recovered = false;
        Task recovery;
        using (ScriptCacheBuilder.Lock(cache.Root))
        {
            recovery = Task.Run(() => { ScriptCacheBuilder.Recover(cache.Root); recovered = true; });
            Thread.Sleep(400);
            Assert.False(recovered);
            Assert.True(Directory.Exists(incoming));
        }
        Assert.True(recovery.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(Directory.Exists(incoming));
    }

    [Fact]
    public void ACacheLoadedSessionRunsOnTheRadicleHostAndDisposesIt()
    {
        using var cache = new TempCache();
        CacheFixture.Build(cache.Root, TestHosts.Stub("First"));
        var session = GameSession.Load(cache.Root);
        Assert.Equal("RadicleScriptHost", session.Host.GetType().Name);
        session.Dispose();
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.Host.Attach("First"));
    }

}
