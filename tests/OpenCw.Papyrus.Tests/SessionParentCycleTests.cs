using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SessionParentCycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void QuestCyclesRefuseTheWholeStartupBeforeAttachment(int cycle)
    {
        var session = Session(cycle);
        var result = session.StartNewGame();
        Assert.False(result.Applied);
        Assert.Contains("cyclic parent", string.Join("; ", result.Reasons), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.Host.Instances);
        Assert.Empty(session.Quests.Started);
        Assert.Empty(session.Quests.InstancesFor("000800:QuestProbe.esp"));
        Assert.Equal(0, session.Quests.AttachedScripts);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(0, session.Events.RegisteredOwners);
        Assert.Same(result, session.StartNewGame());
        Assert.Equal(1, session.Quests.BlockedQuests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReferenceCyclesRefuseEveryAttachmentBeforeMutation(int cycle)
    {
        var session = Session(cycle);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        var result = session.AttachReference("Played", owner,
            "[{\"name\":\"Healthy\",\"properties\":[]},{\"name\":\"QuestScript\",\"properties\":[]}]");
        Assert.NotNull(result.Refusal);
        Assert.Contains("cyclic parent", result.Refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Instances);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Cells.BoundInstances);
        Assert.Equal(0, session.Events.RegisteredOwners);
        Assert.Equal(1, session.RefusedReferences);
        Assert.Equal(0, session.FailedInitializations);
    }

    [Fact]
    public void LoadedAcyclicParentsStillAllowStartupAndReferenceAttachment()
    {
        var session = Session(3);
        Assert.True(session.StartNewGame().Applied);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        Assert.Null(session.AttachReference("Played", owner,
            "[{\"name\":\"QuestScript\",\"properties\":[]}]").Refusal);
        Assert.Equal(3, session.Host.Instances.Count);
        Assert.Equal(0, session.RefusedReferences);
        Assert.Equal(0, session.Quests.BlockedQuests);
    }

    private static GameSession Session(int cycle)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("Healthy", Array.Empty<PapyrusFunction>(), Array.Empty<string>()));
        library.Add(new PapyrusScript("QuestScript", Array.Empty<PapyrusFunction>(), Array.Empty<string>(),
            Parent: cycle == 0 ? "questscript" : "Middle"));
        if (cycle != 0)
            library.Add(new PapyrusScript("Middle", Array.Empty<PapyrusFunction>(), Array.Empty<string>(),
                Parent: cycle == 1 ? "QUESTSCRIPT" : cycle == 2 ? "Root" : "Healthy"));
        if (cycle == 2)
            library.Add(new PapyrusScript("Root", Array.Empty<PapyrusFunction>(), Array.Empty<string>(), Parent: "middle"));
        var keys = new[] { EvidenceKeys.OnInitBarrier, QuestRuntime.StartupOrderingKey,
            "vm.session.cell_lifecycle", "vmad.reference_compiled_attachments", "vmad.reference_source", "owner.engine_identity" };
        var gate = new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"ENGINE_POLICY\"}}"))}]}}"));
        var input = QuestStartupBindingTests.Input();
        input = input with { Metadata = input.Metadata! with { ScriptProperties = Array.Empty<QuestStartupProperty>() } };
        var healthy = input with { Quest = "000700:QuestProbe.esp", Scripts = new[] { "Healthy" } };
        return new GameSession(library, gate, new[] { healthy, input });
    }
}
