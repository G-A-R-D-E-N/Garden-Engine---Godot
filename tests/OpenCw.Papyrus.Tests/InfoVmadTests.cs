using System.Linq;
using OpenCommonwealth.Services.Papyrus.Vmad;
using Radicle.Interop;
using InfoFragmentState = Radicle.Interop.Native.InfoFragmentState;
using VmadValueKind = Radicle.Interop.Native.VmadValueKind;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class InfoVmadTests
{
    static string? Resolve(uint raw) => raw == 0 ? null : $"{raw:X6}:Quests.esp";

    static VmadPropertyValue Plain => new(VmadValueKind.None, 0, 0, null, 0, 0f, false);

    static VmadPropertyValue Form(uint form, short alias) => Plain with { Kind = VmadValueKind.Object, ObjectFormId = form, Alias = alias };

    static VmadPropertyValue Text(string text) => Plain with { Kind = VmadValueKind.Text, Text = text };

    static VmadPropertyValue Int(int value) => Plain with { Kind = VmadValueKind.Int, Integer = value };

    static VmadPropertyValue Real(float value) => Plain with { Kind = VmadValueKind.Real, Real = value };

    static VmadPropertyValue Bool(bool value) => Plain with { Kind = VmadValueKind.Bool, Boolean = value };

    static VmadPropertyInfo Scalar(string name, byte type, VmadPropertyValue value) => new(name, type, 1, value, 0);

    static VmadElementInfo Item(VmadPropertyValue value, int index) => new(value.Kind, null, 0, index, 0, 0, value);

    static VmadElementInfo Member(string name, byte type, VmadPropertyValue value, int structIndex, int index) =>
        new(value.Kind, name, structIndex, index, type, 1, value);

    static VmadPropertyInfo Many(string name, byte type, VmadValueKind kind, uint declared, params VmadElementInfo[] rows) =>
        new(name, type, 1, Plain, declared) { ElementKind = kind, Elements = rows };

    static DialogueInfoResult Present(VmadScriptInfo? script, params InfoFragment[] rows) =>
        new(null, null, null, InfoFragmentState.Present, null, 3, 3, rows, script);

    static VmadScriptInfo Script(bool unsupported, params VmadPropertyInfo[] properties) =>
        new("F_Info", 0, properties) { Unsupported = unsupported };

    static VmadScriptInfo Script(params VmadPropertyInfo[] properties) => Script(false, properties);

    static DialogueInfoResult State(InfoFragmentState state, string? failure = null) =>
        new(null, null, null, state, failure, 0, 0, [], null);

    [Fact]
    public void BothRowsAreCarriedInBeginThenEndOrderWithTheirScriptAndFunction()
    {
        var parse = InfoVmad.Of(Present(Script(), new InfoFragment(InfoFragmentKind.OnBegin, 0, "F_Info", "Fragment_Begin"),
            new InfoFragment(InfoFragmentKind.OnEnd, 0, "F_Info", "Fragment_End")), Resolve);
        Assert.Null(parse.Failure);
        Assert.Equal("F_Info", parse.Script);
        Assert.Equal([new InfoFragmentRow(false, "F_Info", "Fragment_Begin"), new InfoFragmentRow(true, "F_Info", "Fragment_End")], parse.Rows);
    }

    [Fact]
    public void AnEndOnlyResultYieldsOneEndRow()
    {
        var parse = InfoVmad.Of(Present(Script(), new InfoFragment(InfoFragmentKind.OnEnd, 0, "F_Info", "Fragment_End")), Resolve);
        Assert.Equal([new InfoFragmentRow(true, "F_Info", "Fragment_End")], parse.Rows);
    }

    [Fact]
    public void ScalarPropertiesKeepTheirValuesAndAnAliasBoundObjectCarriesNoForm()
    {
        var parse = InfoVmad.Of(Present(Script(
            Scalar("pForm", 1, Form(0x00ABCD, -1)),
            Scalar("pAlias", 1, Form(0, 3)),
            Scalar("pNone", 1, Form(0, -1)),
            Scalar("pText", 2, Text("hello")),
            Scalar("pInt", 3, Int(-7)),
            Scalar("pFloat", 4, Real(1.5f)),
            Scalar("pBool", 5, Bool(true)))), Resolve);
        Assert.Null(parse.Failure);
        var byName = parse.Properties.ToDictionary(p => p.Name);
        Assert.Equal("00ABCD:Quests.esp", byName["pForm"].Object);
        Assert.False(byName["pForm"].HasAlias);
        Assert.True(byName["pAlias"].HasAlias);
        Assert.Null(byName["pAlias"].Object);
        Assert.False(byName["pNone"].HasAlias);
        Assert.Null(byName["pNone"].Object);
        Assert.Equal("hello", byName["pText"].Text);
        Assert.Equal(-7, byName["pInt"].Integer);
        Assert.Equal(1.5, byName["pFloat"].Real);
        Assert.True(byName["pBool"].Boolean);
    }

    [Fact]
    public void AnObjectThatDoesNotResolveIsCarriedAsNoObject()
    {
        var parse = InfoVmad.Of(Present(Script(Scalar("pGone", 1, Form(0x123456, -1)))), _ => null);
        Assert.Null(Assert.Single(parse.Properties).Object);
    }

    [Fact]
    public void ArraysCarryTheirElementTypeAndEveryElement()
    {
        var parse = InfoVmad.Of(Present(Script(
            Many("pInts", 13, VmadValueKind.Int, 2, Item(Int(4), 0), Item(Int(5), 1)),
            Many("pForms", 11, VmadValueKind.Object, 2, Item(Form(0x10, -1), 0), Item(Form(0x20, 2), 1)),
            Many("pText", 12, VmadValueKind.Text, 1, Item(Text("a"), 0)),
            Many("pFloats", 14, VmadValueKind.Real, 1, Item(Real(2.5f), 0)),
            Many("pBools", 15, VmadValueKind.Bool, 1, Item(Bool(true), 0)),
            Many("pEmpty", 13, VmadValueKind.None, 0))), Resolve);
        var byName = parse.Properties.ToDictionary(p => p.Name);
        Assert.Equal("Array", byName["pInts"].Type);
        Assert.Equal("Int", byName["pInts"].ElementType);
        Assert.Equal(2, byName["pInts"].ElementCount);
        Assert.Equal([4L, 5L], byName["pInts"].Elements!.Select(e => e.Integer!.Value));
        Assert.Equal("Object", byName["pForms"].ElementType);
        Assert.Equal(["000010:Quests.esp", "000020:Quests.esp"], byName["pForms"].Elements!.Select(e => e.Object!));
        Assert.Equal([false, true], byName["pForms"].Elements!.Select(e => e.HasAlias));
        Assert.Equal("a", Assert.Single(byName["pText"].Elements!).Text);
        Assert.Equal(2.5, Assert.Single(byName["pFloats"].Elements!).Real);
        Assert.True(Assert.Single(byName["pBools"].Elements!).Boolean);
        Assert.Equal(0, byName["pEmpty"].ElementCount);
        Assert.Empty(byName["pEmpty"].Elements!);
    }

    [Fact]
    public void AnArrayWithFewerRowsThanDeclaredIsRefusedInsteadOfBoundShort()
    {
        var parse = InfoVmad.Of(Present(Script(Many("pInts", 13, VmadValueKind.Int, 3, Item(Int(4), 0)))), Resolve);
        Assert.StartsWith("unsupported", Assert.Single(parse.Properties).Type);
    }

    [Fact]
    public void AStructCarriesItsMembersByName()
    {
        var parse = InfoVmad.Of(Present(Script(
            Many("pStruct", 7, VmadValueKind.StructMember, 2, Member("m", 3, Int(9), 0, 0), Member("o", 1, Form(0x30, -1), 0, 1)))), Resolve);
        var shape = Assert.Single(parse.Properties);
        Assert.Equal("Struct", shape.Type);
        Assert.Equal(2, shape.ElementCount);
        Assert.Equal(["m", "o"], shape.Members!.Select(m => m.Name));
        Assert.Equal(9, shape.Members![0].Integer);
        Assert.Equal("000030:Quests.esp", shape.Members![1].Object);
    }

    [Fact]
    public void AStructWhoseMembersWereLostIsRefused()
    {
        var parse = InfoVmad.Of(Present(Script(true, Many("pStruct", 7, VmadValueKind.None, 2))), Resolve);
        Assert.StartsWith("unsupported", Assert.Single(parse.Properties).Type);
    }

    [Fact]
    public void AStructArrayGroupsRowsByStructIndexAndRebuildsAnEmptyStruct()
    {
        var parse = InfoVmad.Of(Present(Script(Many("pStructs", 17, VmadValueKind.StructMember, 3,
            Member("a", 3, Int(1), 0, 0), Member("b", 2, Text("x"), 0, 1), Member("a", 3, Int(2), 2, 2)))), Resolve);
        var shape = Assert.Single(parse.Properties);
        Assert.Equal("Array", shape.Type);
        Assert.Equal("Struct", shape.ElementType);
        Assert.Equal(3, shape.ElementCount);
        Assert.Equal([2, 0, 1], shape.Elements!.Select(e => e.Members!.Count));
        Assert.All(shape.Elements!, e => { Assert.Equal("", e.Name); Assert.Equal("Struct", e.Type); });
        Assert.Equal("x", shape.Elements![0].Members![1].Text);
        Assert.Equal(2, shape.Elements![2].Members![0].Integer);
    }

    [Fact]
    public void AStructArrayWithNoRowsIsEmptyStructsUnlessTheReadWasUnsupported()
    {
        var empty = Assert.Single(InfoVmad.Of(Present(Script(Many("pStructs", 17, VmadValueKind.None, 2))), Resolve).Properties);
        Assert.Equal(2, empty.ElementCount);
        Assert.All(empty.Elements!, e => Assert.Empty(e.Members!));
        var lost = Assert.Single(InfoVmad.Of(Present(Script(true, Many("pStructs", 17, VmadValueKind.None, 2))), Resolve).Properties);
        Assert.StartsWith("unsupported", lost.Type);
    }

    [Fact]
    public void AStructRowOutsideTheDeclaredCountIsRefused()
    {
        var parse = InfoVmad.Of(Present(Script(Many("pStructs", 17, VmadValueKind.StructMember, 1, Member("a", 3, Int(1), 4, 0)))), Resolve);
        Assert.StartsWith("unsupported", Assert.Single(parse.Properties).Type);
    }

    [Fact]
    public void AVariableAndAnArrayOfVariableAreCarriedAsUnsupportedAndOtherPropertiesStayBindable()
    {
        var parse = InfoVmad.Of(Present(Script(
            Scalar("v", 6, Plain), Many("av", 16, VmadValueKind.None, 0), Scalar("keep", 3, Int(2)))), Resolve);
        Assert.Null(parse.Failure);
        Assert.StartsWith("unsupported property type 6", parse.Properties[0].Type);
        Assert.StartsWith("unsupported property type 16", parse.Properties[1].Type);
        Assert.Equal("Int", parse.Properties[2].Type);
    }

    [Fact]
    public void AnUnknownPropertyTypeFailsTheParse()
    {
        var parse = InfoVmad.Of(Present(Script(Scalar("bad", 9, Plain))), Resolve);
        Assert.Contains("unknown property type 9 on bad", parse.Failure);
        Assert.Empty(parse.Rows);
    }

    [Fact]
    public void OnlyTheFragmentScriptPropertiesAreReturned()
    {
        var parse = InfoVmad.Of(Present(Script(Scalar("keep", 3, Int(2)))), Resolve);
        Assert.Equal(["keep"], parse.Properties.Select(p => p.Name));
    }

    [Fact]
    public void AnUnreadableSectionFailsWithRadiclesReasonAndCarriesNoRows()
    {
        var parse = InfoVmad.Of(State(InfoFragmentState.Unreadable, "the VMAD subrecord ends early"), Resolve);
        Assert.Equal("the VMAD subrecord ends early", parse.Failure);
        Assert.Empty(parse.Rows);
        Assert.Contains("unreadable", InfoVmad.Of(State(InfoFragmentState.Unreadable), Resolve).Failure);
    }

    [Fact]
    public void AVmadWithNoFragmentSectionHasNoRowsAndNoFailure()
    {
        foreach (var state in new[] { InfoFragmentState.NoSection, InfoFragmentState.NoVmad })
        {
            var parse = InfoVmad.Of(State(state), Resolve);
            Assert.Null(parse.Failure);
            Assert.Null(parse.Script);
            Assert.Empty(parse.Rows);
            Assert.Empty(parse.Properties);
        }
    }

    [Fact]
    public void APresentStateWithNoScriptFailsInsteadOfThrowing()
    {
        Assert.NotNull(InfoVmad.Of(Present(null), Resolve).Failure);
    }
}
