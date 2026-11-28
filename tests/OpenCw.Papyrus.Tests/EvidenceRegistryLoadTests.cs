using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class EvidenceRegistryLoadTests
{
    private static string Json(string rows) => $"{{\"schema\":\"{EvidenceRegistry.SchemaName}\",\"rows\":{rows}}}";

    private static string Row(string key, string status) => $"{{\"key\":\"{key}\",\"status\":\"{status}\"}}";

    [Theory]
    [InlineData("null", "the evidence registry was empty")]
    [InlineData("[]", "the evidence registry must be a JSON object")]
    [InlineData("\"text\"", "the evidence registry must be a JSON object")]
    [InlineData("{\"rows\":[]}", "the evidence registry schema must be garden.papyrus.evidence-registry")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\"}", "the evidence registry must carry a rows array")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":{}}", "the evidence registry must carry a rows array")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[]}", "the evidence registry has no rows")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[1,\"x\",[]]}", "the evidence registry has no rows")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{\"status\":\"PROVEN\"}]}", "an evidence row has no key")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{\"key\":\"a\"}]}", "unknown evidence status ")]
    [InlineData("{\"schema\":\"garden.papyrus.evidence-registry\",\"rows\":[{\"key\":\"a\",\"status\":\"proven\"}]}", "unknown evidence status proven")]
    public void ALoadThatCannotBuildARegistryNamesWhatIsWrong(string json, string message)
    {
        var error = Assert.Throws<FormatException>(() => EvidenceRegistry.Load(json));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void TextThatIsNotJsonIsWrappedWithItsParseError()
    {
        var error = Assert.Throws<FormatException>(() => EvidenceRegistry.Load("{"));

        Assert.Equal("the evidence registry is not valid JSON", error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public void EveryStatusNameMapsToItsStatus()
    {
        var registry = EvidenceRegistry.Load(Json("[" + string.Join(",",
            Row("p", "PROVEN"), Row("u", "UNPROVEN"), Row("c", "CONTRADICTED"), Row("a", "APPROXIMATED"), Row("e", "ENGINE_POLICY")) + "]"));

        Assert.Equal(5, registry.Count);
        Assert.Equal(EvidenceStatus.Proven, registry.StatusFor("p"));
        Assert.Equal(EvidenceStatus.Unproven, registry.StatusFor("u"));
        Assert.Equal(EvidenceStatus.Contradicted, registry.StatusFor("c"));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor("a"));
        Assert.Equal(EvidenceStatus.EnginePolicy, registry.StatusFor("e"));
    }

    [Fact]
    public void ARowThatIsNotAnObjectIsSkippedAndTheLastDuplicateKeyWins()
    {
        var registry = EvidenceRegistry.Load(Json("[7," + Row("k", "PROVEN") + ",\"x\"," + Row("k", "CONTRADICTED") + "]"));

        Assert.Equal(1, registry.Count);
        Assert.Equal(EvidenceStatus.Contradicted, registry.StatusFor("k"));
    }

    [Fact]
    public void KeysCompareOrdinallyAndAnAbsentKeyHasNoStatus()
    {
        var registry = EvidenceRegistry.Load(Json("[" + Row("Key", "PROVEN") + "]"));

        Assert.True(registry.Has("Key"));
        Assert.False(registry.Has("key"));
        Assert.True(registry.TryStatus("Key", out var status));
        Assert.Equal(EvidenceStatus.Proven, status);
        Assert.False(registry.TryStatus("key", out var absent));
        Assert.Equal(default, absent);
        var error = Assert.Throws<KeyNotFoundException>(() => registry.StatusFor("key"));
        Assert.Equal("the evidence registry has no row for key", error.Message);
    }
}
