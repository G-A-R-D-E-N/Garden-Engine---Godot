using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.MainMenu;

// Requires: main thread only; callers pass a monotonic clock in seconds.
public sealed class PlayClock
{
    public const string SectionName = "player.playtime.json";
    public const int CurrentVersion = 1;
    public const double MaxSeconds = 1e9;
    public const string DefaultCharacterName = "Sole Survivor";

    public static PlayClock Current { get; } = new();

    private double _carried;
    private double _startedAt;

    public void Start(double now, double carriedSeconds = 0)
    {
        _carried = double.IsFinite(carriedSeconds) ? Math.Clamp(carriedSeconds, 0, MaxSeconds) : 0;
        _startedAt = now;
    }

    public TimeSpan Elapsed(double now) => TimeSpan.FromSeconds(Math.Min(MaxSeconds, _carried + Math.Max(0, now - _startedAt)));

    public IReadOnlyDictionary<string, byte[]> Sections(double now) =>
        new Dictionary<string, byte[]>
        {
            [SectionName] = Encoding.UTF8.GetBytes(new JsonObject { ["version"] = CurrentVersion, ["seconds"] = Elapsed(now).TotalSeconds }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" })),
        };

    // Invariant: null means the save has no section (made before the clock existed), so the clock restarts from zero.
    public static double? Read(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(SectionName, out var payload)) return null;
        JsonObject root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(payload), documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) as JsonObject
                ?? throw new FormatException("the playtime section must be a JSON object");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the playtime section is not valid JSON: {ex.Message}", ex);
        }
        try
        {
            if ((root["version"] ?? throw new FormatException("missing 'version'")).GetValue<int>() != CurrentVersion) throw new FormatException("the playtime section version is not supported");
            var seconds = (root["seconds"] ?? throw new FormatException("missing 'seconds'")).GetValue<double>();
            return double.IsFinite(seconds) && seconds >= 0 && seconds <= MaxSeconds ? seconds : throw new FormatException("the playtime must be between zero and a billion seconds");
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException($"the playtime section has the wrong type: {ex.Message}", ex);
        }
    }
}
