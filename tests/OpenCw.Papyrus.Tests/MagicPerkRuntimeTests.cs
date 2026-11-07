using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class MagicPerkRuntimeTests
{
    private const string Base = "000800:Npcs.esp";
    private const string Effect = "000A00:Magic.esp";

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
        (ScriptOverrideRegistry.ReflectionKey, "ENGINE_POLICY"),
        (MagicPerkRuntime.ApplyKey, "PROVEN"),
        (MagicPerkRuntime.DispelKey, "PROVEN"),
        (MagicPerkRuntime.PerkKey, "PROVEN"),
        (MagicPerkRuntime.PerkFragmentKey, "PROVEN"),
        (MagicPerkRuntime.MagicSystemKey, "UNPROVEN"),
        (MagicPerkRuntime.PackageSystemKey, "UNPROVEN"),
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

    private static PapyrusOwnerId Actor(string reference) => PapyrusOwnerId.ForPlacedReference(Base, reference);

    private static (MagicPerkRuntime Runtime, ScriptInstance Instance, List<string> Seen) Setup(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("PerkScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
            new PapyrusFunction("Fragment_Apply", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var seen = new List<string>();
        host.Overrides = new ScriptOverrideRegistry(gate);
        host.Overrides.Register("PerkScript", "Fragment_Apply", ScriptOverrideRegistry.AnyParameterCount, context =>
        {
            seen.Add(context.Function);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var instance = host.Attach("PerkScript");
        host.Initialize(instance);
        return (new MagicPerkRuntime(host, gate), instance, seen);
    }

    [Fact]
    public void AppliedEffectsHaveIndependentOwners()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        var first = runtime.Apply(Actor("000A00"), Effect);
        var second = runtime.Apply(Actor("000A00"), Effect);

        Assert.NotEqual(first, second);
        Assert.StartsWith("dynamic:", first.Key, StringComparison.Ordinal);
        Assert.Equal(2, runtime.ActiveEffects.Count);
        Assert.Equal(2, runtime.Applied);
    }

    [Fact]
    public void AnUnprovenApplyChangesNothing()
    {
        var gate = Gate((MagicPerkRuntime.ApplyKey, "UNPROVEN"));
        var (runtime, _, _) = Setup(gate);

        runtime.Apply(Actor("000A00"), Effect);

        Assert.Empty(runtime.ActiveEffects);
        Assert.Equal(1, runtime.Refused);
    }

    [Fact]
    public void DispelRemovesTheEffect()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);
        var effect = runtime.Apply(Actor("000A00"), Effect);

        Assert.True(runtime.Dispel(effect));
        Assert.Empty(runtime.ActiveEffects);
        Assert.False(runtime.Dispel(effect));
    }

    [Fact]
    public void APerkIsRecordedOnce()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        Assert.True(runtime.AddPerk(Actor("000A00"), "000B00:Perks.esp"));
        Assert.False(runtime.AddPerk(Actor("000A00"), "000B00:Perks.esp"));
        Assert.True(runtime.HasPerk(Actor("000A00"), "000B00:Perks.esp"));
        Assert.False(runtime.HasPerk(Actor("000A01"), "000B00:Perks.esp"));
        var report = runtime.Report(Array.Empty<MagicOutcome>());
        Assert.Equal(1, report.Applied);
        Assert.Equal(1, report.Refused);
    }

    [Fact]
    public void APerkFragmentRunsOnTheSharedHost()
    {
        var gate = Gate();
        var (runtime, instance, seen) = Setup(gate);

        var outcome = runtime.RunFragment(Actor("000A00"), "Fragment_Apply", instance, package: false);

        Assert.Equal(MagicState.Applied, outcome.State);
        Assert.Equal(new[] { "Fragment_Apply" }, seen);
    }

    [Fact]
    public void APackageFragmentIsBlockedAndNamed()
    {
        var gate = Gate();
        var (runtime, instance, seen) = Setup(gate);

        var outcome = runtime.RunFragment(Actor("000A00"), "Fragment_Package", instance, package: true);

        Assert.Equal(MagicState.Blocked, outcome.State);
        Assert.Contains("AI system", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(seen);
        Assert.Equal(1, runtime.Blocked);
    }

    [Fact]
    public void TheMissingSystemsAreNamedBlockers()
    {
        var gate = Gate();
        var (runtime, _, _) = Setup(gate);

        var report = runtime.Report(Array.Empty<MagicOutcome>());

        Assert.Equal(2, report.Blockers.Count);
        Assert.Contains(report.Blockers, blocker => blocker.Key == MagicPerkRuntime.MagicSystemKey);
        Assert.Contains(report.Blockers, blocker => blocker.Key == MagicPerkRuntime.PackageSystemKey);
        Assert.Contains("magic", report.ToReport(), StringComparison.Ordinal);
        Assert.Contains("ai.package", report.ToReport(), StringComparison.Ordinal);
        Assert.Equal(64, report.Digest().Length);
    }

    [Fact]
    public void TheReportIsDeterministicAndCountsEveryOperation()
    {
        var gate = Gate();
        var (runtime, instance, _) = Setup(gate);
        var outcomes = new List<MagicOutcome>
        {
            runtime.RunFragment(Actor("000A00"), "Fragment_Apply", instance, package: false),
            runtime.RunFragment(Actor("000A00"), "Fragment_Package", instance, package: true),
        };

        var first = runtime.Report(outcomes);
        var second = runtime.Report(outcomes);

        Assert.Equal(first.Outcomes.Count, first.Applied + first.Refused + first.Blocked);
        Assert.Equal(first.ToReport(), second.ToReport());
        Assert.Equal(first.Digest(), second.Digest());
        Assert.Equal(1, first.Applied);
        Assert.Equal(1, first.Blocked);
    }
}
