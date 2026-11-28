using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Combat;

public readonly record struct ControllerActionSnapshot(float ActionPoints, float RecoveryDelay, bool Exhausted);

public static class ControllerActionSave
{
    public const string SectionName = "player.controller-ap.json";
    private const int Version = 1;

    public static IReadOnlyDictionary<string, byte[]> Sections(ControllerActionSnapshot? snapshot)
    {
        if (snapshot is not { } state) return new Dictionary<string, byte[]>();
        var root = new JsonObject { ["version"] = Version, ["actionPoints"] = state.ActionPoints, ["recoveryDelay"] = state.RecoveryDelay, ["exhausted"] = state.Exhausted };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    public static ControllerActionSnapshot? Read(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(SectionName, out var payload)) return null;
        try
        {
            var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("controller action state is not an object");
            if (root["version"]?.GetValue<int>() != Version) throw new FormatException("controller action state has an unknown version");
            var ap = root["actionPoints"]?.GetValue<float>() ?? throw new FormatException("controller action points are missing");
            var delay = root["recoveryDelay"]?.GetValue<float>() ?? throw new FormatException("controller action recovery delay is missing");
            var exhausted = root["exhausted"]?.GetValue<bool>() ?? throw new FormatException("controller exhaustion state is missing");
            if (!float.IsFinite(ap) || ap < 0 || !float.IsFinite(delay) || delay < 0) throw new FormatException("controller action state must be finite and non-negative");
            return new ControllerActionSnapshot(ap, delay, exhausted);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new FormatException("controller action state is malformed", ex);
        }
    }
}
