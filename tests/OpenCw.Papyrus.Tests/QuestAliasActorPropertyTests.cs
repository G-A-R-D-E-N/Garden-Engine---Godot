using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vmad;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class QuestAliasActorPropertyTests
{
    private const string Quest = "000800:Quests.esp";
    private static PapyrusOwnerId Alias(int id) => PapyrusOwnerId.ForQuestAlias(Quest, id);
    private static PapyrusOwnerId Actor(int id) => PapyrusOwnerId.ForPlacedReference("000800:Npcs.esp", $"000A0{id}");

    private static EvidenceGate Gate(string? blocked = null)
    {
        var keys = new[] { EvidenceKeys.OnInitBarrier, ScriptOverrideRegistry.Key,
            QuestAliasActorBinding.BindingKey, "vmad.value_binding", "owner.engine_identity" };
        var rows = keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"{(key == blocked ? "UNPROVEN" : "ENGINE_POLICY")}\"}}");
        return new EvidenceGate(EvidenceRegistry.Load(
            $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[{string.Join(",", rows)}]}}"));
    }

    private static (QuestAliasActorBinding Binding, ScriptHost Host, PapyrusEventBridge Bridge,
        List<ScriptInstance> Initialized) Setup(string? blocked = null)
    {
        var gate = Gate(blocked);
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("AliasScript", new[]
        {
            new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }),
        }, Array.Empty<string>()));
        var host = new ScriptHost(library, gate) { Overrides = new ScriptOverrideRegistry(gate) };
        var initialized = new List<ScriptInstance>();
        host.Overrides.Register("AliasScript", "OnInit", 0, context =>
        {
            initialized.Add(context.Instance);
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var bridge = new PapyrusEventBridge(host, gate);
        return (new QuestAliasActorBinding(host, bridge, gate), host, bridge, initialized);
    }

    private static ActorAliasReport Filled(params int[] ids) => new(ids.Select(id =>
        new ActorAliasOutcome(Alias(id), id == 2 ? AliasFillKind.FindMatching : AliasFillKind.UniqueActor,
            ActorAliasState.Filled, Actor(id), "synthetic filled actor")).ToArray(), ids.Length, 0, 0, Array.Empty<string>());

    private static QuestAliasScript Script(int id, params VmadValueShape[] properties) =>
        new(Quest, id, "AliasScript", properties, Array.Empty<int>());

    [Fact]
    public void ScalarsAreOwnerLocalAndVisibleDuringInitialization()
    {
        var (binding, host, bridge, initialized) = Setup();
        var report = binding.Bind(Filled(1, 2), new[]
        {
            Script(1, new("Count", "Int", Integer: 7), new("Scale", "Float", Real: 1.5),
                new("Enabled", "Bool", Boolean: true), new("Label", "String", Text: "first")),
            Script(2, new("Count", "Int", Integer: 11), new("Label", "String", Text: "second")),
        });

        Assert.Equal(2, report.Attached);
        Assert.Equal(2, bridge.RegisteredOwners);
        Assert.Equal(2, initialized.Count);
        Assert.Equal(7, initialized[0].Properties["count"].Integer);
        Assert.Equal(1.5, initialized[0].Properties["Scale"].Real);
        Assert.True(initialized[0].Properties["Enabled"].Flag);
        Assert.Equal("first", initialized[0].Properties["Label"].Text);
        Assert.Equal(11, initialized[1].Properties["COUNT"].Integer);
        Assert.Equal("second", initialized[1].Properties["Label"].Text);
        Assert.Same(initialized[0], binding.InstanceOf(Alias(1)));
        Assert.Same(initialized[1], binding.InstanceOf(Alias(2)));
        Assert.NotSame(initialized[0].Properties, initialized[1].Properties);
        Assert.Equal(Actor(1), binding.ActorOf(Alias(1)));
        Assert.Equal(Actor(2), binding.ActorOf(Alias(2)));
        Assert.Equal(2, host.Instances.Count);
    }

    [Fact]
    public void ObjectPropertiesRetainTheExistingEngineReferenceValue()
    {
        var (binding, _, _, initialized) = Setup();
        binding.Bind(Filled(1), new[] { Script(1, new VmadValueShape("Target", "Object", Object: Quest)) });

        var value = Assert.Single(initialized).Properties["Target"];
        Assert.Equal(PapyrusValueKind.Object, value.Kind);
        Assert.Equal(Quest, Assert.IsType<EngineObjectReference>(value.Reference).Identity);
        Assert.Null(value.Text);
    }

    public static IEnumerable<object[]> InvalidProperties()
    {
        yield return new object[] { new[] { new VmadValueShape("Count", "Int") }, "integer" };
        yield return new object[] { new[] { new VmadValueShape("Scale", "Float") }, "float" };
        yield return new object[] { new[] { new VmadValueShape("Enabled", "Bool") }, "boolean" };
        yield return new object[] { new[] { new VmadValueShape("Label", "String") }, "string" };
        yield return new object[] { new[] { new VmadValueShape("Target", "Object") }, "reference" };
        yield return new object[] { new[] { new VmadValueShape("Target", "Object", HasAlias: true, Object: Quest) }, "alias" };
        yield return new object[] { new[] { new VmadValueShape("Items", "Array", ElementCount: 2) }, "array" };
        yield return new object[] { new[] { new VmadValueShape("Data", "Struct", ElementCount: 2) }, "struct" };
        yield return new object[] { new[] { new VmadValueShape("Any", "Variable") }, "variable" };
        yield return new object[] { new[] { new VmadValueShape("Unknown", "Other") }, "recognised" };
        yield return new object[] { new[] { new VmadValueShape(" ", "Int", Integer: 1) }, "empty" };
        yield return new object[] { new[] { new VmadValueShape("Count", "Int", Integer: 1),
            new VmadValueShape("COUNT", "Int", Integer: 2) }, "duplicate" };
    }

    [Theory]
    [MemberData(nameof(InvalidProperties))]
    public void InvalidPropertiesRefuseOnlyTheirScriptBeforeHostMutation(VmadValueShape[] properties, string reason)
    {
        var (binding, host, bridge, initialized) = Setup();
        var report = binding.Bind(Filled(1, 2, 3), new[]
        {
            Script(1), Script(2, properties), Script(3),
        });

        Assert.Equal(2, report.Attached);
        Assert.Equal(2, binding.Attached);
        Assert.Equal(3, report.Bound);
        Assert.Equal(0, report.Unbound);
        Assert.Empty(report.Unresolved);
        Assert.True(report.Bindings[1].Bound);
        Assert.Contains(reason, report.Bindings[1].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Actor(2), binding.ActorOf(Alias(2)));
        Assert.Null(binding.InstanceOf(Alias(2)));
        Assert.NotNull(binding.InstanceOf(Alias(1)));
        Assert.NotNull(binding.InstanceOf(Alias(3)));
        Assert.Equal(2, initialized.Count);
        Assert.Equal(2, host.Instances.Count);
        Assert.Equal(2, bridge.RegisteredOwners);
    }

    [Theory]
    [InlineData("vmad.value_binding")]
    [InlineData("owner.engine_identity")]
    public void ClosedPropertyGatesKeepTheActorBoundWithoutAttachingItsScript(string key)
    {
        var (binding, host, bridge, initialized) = Setup(key);
        var property = key == "vmad.value_binding"
            ? new VmadValueShape("Count", "Int", Integer: 1)
            : new VmadValueShape("Target", "Object", Object: Quest);

        var report = binding.Bind(Filled(1), new[] { Script(1, property) });

        Assert.Equal(1, report.Bound);
        Assert.Equal(0, report.Attached);
        Assert.Contains(key, Assert.Single(report.Bindings).Reason, StringComparison.Ordinal);
        Assert.Equal(Actor(1), binding.ActorOf(Alias(1)));
        Assert.Null(binding.InstanceOf(Alias(1)));
        Assert.Empty(host.Instances);
        Assert.Empty(initialized);
        Assert.Equal(0, bridge.RegisteredOwners);
    }

    [Fact]
    public void EmptyPropertiesDoNotRequireTheValueBindingGate()
    {
        var (binding, _, _, initialized) = Setup("vmad.value_binding");
        var report = binding.Bind(Filled(1), new[] { Script(1) });

        Assert.Equal(1, report.Attached);
        Assert.Empty(Assert.Single(initialized).Properties);
    }
}
