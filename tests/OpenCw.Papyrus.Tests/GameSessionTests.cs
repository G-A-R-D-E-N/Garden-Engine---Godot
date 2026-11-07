using System.Text;
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

    internal static GameSession Session(bool sourceAllowed = true, bool waiting = false,
        string? bindingStatus = "ENGINE_POLICY")
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Parameters,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.SchedulerBudget, EvidenceKeys.NativeTarget,
            "vm.session.cell_lifecycle", "vm.session.activation_routing", "vmad.reference_compiled_attachments", "owner.engine_identity",
            "event.OnActivate", "vm.native.latent", ScriptOverrideRegistry.Key };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}").ToList();
        rows.Add($"{{\"key\":\"vmad.reference_source\",\"status\":\"{(sourceAllowed ? "ENGINE_POLICY" : "UNPROVEN")}\"}}");
        if (bindingStatus is not null)
            rows.Add($"{{\"key\":\"vmad.value_binding\",\"status\":\"{bindingStatus}\"}}");
        var gate = new EvidenceGate(EvidenceRegistry.Load($"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
        var library = new ScriptLibrary();
        foreach (var name in new[] { "First", "Second" })
            library.Add(new PapyrusScript(name, new[]
            {
                new PapyrusFunction("OnActivate", waiting ? new[]
                {
                    PapyrusInstruction.CallStatic("External", "Answer", PapyrusOperand.Local(1), Array.Empty<PapyrusOperand>()),
                    PapyrusInstruction.CallStatic("External", "Observe", PapyrusOperand.Discard, new[] { PapyrusOperand.Local(1) }),
                    PapyrusInstruction.Return(),
                } : new[] { PapyrusInstruction.Return() }, ParameterCount: 1),
            }, Array.Empty<string>()));
        return new GameSession(library, gate);
    }

    [Fact]
    public void TwoCellsShareOneHostAndReattachTheExactInstances()
    {
        var session = Session();
        var first = session.AttachReference("Played", Owner, Metadata);
        var streamed = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Streamed:Probe.esp");
        var second = session.AttachReference("Streamed", streamed, Metadata);
        Assert.Null(first.Refusal);
        Assert.Null(second.Refusal);
        Assert.Equal(4, session.Host.Instances.Count);
        Assert.Equal(2, session.Cells.AttachedCells);
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
    [InlineData("[{\"name\":\"First\",\"properties\":[]},{\"name\":\"first\",\"properties\":[]}]", false)]
    [InlineData("[{\"name\":\"First\",\"properties\":[]},{\"name\":\"Missing\",\"properties\":[]}]", false)]
    [InlineData("[{\"name\":\"First\",\"properties\":[{\"name\":\"X\",\"type\":\"Array\"}]}]", false)]
    [InlineData("[{\"name\":\"First\",\"properties\":[]}]", true)]
    [InlineData("[42]", false)]
    public void ReferencePreflightRefusesWithoutAttachingAnything(string metadata, bool partial)
    {
        var session = Session();
        Assert.NotNull(session.AttachReference("Cell", Owner, metadata, partial).Refusal);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(1, session.RefusedReferences);
    }

    [Fact]
    public void TheSourceGateStillRefusesSyntheticMetadataWhenUnproven()
    {
        var session = Session(sourceAllowed: false);
        Assert.Contains("vmad.reference_source", session.AttachReference("Cell", Owner, Metadata).Refusal);
        Assert.Empty(session.Host.Instances);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UNPROVEN")]
    [InlineData("CONTRADICTED")]
    public void AClosedValueBindingGateRefusesTheWholeReferenceBeforeMutation(string? status)
    {
        var session = Session(bindingStatus: status);
        const string metadata = "[{\"name\":\"Second\",\"properties\":[]},{\"name\":\"First\",\"properties\":[{\"name\":\"Count\",\"type\":\"Int\",\"value\":\"7\"}]}]";
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            Assert.Contains("vmad.value_binding", session.AttachReference("Cell", Owner, metadata).Refusal);
            Assert.Empty(session.Host.Instances);
            Assert.Equal(0, session.Cells.BoundInstances);
            Assert.Equal(0, session.FailedInitializations);
            Assert.Equal(attempt, session.RefusedReferences);
        }
    }

    [Fact]
    public void AClosedValueBindingGateDoesNotRefusePropertyFreeAttachments()
    {
        var session = Session(bindingStatus: "UNPROVEN");
        var result = session.AttachReference("Cell", Owner, "[{\"name\":\"First\",\"properties\":[]}]");
        Assert.Null(result.Refusal);
        Assert.Single(result.Instances);
        Assert.Equal(0, session.RefusedReferences);
    }

    [Fact]
    public void DetachingCellsWithoutScriptsDoesNotAccumulateParkedEntries()
    {
        var session = Session();
        foreach (var cell in new[] { "EmptyFirst", "EmptySecond", "EmptyThird" }) session.DetachCell(cell);
        Assert.Equal(0, session.Cells.ParkedCells);
        Assert.Equal(0, session.Cells.ParkedInstances);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CellParkingRetainsLatentCompletionBeforeAndAfterDetach(bool completeFirst)
    {
        var session = Session(waiting: true);
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
        Assert.Empty(observed);
        session.ReattachCell("Cell");
        session.Host.RunStep(32);
        Assert.Equal(PapyrusValue.FromInt(9), Assert.Single(observed));
        Assert.Equal(1, session.Events.Pending);
        Assert.Equal(0, session.Events.Delivered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingValidatesCachePinAndIncompleteSwaps(bool marker)
    {
        using var cache = new TempCache();
        var payload = Encoding.UTF8.GetBytes(PapyrusIr.ToJson(new[]
        {
            new PapyrusScript("First", Array.Empty<PapyrusFunction>(), Array.Empty<string>()),
        }));
        var built = ScriptCacheBuilder.Build(new[]
        {
            new ScriptSourceCandidate("First", ScriptSourceKind.Loose, "project-authored", 0, payload.Length, () => payload),
        }, "project-authored", new ScriptCacheConverter(1, 1, bytes => Encoding.UTF8.GetString(bytes)), cache.Root);
        Assert.True(GameSession.Load(cache.Root).Host.HasScript("First"));
        if (marker) File.WriteAllText(ScriptCacheLayout.MarkerPath(cache.Root), "pending");
        else File.WriteAllText(ScriptCacheLayout.ManifestPath(cache.Root), (built.Manifest with { RadicleAbiVersion = 7 }).ToJson());
        Assert.Throws<InvalidOperationException>(() => GameSession.Load(cache.Root));
    }
}
