using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupBindingTests
{
    [Fact]
    public void DecodedPropertiesAreBoundBeforeOnInitOnTheSharedHost()
    {
        var gate = Gate();
        var host = Host(gate);
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

    [Theory]
    [InlineData("vmad.value_binding")]
    [InlineData("owner.engine_identity")]
    public void QuestPropertyBindingHonorsExistingEvidenceGates(string key)
    {
        var gate = Gate(key);
        var host = Host(gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { Input() }, host);
        Assert.False(result.Applied);
        Assert.Empty(host.Instances);
        Assert.Equal(1, runtime.BlockedQuests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void UnsupportedOrMalformedPropertiesBlockTheWholeBatchBeforeAttachment(int defect)
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
            6 => properties[4] with { Value = properties[4].Value with { ObjectKey = null } },
            _ => properties[3] with { Value = properties[3].Value with { Text = null } },
        };
        input = input with { Metadata = input.Metadata with { ScriptProperties = properties } };
        var gate = Gate();
        var host = Host(gate);
        var runtime = new QuestRuntime(gate);
        var result = runtime.StartNewGame(new[] { Input() with { Quest = "000700:QuestProbe.esp" }, input }, host);
        Assert.False(result.Applied);
        Assert.NotEmpty(result.Reasons);
        Assert.Empty(host.Instances);
        Assert.Equal(0, runtime.StartedQuests);
        Assert.Equal(0, runtime.AttachedScripts);
        Assert.Equal(1, runtime.BlockedQuests);
    }

    [Fact]
    public void QuestPropertyTextWithAnEmbeddedNulIsRefusedBeforeAttachment()
    {
        var input = Input();
        var properties = input.Metadata!.ScriptProperties.ToArray();
        properties[3] = properties[3] with { Value = properties[3].Value with { Text = "a\0text" } };
        input = input with { Metadata = input.Metadata with { ScriptProperties = properties } };
        var gate = Gate();
        var host = Host(gate);
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

    internal static EvidenceGate Gate(string? refusedKey = null)
    {
        var keys = new[] { QuestRuntime.StartupOrderingKey, QuestRuntime.FragmentTimingKey, QuestRuntime.AliasFillKey,
            EvidenceKeys.OnInitBarrier, EvidenceKeys.Frames, EvidenceKeys.Return, ScriptOverrideRegistry.Key,
            "vmad.value_binding", "owner.engine_identity" };
        return new EvidenceGate(EvidenceRegistry.Load($"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":[" +
            string.Join(",", keys.Select(key => $"{{\"key\":\"{key}\",\"status\":\"{(key == refusedKey ? "UNPROVEN" : "ENGINE_POLICY")}\"}}")) + "]}"));
    }

    internal static ScriptHost Host(EvidenceGate gate)
    {
        var library = new ScriptLibrary();
        library.Add(new PapyrusScript("QuestScript", new[] { new PapyrusFunction("OnInit", new[] { PapyrusInstruction.Return() }) },
            Input().Metadata!.ScriptProperties.Select(property => property.Name).ToArray()));
        return new ScriptHost(library, gate);
    }
}
