using System;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptMetadataShapeTests
{
    [Theory]
    [InlineData("[null]")]
    [InlineData("[42]")]
    [InlineData("[[]]")]
    [InlineData("[{\"name\":null,\"properties\":[]}]")]
    [InlineData("[{\"name\":7,\"properties\":[]}]")]
    [InlineData("[{\"name\":\" \",\"properties\":[]}]")]
    [InlineData("[{\"name\":\"S\"}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":null}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":{}}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":[null]}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":[42]}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":[{\"type\":\"String\",\"value\":\"x\"}]}]")]
    [InlineData("[{\"name\":\"S\",\"properties\":[{\"name\":7,\"type\":\"String\",\"value\":\"x\"}]}]")]
    public void MalformedEntriesCannotDisappearOrBecomePropertyFreeScripts(string json) =>
        Assert.Throws<FormatException>(() => ScriptAttachments.Parse(json));

    [Theory]
    [InlineData("String", "null")]
    [InlineData("String", "{}")]
    [InlineData("String", "[]")]
    [InlineData("String", "7")]
    [InlineData("String", "true")]
    [InlineData("String", null)]
    [InlineData("Object", "7")]
    [InlineData("Object", "true")]
    public void NonTextValuesAreUnsupportedAndTheirReasonsRoundTrip(string type, string? raw)
    {
        var value = raw is null ? "" : ",\"value\":" + raw;
        var json = "[{\"name\":\"S\",\"properties\":[{\"name\":\"Value\",\"type\":\"" + type + "\"" + value + "}]}]";
        AssertUnsupportedRoundTrip(json);
    }

    [Theory]
    [InlineData("String", "\"text\"")]
    [InlineData("Int", "\"7\"")]
    public void AnExplicitProducerRefusalCannotBecomeASupportedValue(string type, string raw)
    {
        var json = "[{\"name\":\"S\",\"properties\":[{\"name\":\"Value\",\"type\":\"" + type +
            "\",\"value\":" + raw + ",\"unsupported\":\"producer refusal\"}]}]";
        AssertUnsupportedRoundTrip(json, "producer refusal");
    }

    private static void AssertUnsupportedRoundTrip(string json, string? reason = null)
    {
        var attachments = ScriptAttachments.Parse(json);
        var property = Assert.Single(Assert.Single(attachments).Values);
        Assert.Equal(ScriptValueKind.Unsupported, property.Kind);
        Assert.Null(property.Value);
        Assert.False(string.IsNullOrWhiteSpace(property.Reason));
        if (reason is not null) Assert.Equal(reason, property.Reason);
        Assert.Equal(1, ScriptAttachments.CountUnsupportedValues(attachments));
        var restored = ScriptAttachments.Parse(ScriptAttachments.ToMetadataJson(attachments));
        Assert.Equal(property, Assert.Single(Assert.Single(restored).Values));
        Assert.Equal(1, ScriptAttachments.CountUnsupportedValues(restored));
    }

    [Theory]
    [InlineData("String", "\"\"", "")]
    [InlineData("String", "\"a\\u0000b\"", "a\0b")]
    [InlineData("Object", "\"000123:Probe.esp\"", "000123:Probe.esp")]
    [InlineData("Int", "7", "7")]
    [InlineData("Bool", "true", "true")]
    [InlineData("Float", "1.5", "1.5")]
    public void ValidEmptyTextAndExistingScalarShapesKeepTheirValues(string type, string raw, string expected)
    {
        var json = "[{\"name\":\"S\",\"properties\":[{\"name\":\"Value\",\"type\":\"" + type + "\",\"value\":" + raw + "}]}]";
        var attachments = ScriptAttachments.Parse(json);
        var value = Assert.Single(Assert.Single(attachments).Values);
        Assert.True(value.IsSupported);
        Assert.Equal(expected, value.Value);
        Assert.Equal(0, ScriptAttachments.CountUnsupportedValues(attachments));
        Assert.Equal(value, Assert.Single(Assert.Single(ScriptAttachments.Parse(ScriptAttachments.ToMetadataJson(attachments))).Values));
    }
}
