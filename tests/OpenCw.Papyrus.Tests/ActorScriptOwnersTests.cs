using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ActorScriptOwnersTests
{
    private const string Base = "000800:Quests.esp";

    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.Return, "ENGINE_POLICY"),
        (EvidenceKeys.Parameters, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (ScriptOverrideRegistry.Key, "ENGINE_POLICY"),
        (ActorScriptOwners.PrecedenceKey, "UNPROVEN"),
        (ActorScriptOwners.SpawnKey, "UNPROVEN"),
    };

    private static EvidenceGate Gate(params (string Key, string Status)[] overrides)
    {
        var rows = Rows.ToDictionary(row => row.Key, row => row.Status, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            rows[pair.Key] = pair.Status;
        }
        var entries = rows.Select(row => $"{{\"key\":\"{row.Key}\",\"status\":\"{row.Value}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", entries)}]}}"));
    }

    private static ScriptHost Host(EvidenceGate gate, params string[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var name in scripts)
        {
            library.Add(new PapyrusScript(name, new[]
            {
                new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            }, Array.Empty<string>()));
        }
        return new ScriptHost(library, gate);
    }

    private static ActorInput Placed(string reference, string? baseScript, string? referenceScript = null) =>
        new(ActorOrigin.Placed, Base, reference, baseScript, referenceScript);

    private static ActorInput Spawned(string token, string? baseScript) =>
        new(ActorOrigin.Spawned, Base, token, baseScript, null);

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void FailedInitializationDoesNotPublishAnActorScript(bool spawned, int failure)
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"), (ActorScriptOwners.SpawnKey, "PROVEN"));
        var library = new ScriptLibrary();
        foreach (var script in new[] { "Healthy", "Failure" })
            library.Add(new PapyrusScript(script, new[]
            {
                new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Unsupported("synthetic actor init failure") }),
            }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var initialized = new List<string>();
        foreach (var script in new[] { "Healthy", "Failure" })
            host.Overrides.Register(script, "OnInit", 0, context =>
            {
                initialized.Add(context.Instance.Script.Name);
                if (context.Instance.Script.Name == "Healthy") return OverrideResult.Handled(PapyrusValue.None);
                if (failure == 1) throw new InvalidOperationException("synthetic actor init failure");
                return failure == 0 ? OverrideResult.Refused("synthetic actor init failure") : OverrideResult.Declined();
            });
        var owners = new ActorScriptOwners(host, gate);
        var actors = new[] { "Healthy", "Failure", "Healthy" }.Select((script, index) => spawned
            ? Spawned($"spawn-{index}", script)
            : Placed($"000A0{index}", script));

        var report = owners.Attach(actors);

        Assert.Equal(2, report.Attached);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Unresolved);
        Assert.Equal(report.Attached, owners.Attached);
        Assert.Equal(report.Refused, owners.Refused);
        Assert.Equal(report.Actors.Count, report.Attached + report.Refused + report.Unresolved);
        Assert.Equal(ActorState.Attached, report.Actors[0].State);
        Assert.NotEqual(ActorState.Attached, report.Actors[1].State);
        Assert.Equal("Failure", report.Actors[1].Script);
        Assert.Contains(failure == 2 ? "vm.ir.unsupported_operation" : "synthetic actor init failure", report.Actors[1].Reason, StringComparison.Ordinal);
        Assert.Equal(ActorState.Attached, report.Actors[2].State);
        Assert.NotNull(owners.InstanceOf(report.Actors[0].Owner));
        Assert.Null(owners.InstanceOf(report.Actors[1].Owner));
        Assert.NotNull(owners.InstanceOf(report.Actors[2].Owner));
        Assert.Equal(new[] { ScriptLifecycle.Ready, ScriptLifecycle.Failed, ScriptLifecycle.Ready }, host.Instances.Select(instance => instance.Lifecycle));
        Assert.Equal(new[] { "Healthy", "Failure", "Healthy" }, initialized);
        Assert.False(host.Initialize(host.Instances[1]).Completed);
        Assert.Equal(3, initialized.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockedInitializationDoesNotPublishAnActorScript(bool spawned)
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"), (ActorScriptOwners.SpawnKey, "PROVEN"),
            (EvidenceKeys.OnInitBarrier, "UNPROVEN"));
        var host = Host(gate, "NpcScript");
        var owners = new ActorScriptOwners(host, gate);
        var report = owners.Attach(new[] { spawned ? Spawned("spawn-1", "NpcScript") : Placed("000A00", "NpcScript") });

        Assert.Equal(0, report.Attached);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, owners.Attached);
        Assert.Equal(1, owners.Refused);
        Assert.NotEqual(ActorState.Attached, Assert.Single(report.Actors).State);
        Assert.Contains(EvidenceKeys.OnInitBarrier, report.Actors[0].Reason, StringComparison.Ordinal);
        Assert.Null(owners.InstanceOf(report.Actors[0].Owner));
        Assert.Equal(ScriptLifecycle.NotInitialized, Assert.Single(host.Instances).Lifecycle);
    }

    [Fact]
    public void APlacedActorRunsItsOwnScriptOnceThePrecedenceIsProven()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);

        var report = owners.Attach(new[] { Placed("000A00", "NpcScript") });

        var actor = Assert.Single(report.Actors);
        Assert.Equal(ActorState.Attached, actor.State);
        Assert.Equal("NpcScript", actor.Script);
        Assert.Equal("placed:000800:Quests.esp#000A00", actor.Owner.Key);
        Assert.NotNull(owners.InstanceOf(actor.Owner));
    }

    [Fact]
    public void TwoSpawnedActorsOfOneBaseAreDistinctOwners()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"), (ActorScriptOwners.SpawnKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);

        var report = owners.Attach(new[] { Spawned("spawn-1", "NpcScript"), Spawned("spawn-2", "NpcScript") });

        Assert.Equal(2, report.Attached);
        Assert.NotEqual(report.Actors[0].Owner, report.Actors[1].Owner);
        Assert.Equal("dynamic:000800:Quests.esp#spawn-1", report.Actors[0].Owner.Key);
        Assert.Equal("dynamic:000800:Quests.esp#spawn-2", report.Actors[1].Owner.Key);
    }

    [Fact]
    public void APlacedAndASpawnedActorOfOneBaseAreDistinct()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"), (ActorScriptOwners.SpawnKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);

        var report = owners.Attach(new[] { Placed("000A00", "NpcScript"), Spawned("spawn-1", "NpcScript") });

        Assert.NotEqual(report.Actors[0].Owner, report.Actors[1].Owner);
        Assert.Equal(ActorOrigin.Placed, report.Actors[0].Origin);
        Assert.Equal(ActorOrigin.Spawned, report.Actors[1].Origin);
    }

    [Fact]
    public void AnUnprovenPrecedenceMergesNothingAndIsCounted()
    {
        var gate = Gate();
        var owners = new ActorScriptOwners(Host(gate, "NpcScript", "RefScript"), gate);

        var report = owners.Attach(new[] { Placed("000A00", "NpcScript", "RefScript") });

        var actor = Assert.Single(report.Actors);
        Assert.Equal(ActorState.UnresolvedPrecedence, actor.State);
        Assert.Null(actor.Script);
        Assert.Equal(1, report.Unresolved);
        Assert.Equal(0, report.Attached);
        Assert.Null(owners.InstanceOf(actor.Owner));
    }

    [Fact]
    public void SpawningIsRefusedWhileItIsUnproven()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);

        var report = owners.Attach(new[] { Spawned("spawn-1", "NpcScript") });

        var actor = Assert.Single(report.Actors);
        Assert.Equal(ActorState.RefusedSpawn, actor.State);
        Assert.Equal(1, report.Refused);
        Assert.Equal(0, report.Attached);
        Assert.Contains("not proven", actor.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AProvenSpawnCreatesTheActorAndItsScript()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"), (ActorScriptOwners.SpawnKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);

        var report = owners.Attach(new[] { Spawned("spawn-1", "NpcScript") });

        Assert.Equal(1, report.Attached);
        Assert.NotNull(owners.InstanceOf(report.Actors[0].Owner));
        Assert.Equal("dynamic:000800:Quests.esp#spawn-1", report.Actors[0].Owner.Key);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryActor()
    {
        var gate = Gate((ActorScriptOwners.PrecedenceKey, "PROVEN"));
        var owners = new ActorScriptOwners(Host(gate, "NpcScript"), gate);
        var actors = new List<ActorInput>
        {
            Placed("000A01", "NpcScript"),
            Placed("000A00", "NpcScript"),
            Spawned("spawn-1", "NpcScript"),
        };

        var first = owners.Attach(actors);
        var second = owners.Attach(actors);

        Assert.Equal(first.Actors.Count, first.Attached + first.Unresolved + first.Refused);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(64, first.Digest().Length);
        Assert.Equal(2, first.Attached);
        Assert.Equal(1, first.Refused);
    }
}
