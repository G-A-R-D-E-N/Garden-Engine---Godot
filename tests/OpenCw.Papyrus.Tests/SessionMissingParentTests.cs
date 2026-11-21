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

public sealed class SessionMissingParentTests : HostFixture
{
    private static readonly string[] GateKeys =
    {
        QuestRuntime.StartupOrderingKey,
        "vm.session.cell_lifecycle",
        "vmad.reference_compiled_attachments",
        "vmad.reference_source",
        "owner.engine_identity",
    };

    private static readonly string[] ChainNames = { "Healthy", "QuestScript", "First", "Second", "Third" };

    private static readonly ScriptSpec[] Chain =
    {
        TestHosts.Stub("Healthy"),
        TestHosts.Stub("QuestScript", "First"),
        TestHosts.Stub("First", "Second"),
        TestHosts.Stub("Second", "Third"),
        TestHosts.Stub("Third"),
    };

    public static IEnumerable<object[]> HostDepths()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            for (var depth = 0; depth < 3; depth++)
            {
                yield return new object[] { host[0], depth };
            }
        }
    }

    [Theory]
    [MemberData(nameof(HostDepths))]
    public void MissingQuestParentsBlockOnlyThatQuestBeforeAttachment(HostKind kind, int depth)
    {
        var session = Session(kind, depth);
        var result = session.StartNewGame();
        Assert.True(result.Applied);
        Assert.Contains("parent", string.Join("; ", result.Reasons), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("000800:QuestProbe.esp", Assert.Single(result.Blocked).Quest);
        Assert.Equal("Healthy", Assert.Single(session.Host.Instances).ScriptName);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, session.Quests.Started);
        Assert.Equal(1, session.Quests.AttachedScripts);
        Assert.Equal(1, session.Quests.BlockedQuests);
        Assert.Equal(1, session.Events.RegisteredOwners);
        Assert.Same(result, session.StartupResult);
    }

    [Theory]
    [MemberData(nameof(HostDepths))]
    public void MissingReferenceParentsRefuseEveryAttachmentBeforeMutation(HostKind kind, int depth)
    {
        var session = Session(kind, depth);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        var result = session.AttachReference("Played", owner,
            "[{\"name\":\"Healthy\",\"properties\":[]},{\"name\":\"QuestScript\",\"properties\":[]}]");
        Assert.NotNull(result.Refusal);
        Assert.Contains("parent", result.Refusal, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Instances);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, session.Cells.BoundInstances);
        Assert.Equal(0, session.Events.RegisteredOwners);
        Assert.Equal(1, session.RefusedReferences);
        Assert.Equal(0, session.FailedInitializations);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void FullyLoadedParentsAndParentlessScriptsRemainAdmissible(HostKind kind)
    {
        var session = Session(kind, 3);
        Assert.True(session.StartNewGame().Applied);
        var owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");
        Assert.Null(session.AttachReference("Played", owner,
            "[{\"name\":\"Healthy\",\"properties\":[]},{\"name\":\"QuestScript\",\"properties\":[]}]").Refusal);
        Assert.Equal(4, session.Host.Instances.Count);
        Assert.Equal(0, session.RefusedReferences);
        Assert.Equal(0, session.Quests.BlockedQuests);
    }

    private GameSession Session(HostKind kind, int depth)
    {
        var gate = Gate();
        var hosted = ChainNames.Take(depth + 2).ToArray();
        var host = Hosted(kind, gate, hosted);
        var input = QuestStartupBindingTests.Input();
        input = input with { Metadata = input.Metadata! with { ScriptProperties = Array.Empty<QuestStartupProperty>() } };
        var healthy = input with { Quest = "000700:QuestProbe.esp", Scripts = new[] { "Healthy" } };
        return new GameSession(host, gate, new[] { healthy, input });
    }

    private static EvidenceGate Gate() =>
        TestHosts.Gate(GateKeys.Select(key => (key, "ENGINE_POLICY")).ToArray());

    private IScriptHost Hosted(HostKind kind, EvidenceGate gate, string[] hosted)
    {
        var pex = ConformanceScripts.Compile(Chain.Select(spec => (spec.Name, spec.Source)));
        var bytes = hosted.Select(name => pex[name]).ToArray();
        return Track(kind switch
        {
            HostKind.Radicle => new RadicleScriptHost(gate, bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        });
    }
}
