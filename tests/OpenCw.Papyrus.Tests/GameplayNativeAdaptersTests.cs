using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class GameplayNativeAdaptersTests
{
    private static readonly (string Key, string Status)[] Rows =
    {
        (EvidenceKeys.Frames, "ENGINE_POLICY"),
        (EvidenceKeys.OnInitBarrier, "ENGINE_POLICY"),
        (EvidenceKeys.EventDuringOnInit, "APPROXIMATED"),
        (EvidenceKeys.SchedulerBudget, "ENGINE_POLICY"),
        (EvidenceKeys.Host, "ENGINE_POLICY"),
        (EvidenceKeys.FailClosed, "ENGINE_POLICY"),
        (EvidenceKeys.NativeTarget, "ENGINE_POLICY"),
        (InventoryRuntime.AddKey, "PROVEN"),
        (MagicPerkRuntime.PerkKey, "PROVEN"),
        ("system.inventory", "PROVEN"),
        ("system.magic_perk", "PROVEN"),
        ("system.actor_values", "UNPROVEN"),
        ("system.factions", "UNPROVEN"),
        ("system.ai", "UNPROVEN"),
        ("system.movement", "UNPROVEN"),
        ("system.combat", "UNPROVEN"),
        ("system.crime", "UNPROVEN"),
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

    private static (GameplayNativeAdapters Adapters, ScriptHost Host, InventoryRuntime Inventory, MagicPerkRuntime Magic) Setup(
        EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("ActorScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        library.Add(new PapyrusScript("Caller", GameplayNativeAdapters.Catalogue.Select(family =>
            new PapyrusFunction(family.Native.Function, new[]
            {
                PapyrusInstruction.CallStatic("Actor", family.Native.Function, PapyrusOperand.Discard,
                    Array.Empty<PapyrusOperand>()),
                PapyrusInstruction.Return(),
            })).ToArray(), Array.Empty<string>()));
        var host = new ScriptHost(library, gate);
        var bridge = new PapyrusEventBridge(host, gate);
        var registrations = new ScriptRegistrationRegistry();
        var inventory = new InventoryRuntime(bridge, registrations, gate);
        var magic = new MagicPerkRuntime(host, gate);
        return (new GameplayNativeAdapters(host, inventory, magic, gate), host, inventory, magic);
    }

    [Fact]
    public void ExistingInventoryAndPerkSystemsBindTheirReceiverNatives()
    {
        var gate = Gate();
        var (adapters, _, _, _) = Setup(gate);

        var report = adapters.Bind();

        var inventory = report.Natives.Single(native => native.Family.Native == new NativeId("ObjectReference", "AddItem"));
        Assert.Equal(AdapterState.Bound, inventory.State);
        Assert.Contains("receiver", inventory.Reason, StringComparison.Ordinal);
        Assert.Equal("Garden.Inventory", inventory.Family.OwningSystem);
        Assert.Equal(new NativeId("ObjectReference", "AddItem"), inventory.Family.Native);
        Assert.Equal(5, report.Bound);
    }

    [Fact]
    public void TheInventoryAdapterDoesNotUseTheInventedTextConvention()
    {
        var gate = Gate();
        var (adapters, host, inventory, _) = Setup(gate);
        adapters.Bind();

        Assert.Throws<InvalidOperationException>(() => ((INativeTarget)host).Invoke(
            new NativeId("Actor", "AddItem"), NativeTargetKind.Static, PapyrusValue.None, new[]
        {
            PapyrusValue.FromString("000800:Npcs.esp"),
            PapyrusValue.FromString("000A00"),
            PapyrusValue.FromString("000900:Items.esp"),
        }));
        Assert.Equal(0, inventory.CountOf(
            OpenCommonwealth.Services.Papyrus.Owners.PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00"),
            "000900:Items.esp"));
    }

    [Fact]
    public void ThePerkAdapterDoesNotUseTheInventedTextConvention()
    {
        var gate = Gate();
        var (adapters, host, _, magic) = Setup(gate);
        adapters.Bind();

        Assert.Throws<InvalidOperationException>(() => ((INativeTarget)host).Invoke(
            new NativeId("Actor", "AddPerk"), NativeTargetKind.Static, PapyrusValue.None, new[]
        {
            PapyrusValue.FromString("000800:Npcs.esp"),
            PapyrusValue.FromString("000A00"),
            PapyrusValue.FromString("000B00:Perks.esp"),
        }));
        Assert.False(magic.HasPerk(
            OpenCommonwealth.Services.Papyrus.Owners.PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", "000A00"),
            "000B00:Perks.esp"));
    }

    [Fact]
    public void AnAbsentSystemKeepsItsNativeUnboundAndNamed()
    {
        var gate = Gate();
        var (adapters, _, _, _) = Setup(gate);

        var report = adapters.Bind();

        var actorValue = report.Natives.Single(native => native.Family.Family == "actorvalue");
        Assert.Equal(AdapterState.AbsentSystem, actorValue.State);
        Assert.Contains("Garden.ActorValues", actorValue.Reason, StringComparison.Ordinal);
        Assert.Equal(6, report.Unbound);
        Assert.Contains("Garden.AI", report.AbsentSystems);
        Assert.Contains("Garden.Combat", report.AbsentSystems);
    }

    [Fact]
    public void EveryCatalogueNativeNamesItsOwningSystem()
    {
        var gate = Gate();
        var (adapters, _, _, _) = Setup(gate);

        var report = adapters.Bind();

        Assert.Equal(GameplayNativeAdapters.Catalogue.Count, report.Natives.Count);
        Assert.All(report.Natives, native => Assert.False(string.IsNullOrWhiteSpace(native.Family.OwningSystem)));
        Assert.All(report.Natives, native => Assert.StartsWith("system.", native.Family.Key, StringComparison.Ordinal));
        Assert.Equal(report.Natives.Count, report.Bound + report.Unbound);
    }

    [Fact]
    public void TheReportIsDeterministic()
    {
        var gate = Gate();
        var (first, _, _, _) = Setup(gate);
        var (second, _, _, _) = Setup(gate);

        var firstReport = first.Bind();
        var secondReport = second.Bind();

        Assert.Equal(firstReport.ToReport(), secondReport.ToReport());
        Assert.Equal(firstReport.Digest(), secondReport.Digest());
        Assert.Equal(64, firstReport.Digest().Length);
    }

    [Fact]
    public void AnUnprovenSystemKeyKeepsTheNativeUnbound()
    {
        var gate = Gate(("system.inventory", "UNPROVEN"));
        var (adapters, _, _, _) = Setup(gate);

        var report = adapters.Bind();

        Assert.Contains(report.Natives, native => native.Family.Family == "inventory"
            && native.State == AdapterState.AbsentSystem);
        Assert.Equal(10, report.Unbound);
    }

    [Theory]
    [InlineData("actorvalue")]
    [InlineData("faction")]
    [InlineData("ai")]
    [InlineData("movement")]
    [InlineData("combat")]
    [InlineData("crime")]
    public void AnAllowedSystemCannotBindAnUnimplementedFamily(string name)
    {
        var gate = Gate(GameplayNativeAdapters.Catalogue.Select(family => (family.Key, "ENGINE_POLICY")).ToArray());
        var (adapters, host, _, _) = Setup(gate);

        var report = adapters.Bind();
        var outcome = report.Natives.Single(native => native.Family.Family == name);

        Assert.NotEqual(AdapterState.Bound, outcome.State);
        Assert.NotEqual(AdapterState.AbsentSystem, outcome.State);
        Assert.Contains("no Garden implementation", outcome.Reason, StringComparison.Ordinal);
        Assert.Empty(report.AbsentSystems);
        Assert.Equal(5, report.Bound);
        Assert.Equal(6, report.Unbound);
        Assert.Throws<InvalidOperationException>(() => ((INativeTarget)host).Invoke(outcome.Family.Native,
            NativeTargetKind.Static, PapyrusValue.None, Array.Empty<PapyrusValue>()));
    }

    [Theory]
    [InlineData("AddItem")]
    [InlineData("AddPerk")]
    [InlineData("GetValue")]
    [InlineData("GetFactionRank")]
    [InlineData("SetPackage")]
    [InlineData("TranslateTo")]
    [InlineData("StartCombat")]
    [InlineData("GetCrimeFaction")]
    public void TooFewArgumentsStopAsUnboundWithoutEscapingTheVm(string function)
    {
        var gate = Gate(GameplayNativeAdapters.Catalogue.Select(family => (family.Key, "ENGINE_POLICY")).ToArray());
        var (adapters, host, inventory, magic) = Setup(gate);
        adapters.Bind();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, function);

        Assert.False(result.Completed);
        Assert.Equal(VmStopReason.ModelError, result.Reason);
        Assert.Contains(function == "AddPerk" ? "registered as EngineObject"
            : $"no native named Actor.{function}", result.Message!, StringComparison.Ordinal);
        Assert.Equal(1, host.NativeTargetRefusals);
        Assert.Equal(0, inventory.Applied);
        Assert.Equal(0, magic.Applied);
    }
}
