using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class InitializationPreflightTests : HostFixture
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
    private static readonly ScriptSpec[] Scripts =
    {
        OnInitOnly("First"),
        OnInitOnly("Second"),
    };
    private static readonly (string Key, string Status)[] FileRows =
    {
        (QuestRuntime.StartupOrderingKey, "ENGINE_POLICY"),
        (QuestStateMachine.LifecycleKey, "ENGINE_POLICY"),
        ("vm.session.cell_lifecycle", "ENGINE_POLICY"),
        ("vmad.reference_compiled_attachments", "ENGINE_POLICY"),
        ("vmad.reference_source", "ENGINE_POLICY"),
        ("owner.engine_identity", "ENGINE_POLICY"),
    };

    private static ScriptSpec OnInitOnly(string name) =>
        new(name, $"Scriptname {name}\nFunction OnInit()\nEndFunction\n");

    private static EvidenceGate Gate(string? status)
    {
        if (status is not null)
        {
            return TestHosts.Gate(FileRows.Append((EvidenceKeys.OnInitBarrier, status)).ToArray());
        }
        var rows = TestHosts.PolicyRows
            .Where(row => row.Key != EvidenceKeys.OnInitBarrier)
            .Concat(FileRows)
            .ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private IScriptHost Host(HostKind kind, EvidenceGate gate) => Track(TestHosts.Create(kind, gate, Scripts));

    public static IEnumerable<object[]> ClosedHostCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var status in new string?[] { null, "UNPROVEN", "CONTRADICTED" })
            {
                foreach (var separateHost in new[] { false, true })
                {
                    yield return new object[] { host[0], status!, separateHost };
                }
            }
        }
    }

    public static IEnumerable<object[]> ClosedInitializationCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var status in new string?[] { null, "UNPROVEN", "CONTRADICTED" })
            {
                yield return new object[] { host[0], status! };
            }
        }
    }

    public static IEnumerable<object[]> AllowedInitializationCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var status in new[] { "PROVEN", "ENGINE_POLICY", "APPROXIMATED" })
            {
                yield return new object[] { host[0], status };
            }
        }
    }



    [Theory]
    [MemberData(nameof(AllowedInitializationCases))]
    public void AllowedInitializationKeepsOneApproximationPerInitializedScript(HostKind kind, string status)
    {
        var gate = Gate(status);
        var per = kind == HostKind.Radicle ? 2 : 1;
        var session = new GameSession(Host(kind, gate), gate, Startup);
        var attached = session.AttachReference("Cell", Owner, Metadata);
        Assert.Null(attached.Refusal);
        Assert.All(attached.Initialization, result => Assert.True(result.Completed));
        Assert.Equal(2, attached.Instances.Count);
        Assert.Equal(status == "APPROXIMATED" ? 2 * per : 0, gate.ApproximationCount);
        Assert.True(session.StartNewGame().Applied);
        Assert.Equal(2, session.Quests.StartedQuests);
        Assert.Equal(2, session.Quests.AttachedScripts);
        Assert.Equal(status == "APPROXIMATED" ? 4 * per : 0, gate.ApproximationCount);
        Assert.All(session.Host.Instances, instance => Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AClosedInitializationGateDoesNotRefuseScriptFreeStartupOrReferences(HostKind kind)
    {
        var gate = Gate("UNPROVEN");
        var session = new GameSession(Host(kind, gate), gate, new[] { Startup[0] });
        Assert.True(session.StartNewGame().Applied);
        Assert.Null(session.AttachReference("Cell", Owner, "[]").Refusal);
        Assert.Empty(session.Host.Instances);
        Assert.Equal(0, gate.StopCount);
    }

}
