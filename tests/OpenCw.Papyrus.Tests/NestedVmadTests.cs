using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Papyrus.Vmad;
using Radicle.Interop;
using InfoFragmentState = Radicle.Interop.Native.InfoFragmentState;
using VmadValueKind = Radicle.Interop.Native.VmadValueKind;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class NestedVmadTests
{
    private static readonly PapyrusOwnerId Owner = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Ref:Probe.esp");

    static string? Resolve(uint raw) => raw == 0 ? null : $"{raw:X6}:Quests.esp";

    static VmadPropertyValue Plain => new(VmadValueKind.None, 0, 0, null, 0, 0f, false);

    static VmadPropertyValue Form(uint form) => Plain with { Kind = VmadValueKind.Object, ObjectFormId = form, Alias = -1 };

    static VmadPropertyValue Text(string text) => Plain with { Kind = VmadValueKind.Text, Text = text };

    static VmadPropertyValue Int(int value) => Plain with { Kind = VmadValueKind.Int, Integer = value };

    static VmadElementInfo Leaf(string name, byte type, VmadPropertyValue value, int structIndex, int index) =>
        new(value.Kind, name, structIndex, index, type, 1, value);

    static VmadElementInfo Node(string name, byte type, int structIndex, int index, params VmadElementInfo[] children) =>
        new(VmadValueKind.None, name, structIndex, index, type, 1, Plain) { Children = children };

    static VmadPropertyInfo Many(string name, byte type, uint declared, params VmadElementInfo[] rows) =>
        new(name, type, 1, Plain, declared) { ElementKind = VmadValueKind.StructMember, Elements = rows };

    static VmadPropertyInfo Scalar(string name, byte type, VmadPropertyValue value) => new(name, type, 1, value, 0);

    static VmadScriptInfo Script(string name, params VmadPropertyInfo[] properties) => new(name, 0, properties);

    static VmadElementInfo Inner() => Node("Inner", 7, 0, 0, Leaf("Depth", 3, Int(2), 0, 0), Leaf("Target", 1, Form(0x30), 0, 1));

    static VmadElementInfo Counts() => Node("Counts", 13, 0, 1, Leaf("", 3, Int(4), 0, 0), Leaf("", 3, Int(9), 0, 1));

    static string Json(BaseScripts.Result result) => JsonSerializer.Serialize(result.Scripts!,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    static DialogueInfoResult Present(VmadScriptInfo script) =>
        new(null, null, null, InfoFragmentState.Present, null, 3, 3, [], script);

    [Fact]
    public void ABaseStructWithANestedStructAndAScalarArrayBecomesNestedMembers()
    {
        var result = BaseScripts.Map([Script("S", Many("Config", 7, 2, Inner(), Counts()))], Resolve);
        Assert.False(result.Incomplete);
        var config = Assert.Single(Assert.Single(result.Scripts!).Properties);
        Assert.Equal("Struct", config.Type);
        var inner = config.Members![0];
        Assert.Equal(("Inner", "Struct"), (inner.Name, inner.Type));
        Assert.Equal(["2", "000030:Quests.esp"], inner.Members!.Select(member => member.Value!));
        var counts = config.Members[1];
        Assert.Equal(("Array", "Int"), (counts.Type, counts.ElementType));
        Assert.Equal(["4", "9"], counts.Elements!.Select(element => element.Value!));
    }

    [Fact]
    public void ABaseStructArrayCarriesAStructThatHoldsAStructAndAStructArray()
    {
        var items = Node("Items", 17, 1, 1,
            Node("", 7, 0, 0, Leaf("a", 3, Int(1), 0, 0)),
            Node("", 7, 0, 1, Leaf("a", 3, Int(2), 0, 0)));
        var result = BaseScripts.Map([Script("S", Many("List", 17, 2,
            Node("Inner", 7, 0, 0, Leaf("Depth", 3, Int(5), 0, 0)),
            Leaf("Name", 2, Text("t"), 1, 1),
            items))], Resolve);
        var list = Assert.Single(Assert.Single(result.Scripts!).Properties);
        Assert.Equal(("Array", "Struct"), (list.Type, list.ElementType));
        Assert.Equal(2, list.Elements!.Count);
        Assert.Equal("Inner", list.Elements[0].Members![0].Name);
        Assert.Equal("5", list.Elements[0].Members![0].Members![0].Value);
        var nested = list.Elements[1].Members!.Single(member => member.Name == "Items");
        Assert.Equal(("Array", "Struct"), (nested.Type, nested.ElementType));
        Assert.Equal(["1", "2"], nested.Elements!.Select(item => item.Members![0].Value!));
    }

    [Fact]
    public void ANestedMemberWithNoDecodedChildrenIsRefusedByName()
    {
        var result = BaseScripts.Map([Script("S", Many("Config", 7, 2,
            Node("Empty", 7, 0, 0), Node("Missing", 13, 0, 1) with { Children = null }))], Resolve);
        var members = Assert.Single(Assert.Single(result.Scripts!).Properties).Members!;
        Assert.All(members, member => Assert.NotNull(member.Unsupported));
        Assert.Equal(["Empty", "Missing"], members.Select(member => member.Name));
    }

    [Fact]
    public void ANestedValueSurvivesTheMetadataRoundTripAsNestedScriptValues()
    {
        var result = BaseScripts.Map([Script("First", Many("Config", 7, 2, Inner(), Counts()))], Resolve);
        var attachment = Assert.Single(ScriptAttachments.Parse(Json(result)));
        var config = Assert.Single(attachment.Values);
        Assert.True(config.IsSupported);
        Assert.Equal(ScriptValueKind.Struct, config.Kind);
        Assert.Equal(ScriptValueKind.Struct, config.Members![0].Kind);
        Assert.Equal(["4", "9"], config.Members[1].Elements!.Select(element => element.Value!));
    }

    [Fact]
    public void AStopNamesTheScriptAndPropertyInsteadOfMarkingTheOwnerIncomplete()
    {
        var stopped = Script("Second", Scalar("Count", 3, Int(7))) with
        {
            Unsupported = true,
            Stop = new VmadReadStop(1, "Hard", 6, 4),
        };
        var whole = Script("First", Scalar("Count", 3, Int(1))) with { Unsupported = true, Stop = stopped.Stop };
        var result = BaseScripts.Map([whole, stopped], Resolve);
        Assert.False(result.Incomplete);
        Assert.Equal(["First", "Second"], result.Scripts!.Select(script => script.Name));
        Assert.Equal(["Count"], result.Scripts[0].Properties.Select(property => property.Name));
        var hard = result.Scripts[1].Properties[^1];
        Assert.Equal(("Hard", "Unsupported"), (hard.Name, hard.Type));
        Assert.Contains("type 6", hard.Unsupported);
        Assert.Contains("2 later script(s)", hard.Unsupported);
    }

    [Fact]
    public void AnUnsupportedReadWithNoUsableStopStillMakesTheOwnerIncomplete()
    {
        Assert.True(BaseScripts.Map([Script("S") with { Unsupported = true }], Resolve).Incomplete);
        Assert.True(BaseScripts.Map([Script("S") with { Unsupported = true, Stop = new VmadReadStop(3, "Hard", 6, 4) }], Resolve).Incomplete);
        Assert.False(BaseScripts.Map([Script("S")], Resolve).Incomplete);
    }

    [Fact]
    public void AStoppedScriptAttachesWithOnlyTheStoppedPropertyUnboundAndNamed()
    {
        using var session = GameSessionTests.Session();
        var stopped = Script("First", Scalar("Count", 3, Int(7))) with
        {
            Unsupported = true,
            Stop = new VmadReadStop(0, "Hard", 6, 1),
        };
        var read = BaseScripts.Map([stopped], Resolve);
        var result = session.AttachReference("Cell", Owner, Json(read), sourceUnsupported: read.Incomplete);
        Assert.Null(result.Refusal);
        var instance = Assert.Single(result.Instances);
        Assert.Equal(PapyrusValue.FromInt(7), instance.Properties["Count"]);
        Assert.False(instance.Properties.ContainsKey("Hard"));
        var issue = Assert.Single(result.Issues);
        Assert.Equal(("First", "Hard"), (issue.Script, issue.Property));
        Assert.Contains("stopped reading", issue.Reason);
        Assert.Equal((0, 1), (session.RefusedReferences, session.PartialReferences));
    }

    [Fact]
    public void AnInfoStructHoldingAStructAndAnArrayBindsAsOneNestedValue()
    {
        var parse = InfoVmad.Of(Present(Script("F_Info", Many("Config", 7, 2, Inner(), Counts()))), Resolve);
        Assert.Null(parse.Failure);
        var config = Assert.Single(parse.Properties);
        Assert.Equal(2, config.ElementCount);
        Assert.Equal(["Depth", "Target"], config.Members![0].Members!.Select(member => member.Name));
        Assert.Equal("000030:Quests.esp", config.Members[0].Members![1].Object);
        Assert.Equal([4L, 9L], config.Members[1].Elements!.Select(element => element.Integer!.Value));

        var bound = Assert.Single(VmadValueBinder.Bind(Owner, parse.Properties).Properties);
        Assert.Equal(BoundKind.Struct, bound.Kind);
        Assert.Equal(2, bound.Value.Members!["Inner"].Members!["Depth"].Integer);
        Assert.Equal(new long[] { 4, 9 }, bound.Value.Members["Counts"].Elements!.Select(element => element.Integer).ToArray());
    }

    [Fact]
    public void AnInfoStructArrayCarriesANestedStructArrayMember()
    {
        var items = Node("Items", 17, 0, 0, Node("", 7, 0, 0, Leaf("a", 3, Int(1), 0, 0)), Node("", 7, 0, 1, Leaf("a", 3, Int(2), 0, 0)));
        var parse = InfoVmad.Of(Present(Script("F_Info", Many("List", 17, 1, items))), Resolve);
        Assert.Null(parse.Failure);
        var list = Assert.Single(parse.Properties);
        Assert.Equal(("Array", "Struct"), (list.Type, list.ElementType));
        var nested = Assert.Single(Assert.Single(list.Elements!).Members!);
        Assert.Equal(("Items", "Array", "Struct"), (nested.Name, nested.Type, nested.ElementType));
        Assert.Equal(2, nested.ElementCount);
        Assert.Equal([1L, 2L], nested.Elements!.Select(element => element.Members![0].Integer!.Value));
    }

    [Fact]
    public void AnInfoNestedMemberWithoutChildrenRefusesTheWholeProperty()
    {
        var parse = InfoVmad.Of(Present(Script("F_Info", Many("Config", 7, 1, Node("Missing", 13, 0, 0) with { Children = null }))), Resolve);
        Assert.Null(parse.Failure);
        Assert.StartsWith("unsupported", Assert.Single(parse.Properties).Type);
    }

    [Fact]
    public void AnInfoFragmentScriptThatStoppedFailsTheParseWithTheStopNamed()
    {
        var stopped = Script("F_Info") with { Unsupported = true, Stop = new VmadReadStop(0, "Hard", 6, 1) };
        var parse = InfoVmad.Of(Present(stopped), Resolve);
        Assert.Contains("stopped at property Hard", parse.Failure);
    }

    static VmadElementInfo Chain(int levels) => levels == 0
        ? Leaf("leaf", 3, Int(1), 0, 0)
        : Node("n", 7, 0, 0, Chain(levels - 1));

    [Fact]
    public void ABaseStructNestedPastTheBinderCapIsRefusedAtTheDeepestStructByName()
    {
        var fits = Assert.Single(Assert.Single(BaseScripts.Map([Script("S", Many("Config", 7, 1, Chain(15)))], Resolve).Scripts!).Properties);
        var deepest = fits;
        while (deepest.Members is { } members) deepest = members[0];
        Assert.Null(deepest.Unsupported);

        var over = Assert.Single(Assert.Single(BaseScripts.Map([Script("S", Many("Config", 7, 1, Chain(16)))], Resolve).Scripts!).Properties);
        deepest = over;
        while (deepest.Members is { } members) deepest = members[0];
        Assert.Contains("deeper than 16 levels", deepest.Unsupported);
    }

    [Fact]
    public void AnInfoStructNestedPastTheBinderCapRefusesTheProperty()
    {
        var fits = Assert.Single(InfoVmad.Of(Present(Script("F_Info", Many("Config", 7, 1, Chain(15)))), Resolve).Properties);
        Assert.Equal("Struct", fits.Type);
        Assert.Equal(BoundKind.Struct, Assert.Single(VmadValueBinder.Bind(Owner, [fits]).Properties).Kind);

        var over = Assert.Single(InfoVmad.Of(Present(Script("F_Info", Many("Config", 7, 1, Chain(16)))), Resolve).Properties);
        Assert.StartsWith("unsupported", over.Type);
    }
}
