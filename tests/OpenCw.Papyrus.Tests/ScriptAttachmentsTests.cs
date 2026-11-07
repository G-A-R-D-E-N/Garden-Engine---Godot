using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using OpenCommonwealth.Services.Papyrus;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class ScriptAttachmentsTests
{
    private const string ProducerJson = """
    [
      {"name":"QuestScript","properties":[
        {"name":"Count","type":"Int","value":"7"},
        {"name":"Speed","type":"Float","value":"1.5"},
        {"name":"Enabled","type":"Bool","value":"True"},
        {"name":"Label","type":"String","value":"hello"},
        {"name":"Target","type":"Object","value":"00012345:Fallout4.esm"}
      ]}
    ]
    """;

    private static string WithCulture(string culture, Func<string> body)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ParsesTypedValues()
    {
        var attachments = ScriptAttachments.Parse(ProducerJson);

        var script = Assert.Single(attachments);
        Assert.Equal("QuestScript", script.Name);
        Assert.Equal(5, script.Values.Count);
        Assert.Equal(ScriptValueKind.Int, script.Values[0].Kind);
        Assert.Equal("7", script.Values[0].Value);
        Assert.Equal(ScriptValueKind.Float, script.Values[1].Kind);
        Assert.Equal("1.5", script.Values[1].Value);
        Assert.Equal(ScriptValueKind.Bool, script.Values[2].Kind);
        Assert.Equal("true", script.Values[2].Value);
        Assert.Equal(ScriptValueKind.String, script.Values[3].Kind);
        Assert.Equal("hello", script.Values[3].Value);
        Assert.Equal(ScriptValueKind.Object, script.Values[4].Kind);
        Assert.Equal("00012345:Fallout4.esm", script.Values[4].Value);
        Assert.All(script.Values, value => Assert.True(value.IsSupported));
        Assert.Equal(0, ScriptAttachments.CountUnsupportedValues(attachments));
    }

    [Fact]
    public void MetadataRoundTripsEveryValue()
    {
        var attachments = ScriptAttachments.Parse(ProducerJson);
        var metadata = ScriptAttachments.ToMetadataJson(attachments);
        var again = ScriptAttachments.Parse(metadata);

        Assert.Equal(attachments.Count, again.Count);
        for (var index = 0; index < attachments.Count; ++index)
        {
            Assert.Equal(attachments[index].Name, again[index].Name);
            Assert.Equal(attachments[index].Values.Count, again[index].Values.Count);
            for (var value = 0; value < attachments[index].Values.Count; ++value)
            {
                Assert.Equal(attachments[index].Values[value], again[index].Values[value]);
            }
        }
    }

    [Fact]
    public void UnsupportedKindsAreExplicitAndSurviveTheRoundTrip()
    {
        const string json = """
        [{"name":"QuestScript","properties":[
          {"name":"Array","type":"Variable","value":"[1, 2]"},
          {"name":"Weird","type":"ScriptPropertyStruct","value":"{}"}
        ]}]
        """;

        var attachments = ScriptAttachments.Parse(json);
        var script = Assert.Single(attachments);
        Assert.All(script.Values, value =>
        {
            Assert.Equal(ScriptValueKind.Unsupported, value.Kind);
            Assert.Null(value.Value);
            Assert.False(string.IsNullOrWhiteSpace(value.Reason));
        });
        Assert.Equal(2, ScriptAttachments.CountUnsupportedValues(attachments));

        var metadata = ScriptAttachments.ToMetadataJson(attachments);
        Assert.Contains("unsupported", metadata, StringComparison.Ordinal);

        var again = ScriptAttachments.Parse(metadata);
        Assert.Equal(script.Values, Assert.Single(again).Values);
    }

    [Fact]
    public void DuplicateScriptsOnOneOwnerAreCounted()
    {
        const string json = """
        [
          {"name":"Same","properties":[]},
          {"name":"Other","properties":[]},
          {"name":"Same","properties":[]}
        ]
        """;

        var attachments = ScriptAttachments.Parse(json);
        Assert.Equal(3, attachments.Count);
        Assert.Equal(1, ScriptAttachments.CountDuplicateNames(attachments));
        Assert.Equal(0, ScriptAttachments.CountDuplicateNames(ScriptAttachments.Parse(ProducerJson)));
    }

    [Fact]
    public void CaseVariantsCountAsOneDuplicateGroupWithoutRenamingScripts()
    {
        const string json = """
        [{"name":"DoorScript","properties":[]},{"name":"doorscript","properties":[]},
         {"name":"DOORSCRIPT","properties":[]},{"name":"Other","properties":[]}]
        """;
        var attachments = ScriptAttachments.Parse(json);
        var expected = new[] { "DoorScript", "doorscript", "DOORSCRIPT", "Other" };
        Assert.Equal(expected, attachments.Select(script => script.Name));
        Assert.Equal(1, ScriptAttachments.CountDuplicateNames(attachments));
        var roundTrip = ScriptAttachments.Parse(ScriptAttachments.ToMetadataJson(attachments));
        Assert.Equal(expected, roundTrip.Select(script => script.Name));
        Assert.Equal(1, ScriptAttachments.CountDuplicateNames(roundTrip));
    }

    [Fact]
    public void LocaleDoesNotChangeFloats()
    {
        var invariant = WithCulture("en-US", () =>
            ScriptAttachments.ToMetadataJson(ScriptAttachments.Parse(ProducerJson)));
        var comma = WithCulture("de-DE", () =>
            ScriptAttachments.ToMetadataJson(ScriptAttachments.Parse(ProducerJson)));

        Assert.Equal(invariant, comma);
        Assert.Contains("\"1.5\"", invariant, StringComparison.Ordinal);
        Assert.DoesNotContain("1,5", invariant, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommaDecimalProducerValueIsAcceptedAndNormalized()
    {
        const string json = """
        [{"name":"S","properties":[{"name":"Speed","type":"Float","value":"1,5"}]}]
        """;

        var metadata = WithCulture("de-DE", () =>
            ScriptAttachments.ToMetadataJson(ScriptAttachments.Parse(json)));

        Assert.Contains("\"1.5\"", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("1,5", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnparseableFloatIsUnsupportedRatherThanZero()
    {
        const string json = """
        [{"name":"S","properties":[{"name":"Speed","type":"Float","value":"not-a-number"}]}]
        """;

        var value = Assert.Single(Assert.Single(ScriptAttachments.Parse(json)).Values);
        Assert.Equal(ScriptValueKind.Unsupported, value.Kind);
        Assert.Null(value.Value);
        Assert.Contains("not-a-number", value.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankInputHasNoAttachments()
    {
        Assert.Empty(ScriptAttachments.Parse(null));
        Assert.Empty(ScriptAttachments.Parse(""));
        Assert.Empty(ScriptAttachments.Parse("   "));
        Assert.Equal("[]", ScriptAttachments.ToMetadataJson(Array.Empty<ScriptAttachment>()));
    }

    [Fact]
    public void NaNTextIsUnsupportedAndItsRefusalSurvivesMetadataRoundTrip()
    {
        const string json = """
        [{"name":"S","properties":[{"name":"Speed","type":"Float","value":"NaN"}]}]
        """;
        Assert.False(ScriptAttachments.TryNormalizeFloat("NaN", out _));
        var attachments = ScriptAttachments.Parse(json);
        var value = Assert.Single(Assert.Single(attachments).Values);
        Assert.Equal(ScriptValueKind.Unsupported, value.Kind);
        Assert.Null(value.Value);
        Assert.Contains("NaN", value.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, ScriptAttachments.CountUnsupportedValues(attachments));
        Assert.Equal(value, Assert.Single(Assert.Single(ScriptAttachments.Parse(
            ScriptAttachments.ToMetadataJson(attachments))).Values));
    }

    [Fact]
    public void MalformedJsonFailsLoudly()
    {
        Assert.Throws<FormatException>(() => ScriptAttachments.Parse("{ not json"));
        Assert.Throws<FormatException>(() => ScriptAttachments.Parse("{\"name\":\"S\"}"));
    }

    [Fact]
    public void MetadataKeyIsStable()
    {
        Assert.Equal("fo4_scripts", ScriptAttachments.MetadataKey);
    }
}
