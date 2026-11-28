using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupStructTests : HostFixture
{
    private const string Source =
        "Scriptname QuestScript\n" +
        "Struct Config\n" +
        "  Int Speed = 1\n" +
        "  String Label = \"default\"\n" +
        "EndStruct\n" +
        "Config Property Cfg Auto\n" +
        "Function Report(Int bound) global native\n" +
        "Function OnInit()\n" +
        "  If Cfg == None\n" +
        "    Report(0)\n" +
        "  Else\n" +
        "    Report(1)\n" +
        "  EndIf\n" +
        "EndFunction\n";

    private const string ReadSource =
        "Scriptname QuestScript\n" +
        "Struct Config\n" +
        "  Int Speed = 1\n" +
        "  String Label = \"default\"\n" +
        "EndStruct\n" +
        "Config Property Cfg Auto\n" +
        "Function Report(Int bound) global native\n" +
        "Function OnInit()\n" +
        "  Report(Cfg.Speed)\n" +
        "EndFunction\n";

    private const string RowsSource =
        "Scriptname QuestScript\n" +
        "Struct Config\n" +
        "  Int Speed = 1\n" +
        "  String Label = \"default\"\n" +
        "EndStruct\n" +
        "Config[] Property Rows Auto\n" +
        "Function Report(Int bound) global native\n" +
        "Function OnInit()\n" +
        "  Rows[1].Speed = Rows[1].Speed + 3\n" +
        "  Report(Rows[1].Speed)\n" +
        "EndFunction\n";

    private static QuestStartupValue Scalar(int? integer = null, int? realBits = null, bool? flag = null, string? text = null,
        string? key = null, short alias = -1) => new(integer, realBits, flag, text, null, alias, null, key);

    private static QuestStartupMember Member(string name, byte type, QuestStartupValue value) => new(name, type, value);

    private static QuestStartupProperty Struct(string name, params QuestStartupMember[] members) =>
        new("QuestScript", name, 7, 1, new QuestStartupValue(null, null, null, null, null, null, (uint)members.Length, null,
            Structs: new[] { new QuestStartupStruct(members) }));

    private static QuestStartupInput With(params QuestStartupProperty[] properties)
    {
        var input = QuestStartupBindingTests.Input();
        return input with { Metadata = input.Metadata! with { ScriptProperties = properties } };
    }

    private static QuestStartupProperty Config() => Struct("Cfg",
        Member("Speed", 3, Scalar(7)),
        Member("Label", 2, Scalar(text: "go")),
        Member("Enabled", 5, Scalar(flag: true)),
        Member("Scale", 4, Scalar(realBits: BitConverter.SingleToInt32Bits(1.5f))));

    [Fact]
    public void AStructPropertyBindsItsMembersCaseInsensitively()
    {
        Assert.Null(QuestStartupBinding.Prepare(With(Config()), out var scripts));
        var cfg = scripts["QuestScript"]["Cfg"];
        Assert.Equal(PapyrusValueKind.Struct, cfg.Kind);
        Assert.Equal(7, cfg.Members!["speed"].Integer);
        Assert.Equal("go", cfg.Members["LABEL"].Text);
        Assert.True(cfg.Members["Enabled"].Flag);
        Assert.Equal(1.5, cfg.Members["Scale"].Real);
    }

    [Fact]
    public void AStructMemberWithNoObjectReferenceBindsAsNone()
    {
        Assert.Null(QuestStartupBinding.Prepare(With(Struct("Cfg", Member("Speed", 3, Scalar(7)), Member("Link", 1, Scalar(key: null)))), out var scripts));
        var cfg = scripts["QuestScript"]["Cfg"];
        Assert.Equal(7, cfg.Members!["Speed"].Integer);
        Assert.Equal(PapyrusValueKind.None, cfg.Members["Link"].Kind);
    }

    [Fact]
    public void ARadicleRefusedMemberRefusesTheWholeStructInsteadOfDefaulting()
    {
        var aliased = Prepare(Struct("Cfg", Member("Link", 1, Scalar(key: "000123:QuestProbe.esp", alias: 3))));
        var empty = Prepare(Struct("Cfg"));
        Assert.Contains("Cfg", aliased, StringComparison.Ordinal);
        Assert.Contains("Cfg", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void AMemberTypeGardenCannotShapeRefusesTheStruct()
    {
        Assert.Contains("Cfg", Prepare(Struct("Cfg", Member("Items", 13, Scalar()))), StringComparison.Ordinal);
    }

    [Fact]
    public void AStructRadicleDidNotDecodeStillRefusesTheQuest()
    {
        var bare = new QuestStartupProperty("QuestScript", "Cfg", 7, 1,
            new QuestStartupValue(null, null, null, null, null, null, 2, null));
        Assert.Contains("type 7", Prepare(bare), StringComparison.Ordinal);
    }

    [Fact]
    public void AStructArrayPropertyBindsEveryElementsMembers()
    {
        Assert.Null(QuestStartupBinding.Prepare(With(Rows()), out var scripts));
        var rows = scripts["QuestScript"]["Rows"];
        Assert.Equal(PapyrusValueKind.Array, rows.Kind);
        Assert.Equal(new long[] { 1, 2 }, rows.Elements!.Select(item => item.Members!["SPEED"].Integer).ToArray());
        Assert.Equal(new[] { "a", "b" }, rows.Elements!.Select(item => item.Members!["label"].Text).ToArray());
    }

    [Fact]
    public void AStructArrayElementRadicleRefusesRefusesTheWholeArray()
    {
        var aliased = new QuestStartupProperty("QuestScript", "Rows", 17, 1, new QuestStartupValue(null, null, null, null, null, null, 1, null,
            Structs: new[] { new QuestStartupStruct(new[] { Member("Link", 1, Scalar(key: "000123:QuestProbe.esp", alias: 3))}) }));
        Assert.Contains("Rows", Prepare(aliased), StringComparison.Ordinal);
    }

    private static QuestStartupProperty Rows() => new("QuestScript", "Rows", 17, 1, new QuestStartupValue(null, null, null, null, null, null, 2, null,
        Structs: new[]
        {
            new QuestStartupStruct(new[] { Member("Speed", 3, Scalar(1)), Member("Label", 2, Scalar(text: "a")) }),
            new QuestStartupStruct(new[] { Member("Speed", 3, Scalar(2)), Member("Label", 2, Scalar(text: "b")) }),
        }));

    [Fact]
    public void AStructArrayAttachesWithTheDeclaredElementTypeAndAScriptReadsAMember()
    {
        var gate = QuestStartupBindingTests.Gate();
        var pex = ConformanceScripts.Compile(new[] { ("QuestScript", RowsSource) }).Values;
        var host = (RadicleScriptHost)Track(new RadicleScriptHost(gate, pex));
        var seen = new List<long>();
        Assert.True(host.TryRegisterNative(new NativeId("QuestScript", "Report"), args => { seen.Add(args[0].Integer); return PapyrusValue.None; },
            NativeTargetKind.Static, "QuestScript"));
        var result = new QuestRuntime(gate).StartNewGame(new[] { With(Rows()) }, host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        Assert.Empty(host.PropertyRefusals);
        Assert.Equal(new long[] { 5 }, seen);
    }

    [Fact]
    public void TheAdapterGivesEachStructArrayElementTheDeclaredElementTypeName()
    {
        var adapter = new RadicleValueAdapter(new HostObjectTable());
        var element = PapyrusValue.FromStruct(new Dictionary<string, PapyrusValue> { ["Speed"] = PapyrusValue.FromInt(7) });
        var array = PapyrusValue.FromArray(new[] { element, element });
        var typed = Assert.IsAssignableFrom<IReadOnlyList<object?>>(adapter.ToRadicle(array, "QuestScript#Config[]"));
        Assert.All(typed, item => Assert.Equal("QuestScript#Config", Assert.IsType<Radicle.Interop.PapyrusStructValue>(item).TypeName));
        var untyped = Assert.IsAssignableFrom<IReadOnlyList<object?>>(adapter.ToRadicle(array, null));
        Assert.All(untyped, item => Assert.Equal("", Assert.IsType<Radicle.Interop.PapyrusStructValue>(item).TypeName));
    }

    [Fact]
    public void StructsSurviveTheStartupCacheJson()
    {
        var input = With(Config());
        var round = QuestStartupDatabase.FromJson(new QuestStartupDatabase(1, new[] { input }).ToJson());
        var members = round.Inputs[0].Metadata!.ScriptProperties[0].Value.Structs!.Single().Members;
        Assert.Equal(new[] { "Speed", "Label", "Enabled", "Scale" }, members.Select(member => member.Name));
        Assert.Equal(7, members[0].Value.Integer);
        Assert.Null(QuestStartupBinding.Prepare(round.Inputs[0], out _));
    }

    [Fact]
    public void ACacheStructWithANullMemberIsRejected()
    {
        var bad = new QuestStartupProperty("QuestScript", "Cfg", 7, 1, new QuestStartupValue(null, null, null, null, null, null, 1, null,
            Structs: new[] { new QuestStartupStruct(new QuestStartupMember[] { null! }) }));
        Assert.Throws<InvalidOperationException>(() =>
            new QuestStartupDatabase(1, new[] { With(bad) }).Validate());
    }

    [Fact]
    public void ARadicleCompiledScriptReadsAStructMember()
    {
        Assert.Equal(new long[] { 7 }, ReadSpeed(ConformanceScripts.Compile(new[] { ("QuestScript", ReadSource) }).Values));
    }

    [Fact]
    public void AStructValueOnANonStructPropertyIsRefusedAtAttach()
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = (RadicleScriptHost)Track(TestHosts.Create(HostKind.Radicle, gate,
            new ScriptSpec("QuestScript", "Scriptname QuestScript\nInt Property Count Auto\nInt[] Property Counts Auto\nFunction Probe()\nEndFunction\n")));
        var value = PapyrusValue.FromStruct(new Dictionary<string, PapyrusValue> { ["Speed"] = PapyrusValue.FromInt(7) });
        host.Attach("QuestScript", new Dictionary<string, PapyrusValue> { ["Count"] = value, ["Counts"] = value, ["Missing"] = value });
        Assert.Equal(3, host.PropertyRefusals.Count);
        Assert.Contains("QuestScript.Count", host.PropertyRefusals[0], StringComparison.Ordinal);
        Assert.Contains("Int[]", host.PropertyRefusals[1], StringComparison.Ordinal);
        Assert.Contains("unknown", host.PropertyRefusals[2], StringComparison.Ordinal);
        Assert.Contains("property:", host.LastFailureSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructPropertyWithCaseOnlyDuplicateMembersTakesTheLast()
    {
        var adapter = new RadicleValueAdapter(new HostObjectTable());
        var value = PapyrusValue.FromStruct(new Dictionary<string, PapyrusValue>
        {
            ["Speed"] = PapyrusValue.FromInt(1), ["speed"] = PapyrusValue.FromInt(2),
        });
        var carried = Assert.IsType<Radicle.Interop.PapyrusStructValue>(adapter.ToRadicle(value, "QuestScript#Config"));
        Assert.Equal(2, carried.Members["SPEED"]);
        Assert.Single(carried.Members);
    }

    private IReadOnlyList<long> ReadSpeed(IEnumerable<byte[]> pex)
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = Track(new RadicleScriptHost(gate, pex));
        var seen = new List<long>();
        Assert.True(host.TryRegisterNative(new NativeId("QuestScript", "Report"), args => { seen.Add(args[0].Integer); return PapyrusValue.None; },
            NativeTargetKind.Static, "QuestScript"));
        var result = new QuestRuntime(gate).StartNewGame(new[] { With(Config()) }, host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        return seen;
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AStructPropertyBindsToTheScriptBeforeOnInit(HostKind kind)
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = Track(TestHosts.Create(kind, gate, new ScriptSpec("QuestScript", Source), TestHosts.Stub("Form")));
        var seen = new List<long>();
        Assert.True(host.TryRegisterNative(new NativeId("QuestScript", "Report"), args => { seen.Add(args[0].Integer); return PapyrusValue.None; },
            NativeTargetKind.Static, "QuestScript"));
        var result = new QuestRuntime(gate).StartNewGame(new[] { With(Config()) }, host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        Assert.Equal(new long[] { 1 }, seen);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AScriptWithoutAStructPropertyValueSeesNone(HostKind kind)
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = Track(TestHosts.Create(kind, gate, new ScriptSpec("QuestScript", Source), TestHosts.Stub("Form")));
        var seen = new List<long>();
        Assert.True(host.TryRegisterNative(new NativeId("QuestScript", "Report"), args => { seen.Add(args[0].Integer); return PapyrusValue.None; },
            NativeTargetKind.Static, "QuestScript"));
        var result = new QuestRuntime(gate).StartNewGame(new[] { With() }, host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        Assert.Equal(new long[] { 0 }, seen);
    }

    [Fact]
    public void TheAdapterGivesAStructItsDeclaredTypeName()
    {
        var adapter = new RadicleValueAdapter(new HostObjectTable());
        var value = PapyrusValue.FromStruct(new Dictionary<string, PapyrusValue> { ["Speed"] = PapyrusValue.FromInt(7) });
        var carried = Assert.IsType<Radicle.Interop.PapyrusStructValue>(adapter.ToRadicle(value, "QuestScript#Config"));
        Assert.Equal("QuestScript#Config", carried.TypeName);
        Assert.Equal(7, carried.Members["speed"]);
    }

    private static string Prepare(QuestStartupProperty property) =>
        QuestStartupBinding.Prepare(With(property), out _) ?? throw new InvalidOperationException("the quest was not refused");
}
