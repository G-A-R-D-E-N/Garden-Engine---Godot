using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class InitializationPreflightTests
{
    private const string Metadata = "[{\"name\":\"First\",\"properties\":[]},{\"name\":\"Second\",\"properties\":[]}]";
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
    private static readonly QuestStartupMetadata EmptyMetadata = new(Array.Empty<QuestStartupProperty>(), "",
        Array.Empty<QuestStartupProperty>(), Array.Empty<QuestStartupFragment>(), Array.Empty<int>(),
        Array.Empty<QuestStartupAliasScript>(), Array.Empty<QuestStartupProperty>());
    private static readonly QuestStartupInput[] Startup =
    {
        new("000100:Probe.esp", true, Array.Empty<string>(), 0, 0, Metadata: EmptyMetadata),
        new("000200:Probe.esp", true, new[] { "First", "Second" }, 0, 0, Metadata: EmptyMetadata),
        new("000300:Probe.esp", false, Array.Empty<string>(), 0, 0, Metadata: EmptyMetadata),
    };

    private static EvidenceGate Gate(string? status)
    {
        var keys = new[] { EvidenceKeys.Frames, EvidenceKeys.Return, EvidenceKeys.Host, EvidenceKeys.FailClosed,
            QuestRuntime.StartupOrderingKey, "vm.session.cell_lifecycle", "vmad.reference_compiled_attachments",
            "vmad.reference_source", "owner.engine_identity" };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}").ToList();
        if (status is not null) rows.Add($"{{\"key\":\"{EvidenceKeys.OnInitBarrier}\",\"status\":\"{status}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
    }

    private static ScriptLibrary Library()
    {
        var library = new ScriptLibrary();
        foreach (var name in new[] { "First", "Second" })
            library.Add(new PapyrusScript(name, new[]
            {
                new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            }, Array.Empty<string>()));
        return library;
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("UNPROVEN", false)]
    [InlineData("CONTRADICTED", false)]
    [InlineData(null, true)]
    [InlineData("UNPROVEN", true)]
    [InlineData("CONTRADICTED", true)]
    public void AClosedHostInitializationGateRefusesAllQuestStartupBeforeMutation(string? status, bool separateHost)
    {
        var gate = Gate(status);
        var runtime = new QuestRuntime(separateHost ? Gate("ENGINE_POLICY") : gate);
        var host = new ScriptHost(Library(), gate);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var result = runtime.StartNewGame(Startup, host);
            Assert.False(result.Applied);
            Assert.Equal(0, result.Started);
            Assert.Equal(0, result.AttachedScripts);
            Assert.Empty(host.Instances);
            Assert.Empty(runtime.InstancesFor(Startup[1].Quest));
            Assert.Equal(0, runtime.StartedQuests);
            Assert.Equal(0, runtime.AttachedScripts);
            Assert.Equal(attempt, runtime.BlockedQuests);
            Assert.Equal(attempt, runtime.SkippedQuests);
            Assert.Equal(0, runtime.Approximations);
            Assert.Contains(result.Reasons, reason => reason.Contains(EvidenceKeys.OnInitBarrier, StringComparison.Ordinal));
            Assert.Equal(attempt, gate.Stops[EvidenceKeys.OnInitBarrier]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UNPROVEN")]
    [InlineData("CONTRADICTED")]
    public void AClosedInitializationGateRefusesTheWholeReferenceBeforeMutation(string? status)
    {
        var session = new GameSession(Library(), Gate(status));
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            Assert.Contains(EvidenceKeys.OnInitBarrier, session.AttachReference("Cell", Owner, Metadata).Refusal);
            Assert.Empty(session.Host.Instances);
            Assert.Equal(0, session.Cells.BoundInstances);
            Assert.Equal(0, session.FailedInitializations);
            Assert.Equal(attempt, session.RefusedReferences);
        }
    }

    [Theory]
    [InlineData("PROVEN")]
    [InlineData("ENGINE_POLICY")]
    [InlineData("APPROXIMATED")]
    public void AllowedInitializationKeepsOneApproximationPerInitializedScript(string status)
    {
        var gate = Gate(status);
        var session = new GameSession(Library(), gate, Startup);
        var attached = session.AttachReference("Cell", Owner, Metadata);
        Assert.Null(attached.Refusal);
        Assert.All(attached.Initialization, result => Assert.True(result.Completed));
        Assert.Equal(2, attached.Instances.Count);
        Assert.Equal(status == "APPROXIMATED" ? 2 : 0, gate.ApproximationCount);
        Assert.True(session.StartNewGame().Applied);
        Assert.Equal(2, session.Quests.StartedQuests);
        Assert.Equal(2, session.Quests.AttachedScripts);
        Assert.Equal(status == "APPROXIMATED" ? 4 : 0, gate.ApproximationCount);
        Assert.All(session.Host.Instances, instance => Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle));
    }

    [Fact]
    public void AClosedInitializationGateDoesNotRefuseScriptFreeStartupOrReferences()
    {
        var gate = Gate("UNPROVEN");
        var session = new GameSession(Library(), gate, new[] { Startup[0] });
        Assert.True(session.StartNewGame().Applied);
        Assert.Null(session.AttachReference("Cell", Owner, "[]").Refusal);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, gate.StopCount);
    }
}
