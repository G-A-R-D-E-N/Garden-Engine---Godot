using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;

public sealed class RdfException : Exception
{
    public RdfException(string message) : base(message)
    {
    }
}

public sealed class RdfBusyException : Exception
{
    public RdfBusyException() : base("A check run is already in progress. Use checks cancel to stop it.")
    {
    }
}

public sealed record RdfLast(string Status, string? Sha, int Passed, int Total, int Skip, double Seconds, string? At, IReadOnlyList<string> Failing, string? Error = null)
{
    public static readonly RdfLast None = new("NONE", null, 0, 0, 0, 0, null, Array.Empty<string>());

    public string Text => Status == "NONE"
        ? "NONE"
        : Status + " " + Passed.ToString(CultureInfo.InvariantCulture) + "/" + Total.ToString(CultureInfo.InvariantCulture)
            + (Skip > 0 ? " skip " + Skip.ToString(CultureInfo.InvariantCulture) : string.Empty)
            + " " + Seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s"
            + (Error is { Length: > 0 } ? " (" + Error + ")" : string.Empty);
}

public sealed record RdfBox(string Id, string Title, string Kind, IReadOnlyList<string> Tags, IReadOnlyList<string> Tiers, RdfLast Last);

public sealed record RdfList(string ResultsRoot, IReadOnlyList<RdfBox> Boxes);

public sealed record RdfTests(IReadOnlyList<string> Tests, string? Unavailable);

public sealed record RdfEvent(string Kind, string? Name = null, string? Status = null, double Seconds = 0, string? ResultPath = null, string? Line = null, string? Box = null)
{
    public const string Start = "start";
    public const string Phase = "phase";
    public const string Test = "test";
    public const string Done = "done";
    public const string Plan = "plan";
    public const string NoChecksAffected = "No checks affected.";

    public string Describe() => Kind switch
    {
        Start => "Started" + (Box is { Length: > 0 } ? " " + Box : string.Empty) + ".",
        Phase => (Box is { Length: > 0 } ? Box + " phase: " : "Phase: ") + Name,
        Plan => Line ?? "Plan.",
        Test => (Status ?? "?") + " " + Name + " (" + Seconds.ToString("0.##", CultureInfo.InvariantCulture) + "s)",
        Done => Line ?? "Done.",
        _ => Kind,
    };

    public bool IsFailure => Kind switch
    {
        Test => string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase) || string.Equals(Status, "fail", StringComparison.OrdinalIgnoreCase),
        Done => string.Equals(Status, "FAIL", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    public bool IsSkip => Kind == Test && Status is not null && Status.StartsWith("skip", StringComparison.OrdinalIgnoreCase);
}

public static class RdfJson
{
    public static RdfList ParseList(string json)
    {
        using var doc = ParseDocument(json, "-List");
        var root = doc.RootElement;
        var boxes = new List<RdfBox>();
        if (root.TryGetProperty("boxes", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var id = Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var last = item.TryGetProperty("last", out var l) && l.ValueKind == JsonValueKind.Object ? ParseLast(l) : RdfLast.None;
                boxes.Add(new RdfBox(id, Str(item, "title") is { Length: > 0 } t ? t : id, Str(item, "kind") ?? string.Empty,
                    Strings(item, "tags"), Strings(item, "tiers"), last));
            }
        }
        return new RdfList(Str(root, "results_root") ?? string.Empty, boxes);
    }

    public static RdfList ParseResults(string json)
    {
        using var doc = ParseDocument(json, "-Results");
        var root = doc.RootElement;
        var boxes = new List<RdfBox>();
        if (root.TryGetProperty("results", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var id = Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;
                var last = item.TryGetProperty("last", out var l) && l.ValueKind == JsonValueKind.Object ? ParseLast(l) : RdfLast.None;
                boxes.Add(new RdfBox(id, id, string.Empty, Array.Empty<string>(), Array.Empty<string>(), last));
            }
        }
        return new RdfList(Str(root, "results_root") ?? string.Empty, boxes);
    }

    public static RdfTests ParseTests(string json)
    {
        using var doc = ParseDocument(json, "-Tests");
        var root = doc.RootElement;
        if (Str(root, "unavailable") is { } reason)
            return new RdfTests(Array.Empty<string>(), reason);
        if (root.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.False)
            return new RdfTests(Array.Empty<string>(), Str(root, "reason") is { Length: > 0 } why ? why : "unavailable");
        return new RdfTests(Strings(root, "tests"), null);
    }

    public static RdfEvent? ParseEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var text = line.Trim();
        if (text[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var kind = Str(root, "event");
            return kind switch
            {
                RdfEvent.Start => new RdfEvent(kind, Box: Str(root, "box")),
                RdfEvent.Phase => new RdfEvent(kind, Name: Str(root, "name"), Box: Str(root, "box")),
                RdfEvent.Plan => new RdfEvent(kind, Line: PlanLine(root)),
                RdfEvent.Test => Str(root, "name") is { Length: > 0 } name
                    ? new RdfEvent(kind, name, Str(root, "status") ?? "?", Num(root, "seconds"), Box: Str(root, "box"))
                    : null,
                RdfEvent.Done => new RdfEvent(kind, Status: Str(root, "status"), ResultPath: Str(root, "result_path"), Line: Str(root, "line"), Box: Str(root, "box")),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string PlanLine(JsonElement root)
    {
        if (!root.TryGetProperty("boxes", out var boxes) || boxes.ValueKind != JsonValueKind.Array) return "Plan.";
        var planned = Strings(root, "boxes");
        return planned.Count > 0 ? "Plan: " + string.Join(", ", planned) : RdfEvent.NoChecksAffected;
    }

    private static JsonDocument ParseDocument(string json, string what)
    {
        try
        {
            var doc = JsonDocument.Parse(json ?? string.Empty);
            if (doc.RootElement.ValueKind == JsonValueKind.Object) return doc;
            doc.Dispose();
        }
        catch (JsonException)
        {
        }
        throw new RdfException("The launcher returned no JSON object for " + what + ".");
    }

    private static RdfLast ParseLast(JsonElement e) =>
        new(Str(e, "status") ?? "NONE", Str(e, "sha"), (int)Num(e, "passed"), (int)Num(e, "total"), (int)Num(e, "skip"),
            Num(e, "seconds"), Str(e, "at"), Strings(e, "failing"), Str(e, "error"));

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
        && double.IsFinite(d) ? d : 0;

    private static IReadOnlyList<string> Strings(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : Array.Empty<string>();
}
