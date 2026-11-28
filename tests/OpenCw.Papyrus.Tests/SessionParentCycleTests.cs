using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class SessionParentCycleTests : HostFixture
{
    private static readonly string[] GateKeys =
    {
        QuestRuntime.StartupOrderingKey,
        "vm.session.cell_lifecycle",
        "vmad.reference_compiled_attachments",
        "vmad.reference_source",
        "owner.engine_identity",
    };

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void LoadedAcyclicParentsStillAllowStartupAndReferenceAttachment(HostKind kind)
    {
        var gate = Gate();
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Healthy"),
            TestHosts.Stub("QuestScript", "Middle"),
            TestHosts.Stub("Middle", "Healthy")));
        var session = new GameSession(host, gate, Startup());
        Assert.True(session.StartNewGame().Applied);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        Assert.Null(session.AttachReference("Played", owner,
            "[{\"name\":\"QuestScript\",\"properties\":[]}]").Refusal);
        Assert.Equal(3, session.Host.Instances.Count);
        Assert.Equal(0, session.RefusedReferences);
        Assert.Equal(0, session.Quests.BlockedQuests);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void QuestCyclesBlockOnlyThatQuestBeforeAttachment(int cycle)
    {
        var session = CyclicSession(cycle);
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Contains("cyclic parent", string.Join("; ", result.Reasons), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("000800:QuestProbe.esp", Assert.Single(result.Blocked).Quest);
        Assert.Equal("Healthy", Assert.Single(session.Host.Instances).ScriptName);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, session.Quests.Started);
        Assert.Empty(session.Quests.InstancesFor("000800:QuestProbe.esp"));
        Assert.Equal(1, session.Quests.AttachedScripts);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(1, session.Events.RegisteredOwners);
        Assert.Same(result, session.StartupResult);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReferenceCyclesRefuseOnlyThatScriptAndAttachItsSiblings(int cycle)
    {
        var session = CyclicSession(cycle);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        var result = session.AttachReference("Played", owner,
            "[{\"name\":\"Healthy\",\"properties\":[]},{\"name\":\"QuestScript\",\"properties\":[]}]");
        Assert.Null(result.Refusal);
        Assert.Equal("Healthy", Assert.Single(result.Instances).ScriptName);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("QuestScript", issue.Script);
        Assert.Contains("cyclic parent", issue.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, session.Cells.BoundInstances);
        Assert.Equal((0, 1, 1), (session.RefusedReferences, session.PartialReferences, session.RefusedScripts));
        Assert.Equal(0, session.FailedInitializations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ASingleCyclicScriptRefusesTheOwnerAndAttachesNothing(int cycle)
    {
        var session = CyclicSession(cycle);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        var result = session.AttachReference("Played", owner, "[{\"name\":\"QuestScript\",\"properties\":[]}]");
        Assert.Contains("cyclic parent", result.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Instances);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Cells.BoundInstances);
        Assert.Equal(1, session.RefusedReferences);
    }

    private GameSession CyclicSession(int cycle)
    {
        var scripts = new List<byte[]>
        {
            Compiled(TestHosts.Stub("Healthy")),
            Compiled(TestHosts.Stub("QuestScript", "Middle"), TestHosts.Stub("Middle")),
            Compiled(TestHosts.Stub("Middle", cycle == 1 ? "QUESTSCRIPT" : "Root"), TestHosts.Stub("QuestScript"), TestHosts.Stub("Root")),
        };
        if (cycle == 2) scripts.Add(Compiled(TestHosts.Stub("Root", "middle"), TestHosts.Stub("Middle")));
        var gate = Gate();
        var host = Track(new RadicleScriptHost(gate, scripts));
        return new GameSession(host, gate, Startup());
    }

    private static byte[] Compiled(ScriptSpec script, params ScriptSpec[] imports) =>
        ConformanceScripts.Compile(imports.Prepend(script).Select(spec => (spec.Name, spec.Source)))[script.Name];

    private static IReadOnlyList<QuestStartupInput> Startup()
    {
        var input = QuestStartupBindingTests.Input();
        input = input with { Metadata = input.Metadata! with { ScriptProperties = Array.Empty<QuestStartupProperty>() } };
        var healthy = input with { Quest = "000700:QuestProbe.esp", Scripts = new[] { "Healthy" } };
        return new[] { healthy, input };
    }

    private static EvidenceGate Gate() =>
        TestHosts.Gate(GateKeys.Select(key => (key, "ENGINE_POLICY")).ToArray());
}
