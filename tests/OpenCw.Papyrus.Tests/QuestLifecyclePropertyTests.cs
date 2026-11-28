using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Quest;
using Xunit;
using LifecycleProperty = Radicle.Interop.QuestLifecycleProperty;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestLifecyclePropertyTests
{
    private static QuestStartupValue Value(int? integer = null, int? realBits = null, bool? boolean = null, string? text = null) =>
        new(integer, realBits, boolean, text, null, null, null, null);

    private static QuestStartupProperty Property(string name, byte type, QuestStartupValue value) =>
        new("QuestScript", name, type, 1, value);

    private static QuestStartupInput Input(params QuestStartupProperty[] properties) =>
        new("000800:Quests.esp", true, new[] { "QuestScript" }, 0, 0,
            Metadata: new QuestStartupMetadata(properties, "", Array.Empty<QuestStartupProperty>(),
                Array.Empty<QuestStartupFragment>(), Array.Empty<int>(), Array.Empty<QuestStartupAliasScript>(),
                Array.Empty<QuestStartupProperty>()));

    [Fact]
    public void ScalarPropertiesConvertToLifecyclePropertiesInOrder()
    {
        var input = Input(
            Property("Text", 2, Value(text: "a text")),
            Property("Count", 3, Value(integer: 17)),
            Property("Ratio", 4, Value(realBits: BitConverter.SingleToInt32Bits(1.5f))),
            Property("Flag", 5, Value(boolean: true)));

        Assert.True(QuestStartupBinding.TryLifecycleProperties(input, out var properties));

        Assert.Equal(new List<LifecycleProperty>
        {
            new("QuestScript", "Text", "String", Text: "a text"),
            new("QuestScript", "Count", "Int", Integer: 17),
            new("QuestScript", "Ratio", "Float", Real: 1.5),
            new("QuestScript", "Flag", "Bool", Boolean: true),
        }, properties);
    }

    [Fact]
    public void NoMetadataMeansNoLifecyclePropertiesAndTheDirectPath()
    {
        var input = new QuestStartupInput("000800:Quests.esp", true, new[] { "QuestScript" }, 0, 0);

        Assert.False(QuestStartupBinding.TryLifecycleProperties(input, out var properties));

        Assert.Empty(properties);
    }

    [Fact]
    public void AnEmptyPropertyListStillTakesTheLifecyclePath()
    {
        Assert.True(QuestStartupBinding.TryLifecycleProperties(Input(), out var properties));

        Assert.Empty(properties);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(17)]
    public void ObjectArrayAndStructPropertiesRefuseTheLifecyclePath(byte type)
    {
        var input = Input(Property("Count", 3, Value(integer: 1)), Property("Other", type, Value(integer: 2)));

        Assert.False(QuestStartupBinding.TryLifecycleProperties(input, out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AScalarPropertyWhoseValueIsMissingRefusesTheLifecyclePath(byte type)
    {
        Assert.False(QuestStartupBinding.TryLifecycleProperties(Input(Property("Missing", type, Value())), out _));
    }
}
