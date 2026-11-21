using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptAttachmentContainerTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");

    private const string Metadata = """
    [{"name":"Holder","properties":[
      {"name":"Refs","type":"Array","elementType":"Object","elements":[
        {"name":"","type":"Object","value":"000001:Probe.esp"},
        {"name":"","type":"Object","value":"000002:Probe.esp"},
        {"name":"","type":"Object","value":"000003:Probe.esp"},
        {"name":"","type":"Object","value":null,"none":true}]},
      {"name":"Rows","type":"Array","elementType":"Struct","elements":[
        {"name":"","type":"Struct","members":[{"name":"Speed","type":"Int","value":"4"},{"name":"Label","type":"String","value":"a"}]},
        {"name":"","type":"Struct","members":[{"name":"Speed","type":"Int","value":"9"},{"name":"Label","type":"String","value":"b"}]}]},
      {"name":"Single","type":"Struct","members":[{"name":"Speed","type":"Int","value":"6"},{"name":"Label","type":"String","value":"s"}]}
    ]}]
    """;

    private static GameSession Session(out List<string> reports)
    {
        var rows = new[] { "vm.session.cell_lifecycle", "vmad.reference_compiled_attachments", "owner.engine_identity", "vmad.reference_source", "vmad.value_binding" }
            .Select(key => (key, "ENGINE_POLICY")).ToArray();
        var gate = TestHosts.Gate(rows);
        var session = new GameSession(TestHosts.Create(HostKind.Radicle, gate,
            TestHosts.Stub("ObjectReference"),
            TestHosts.Stub("External", null, "Function Report(Int count, Int speed, String label, Int single) global native"),
            new ScriptSpec("Holder",
                "Scriptname Holder\n" +
                "Struct Row\n  Int Speed = 1\n  String Label = \"d\"\nEndStruct\n" +
                "ObjectReference[] Property Refs Auto\n" +
                "Row[] Property Rows Auto\n" +
                "Row Property Single Auto\n" +
                "Function OnInit()\n  External.Report(Refs.Length, Rows[1].Speed, Rows[1].Label, Single.Speed)\nEndFunction\n")), gate);
        var seen = new List<string>();
        session.Host.RegisterNative(new NativeId("External", "Report"),
            args => { seen.Add($"{args[0].Integer},{args[1].Integer},{args[2].Text},{args[3].Integer}"); return PapyrusValue.None; });
        reports = seen;
        return session;
    }

    [Fact]
    public void ArrayAndStructPropertiesRoundTripThroughTheMetadata()
    {
        var attachments = ScriptAttachments.Parse(Metadata);
        var properties = attachments.Single().Values;

        Assert.All(properties, value => Assert.True(value.IsSupported));
        Assert.Equal(ScriptValueKind.Array, properties[0].Kind);
        Assert.Equal("Object", properties[0].ElementType);
        Assert.Equal(4, properties[0].Elements!.Count);
        Assert.Null(properties[0].Elements![3].Value);
        Assert.Equal("Struct", properties[1].ElementType);
        Assert.Equal(new[] { "4", "9" }, properties[1].Elements!.Select(item => item.Members![0].Value));
        Assert.Equal(ScriptValueKind.Struct, properties[2].Kind);
        Assert.Equal(ScriptAttachments.ToMetadataJson(attachments), ScriptAttachments.ToMetadataJson(ScriptAttachments.Parse(ScriptAttachments.ToMetadataJson(attachments))));
    }

    [Fact]
    public void OneUndecodableNestedValueMakesTheWholePropertyUnsupported()
    {
        var json = """
        [{"name":"Holder","properties":[{"name":"Refs","type":"Array","elementType":"Object","elements":[
          {"name":"","type":"Object","value":"000001:Probe.esp"},
          {"name":"","type":"Object","value":null,"unsupported":"alias-bound object properties are unproven"}]}]}]
        """;

        var value = Assert.Single(Assert.Single(ScriptAttachments.Parse(json)).Values);

        Assert.Equal(ScriptValueKind.Unsupported, value.Kind);
        Assert.Contains("alias-bound", value.Reason);
    }

    [Theory]
    [InlineData("{\"name\":\"X\",\"type\":\"Array\",\"elements\":[]}")]
    [InlineData("{\"name\":\"X\",\"type\":\"Struct\",\"members\":[]}")]
    [InlineData("{\"name\":\"X\",\"type\":\"Array\",\"elementType\":\"Int\"}")]
    public void AContainerWithoutItsGuideIsUnsupportedNotEmpty(string property)
    {
        var json = "[{\"name\":\"Holder\",\"properties\":[" + property + "]}]";

        Assert.Equal(ScriptValueKind.Unsupported, ScriptAttachments.Parse(json).Single().Values.Single().Kind);
    }

    [Fact]
    public void ASessionAttachesArrayAndStructPropertiesAndTheScriptReadsThem()
    {
        using var session = Session(out var seen);

        var result = session.AttachReference("Cell", Owner, Metadata);

        Assert.Null(result.Refusal);
        Assert.All(result.Initialization, step => Assert.True(step.Completed, step.Message));
        Assert.Equal(new[] { "4,9,b,6" }, seen);
    }

    [Fact]
    public void AnUnsupportedNestedValueRefusesTheOwnerAndAttachesNothing()
    {
        using var session = Session(out var seen);
        var json = """
        [{"name":"Holder","properties":[{"name":"Refs","type":"Array","elementType":"Object","elements":[
          {"name":"","type":"Object","value":null,"unsupported":"object value is empty"}]}]}]
        """;

        var result = session.AttachReference("Cell", Owner, json);

        Assert.Equal("an attachment carries an unsupported property value", result.Refusal);
        Assert.Empty(result.Instances);
        Assert.Empty(seen);
    }
}
