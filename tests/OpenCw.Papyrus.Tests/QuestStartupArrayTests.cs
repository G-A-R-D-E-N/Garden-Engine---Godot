using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestStartupArrayTests : HostFixture
{
    private const string Source =
        "Scriptname QuestScript\n" +
        "Int[] Property Counts Auto\n" +
        "String[] Property Names Auto\n" +
        "Float[] Property Reals Auto\n" +
        "Bool[] Property Flags Auto\n" +
        "Form[] Property Links Auto\n" +
        "Int[] Property Empty Auto\n" +
        "Quest[] Property Quests Auto\n" +
        "ObjectReference[] Property Refs Auto\n" +
        "Function OnInit()\n" +
        "EndFunction\n";

    private static QuestStartupValue Scalar(int? integer = null, int? realBits = null, bool? flag = null, string? text = null,
        string? key = null, short alias = -1) => new(integer, realBits, flag, text, null, alias, null, key);

    private static QuestStartupProperty Array(string name, byte type, params QuestStartupValue[] elements) =>
        new("QuestScript", name, type, 1, new QuestStartupValue(null, null, null, null, null, null, (uint)elements.Length, null, elements));

    private static QuestStartupInput With(params QuestStartupProperty[] properties)
    {
        var input = QuestStartupBindingTests.Input();
        return input with { Metadata = input.Metadata! with { ScriptProperties = properties } };
    }

    private static QuestStartupInput Arrays() => With(
        Array("Counts", 13, Scalar(4), Scalar(9)),
        Array("Names", 12, Scalar(text: "a"), Scalar(text: "")),
        Array("Reals", 14, Scalar(realBits: BitConverter.SingleToInt32Bits(1.5f))),
        Array("Flags", 15, Scalar(flag: true), Scalar(flag: false)),
        Array("Links", 11, Scalar(key: "000123:QuestProbe.esp"), Scalar(key: "000124:QuestProbe.esp")),
        Array("Empty", 13),
        Array("Quests", 11, Scalar(key: "000800:QuestProbe.esp")),
        Array("Refs", 11, Scalar(key: "000900:QuestProbe.esp"), Scalar(key: "000901:QuestProbe.esp")));

    [Fact]
    public void ArrayPropertiesBindEveryElementTypeInOrder()
    {
        Assert.Null(QuestStartupBinding.Prepare(Arrays(), out var scripts));
        var values = scripts["QuestScript"];
        Assert.Equal(new long[] { 4, 9 }, values["Counts"].Elements!.Select(item => item.Integer).ToArray());
        Assert.Equal(new[] { "a", "" }, values["Names"].Elements!.Select(item => item.Text).ToArray());
        Assert.Equal(1.5, Assert.Single(values["Reals"].Elements!).Real);
        Assert.Equal(new[] { true, false }, values["Flags"].Elements!.Select(item => item.Flag).ToArray());
        Assert.Equal(new[] { "000123:QuestProbe.esp", "000124:QuestProbe.esp" },
            values["Links"].Elements!.Select(item => ((EngineObjectReference)item.Reference!).Identity).ToArray());
        Assert.Equal(PapyrusValueKind.Array, values["Empty"].Kind);
        Assert.Empty(values["Empty"].Elements!);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ArrayPropertiesReachTheHostBeforeOnInit(HostKind kind)
    {
        var gate = QuestStartupBindingTests.Gate();
        var host = Track(TestHosts.Create(kind, gate, new ScriptSpec("QuestScript", Source), TestHosts.Stub("Form"), TestHosts.Stub("Quest", "Form"),
            TestHosts.Stub("ObjectReference", "Form")));
        var result = new QuestRuntime(gate).StartNewGame(new[] { Arrays() }, host);
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        var instance = Assert.Single(host.Instances);
        Assert.Equal(ScriptLifecycle.Ready, instance.Lifecycle);
        Assert.Equal(2, instance.Properties["Counts"].Elements!.Count);
        Assert.Equal(2, instance.Properties["Refs"].Elements!.Count);
    }

    [Fact]
    public void StructArraysAndArraysWithoutDecodedElementsStillRefuseTheQuest()
    {
        var bare = new QuestStartupProperty("QuestScript", "Items", 13, 1,
            new QuestStartupValue(null, null, null, null, null, null, 2, null));
        var structs = new QuestStartupProperty("QuestScript", "Rows", 17, 1,
            new QuestStartupValue(null, null, null, null, null, null, 2, null));
        Assert.Contains("type 13", QuestStartupBinding.Prepare(With(bare), out _), StringComparison.Ordinal);
        Assert.Contains("type 17", QuestStartupBinding.Prepare(With(structs), out _), StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectElementWithNoReferenceBindsAsNone()
    {
        Assert.Null(QuestStartupBinding.Prepare(With(Array("Links", 11, Scalar(key: "000123:QuestProbe.esp"), Scalar(key: null))), out var scripts));
        var elements = scripts["QuestScript"]["Links"].Elements!;
        Assert.Equal(PapyrusValueKind.Object, elements[0].Kind);
        Assert.Equal(PapyrusValueKind.None, elements[1].Kind);
    }

    [Fact]
    public void AnObjectElementWithABlankReferenceOrBoundToAnAliasIsRefusedNotDefaulted()
    {
        const string prefix = "form:000800:QuestProbe.esp# Links: Array: element 0 ";
        const string alias = prefix + "AliasBound: the object is bound to a quest alias, and the alias fill order is unproven";
        Assert.Equal(prefix + "Object: an object property carried a blank reference",
            QuestStartupBinding.Prepare(With(Array("Links", 11, Scalar(key: " "))), out _));
        Assert.Equal(alias, QuestStartupBinding.Prepare(With(Array("Links", 11, Scalar(key: "000123:QuestProbe.esp", alias: 3))), out _));
        Assert.Equal(alias, QuestStartupBinding.Prepare(With(Array("Links", 11, Scalar(key: "000123:QuestProbe.esp", alias: 0))), out _));
    }
}
