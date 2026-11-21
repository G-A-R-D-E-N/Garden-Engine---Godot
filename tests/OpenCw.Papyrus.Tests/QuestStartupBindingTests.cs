using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupBindingTests : HostFixture
{
    private const string QuestScriptSource =
        "Scriptname QuestScript\n" +
        "Int Property Count Auto\n" +
        "Float Property SignedZero Auto\n" +
        "Bool Property Flag Auto\n" +
        "String Property Text Auto\n" +
        "Form Property Link Auto\n" +
        "Function OnInit()\n" +
        "EndFunction\n";

    private static readonly string[] GateKeys =
    {
        QuestRuntime.StartupOrderingKey,
        QuestRuntime.FragmentTimingKey,
        QuestRuntime.AliasFillKey,
        "vmad.value_binding",
        "owner.engine_identity",
    };

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void DecodedPropertiesAreBoundBeforeOnInitOnTheSharedHost(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate);
        host.Overrides = new ScriptOverrideRegistry(gate);
        IReadOnlyDictionary<string, PapyrusValue>? observed = null;
        host.Overrides.Register("QuestScript", "OnInit", 0, args =>
        {
            observed = args.Instance.Properties;
            return OverrideResult.Handled(PapyrusValue.None);
        });
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { Input() }, host);
        Assert.True(result.Applied);
        var instance = Assert.Single(runtime.InstancesFor("000800:QuestProbe.esp"));
        Assert.Same(instance, Assert.Single(host.Instances));
        Assert.Same(instance.Properties, observed);
        Assert.Equal(17, observed!["Count"].Integer);
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0d), BitConverter.DoubleToInt64Bits(observed["SignedZero"].Real));
        Assert.False(observed["Flag"].Flag);
        Assert.Equal("a text", observed["Text"].Text);
        Assert.Equal("000123:QuestProbe.esp", Assert.IsType<EngineObjectReference>(observed["Link"].Reference).Identity);
        Assert.Equal(1, result.Started);
        Assert.Equal(1, result.AttachedScripts);
    }

    public static IEnumerable<object[]> GateCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            foreach (var key in new[] { "vmad.value_binding", "owner.engine_identity" })
            {
                yield return new object[] { host[0], key };
            }
        }
    }


    public static IEnumerable<object[]> DefectCases()
    {
        foreach (var host in TestHosts.HostKinds)
        {
            for (var defect = 0; defect < 7; defect++)
            {
                yield return new object[] { host[0], defect };
            }
        }
    }

    [Theory]
    [MemberData(nameof(DefectCases))]
    public void UnsupportedOrMalformedPropertiesBlockOnlyThatQuestBeforeAttachment(HostKind kind, int defect)
    {
        var input = Input();
        var properties = input.Metadata!.ScriptProperties.ToArray();
        properties[0] = defect switch
        {
            0 => properties[0] with { Type = 13, Value = properties[0].Value with { ElementCount = 2 } },
            1 => properties[0] with { Type = 6 },
            2 => properties[0] with { Value = properties[0].Value with { Integer = null } },
            3 => properties[0] with { Script = "UnattachedScript" },
            4 => properties[0] with { Name = properties[1].Name.ToUpperInvariant() },
            5 => properties[4] with { Value = properties[4].Value with { Alias = 3 } },
            _ => properties[3] with { Value = properties[3].Value with { Text = null } },
        };
        input = input with { Metadata = input.Metadata with { ScriptProperties = properties } };
        var gate = Gate();
        var host = Host(kind, gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { Input() with { Quest = "000700:QuestProbe.esp" }, input }, host);
        Assert.True(result.Applied);
        var reason = Assert.Single(result.Reasons);
        Assert.StartsWith("000800:QuestProbe.esp: ", reason, StringComparison.Ordinal);
        if (defect == 3) Assert.Equal("000800:QuestProbe.esp: quest properties name an unattached script UnattachedScript", reason);
        Assert.Equal("000800:QuestProbe.esp", Assert.Single(result.Blocked).Quest);
        Assert.Equal(new[] { "000700:QuestProbe.esp" }, runtime.Started);
        Assert.Equal(ScriptLifecycle.Ready, Assert.Single(host.Instances).Lifecycle);
        Assert.Equal(1, runtime.AttachedScripts);
        Assert.Equal(1, runtime.BlockedQuests);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AQuestObjectPropertyWithNoResolvedReferenceBindsAsNone(HostKind kind)
    {
        var input = Input();
        var properties = input.Metadata!.ScriptProperties.ToArray();
        properties[4] = properties[4] with { Value = properties[4].Value with { ObjectKey = null } };
        input = input with { Metadata = input.Metadata with { ScriptProperties = properties } };
        var gate = Gate();
        var host = Host(kind, gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { input }, host);
        Assert.True(result.Applied);
        var link = Assert.Single(runtime.InstancesFor("000800:QuestProbe.esp")).Properties["Link"];
        Assert.Equal(PapyrusValueKind.None, link.Kind);
        Assert.Null(link.Reference);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void QuestPropertyTextWithAnEmbeddedNulIsRefusedBeforeAttachment(HostKind kind)
    {
        var input = Input();
        var properties = input.Metadata!.ScriptProperties.ToArray();
        properties[3] = properties[3] with { Value = properties[3].Value with { Text = "a\0text" } };
        input = input with { Metadata = input.Metadata with { ScriptProperties = properties } };
        var gate = Gate();
        var host = Host(kind, gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { input }, host);
        Assert.False(result.Applied);
        Assert.Contains("embedded NUL", string.Join("; ", result.Reasons), StringComparison.Ordinal);
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.StartedQuests);
        Assert.Equal(0, runtime.AttachedScripts);
        Assert.Equal(1, runtime.BlockedQuests);
    }

    internal static QuestStartupInput Input() => new("000800:QuestProbe.esp", true, new[] { "QuestScript" }, 0, 0,
        "QuestProbe.esp", Metadata: new QuestStartupMetadata(new[]
        {
            Property("Count", 3, new QuestStartupValue(17, null, null, null, null, null, null, null)),
            Property("SignedZero", 4, new QuestStartupValue(null, BitConverter.SingleToInt32Bits(-0.0f), null, null, null, null, null, null)),
            Property("Flag", 5, new QuestStartupValue(null, null, false, null, null, null, null, null)),
            Property("Text", 2, new QuestStartupValue(null, null, null, "a text", null, null, null, null)),
            Property("Link", 1, new QuestStartupValue(null, null, null, null, 0x123, -1, null, "000123:QuestProbe.esp")),
        }, "", Array.Empty<QuestStartupProperty>(), Array.Empty<QuestStartupFragment>(), Array.Empty<int>(),
            Array.Empty<QuestStartupAliasScript>(), Array.Empty<QuestStartupProperty>()));

    private static QuestStartupProperty Property(string name, byte type, QuestStartupValue value) =>
        new("QuestScript", name, type, 1, value);

    internal static EvidenceGate Gate(string? refusedKey = null) => refusedKey is null
        ? TestHosts.Gate(GateKeys.Select(key => (key, "ENGINE_POLICY")).ToArray())
        : TestHosts.GateDenying(refusedKey, GateKeys);

    private IScriptHost Host(HostKind kind, EvidenceGate gate) =>
        Track(TestHosts.Create(kind, gate, new ScriptSpec("QuestScript", QuestScriptSource),
            TestHosts.Stub("Form")));

}
