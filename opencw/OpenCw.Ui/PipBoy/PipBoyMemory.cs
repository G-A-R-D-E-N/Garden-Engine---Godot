using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Requires: main thread only; holds what the player chose in the Pip-Boy so the next menu instance starts from it.
public sealed class PipBoyMemory
{
    public const string SectionName = "ge.pipboy";

    private readonly HashSet<string> _discovered = new(StringComparer.Ordinal);

    public static PipBoyMemory Current { get; } = new();

    public IReadOnlyCollection<string> Discovered => _discovered;

    public bool IsEmpty => TrackedQuest is null && Waypoint is null && RadioStation is null && !RadioMuted && _discovered.Count == 0 && Favourites.Load().Count == 0;

    public bool Discover(string markerId) => _discovered.Add(markerId);

    public string? TrackedQuest { get; private set; }

    public MapWaypoint? Waypoint { get; private set; }

    public string? RadioStation { get; private set; }

    public bool RadioMuted { get; private set; }

    public IFavouriteStore Favourites { get; private set; } = new InMemoryFavouriteStore();

    public void Apply(QuestEvent command)
    {
        if (command.Kind == QuestEventKind.Track) TrackedQuest = command.QuestId;
        else if (TrackedQuest == command.QuestId) TrackedQuest = null;
    }

    public void SetWaypoint(MapWaypoint? waypoint) => Waypoint = waypoint;

    public void Apply(RadioEvent command)
    {
        switch (command.Kind)
        {
            case RadioEventKind.Play: RadioStation = command.StationId; break;
            case RadioEventKind.Stop: RadioStation = null; break;
            case RadioEventKind.Mute: RadioMuted = true; break;
            case RadioEventKind.Unmute: RadioMuted = false; break;
        }
    }

    public void Restore(QuestLogModel quests) => quests.RestoreTracked(TrackedQuest);

    public void Restore(RadioModel radio)
    {
        if (RadioStation is { } id) radio.Play(id);
        radio.SetMuted(RadioMuted);
    }

    public void Clear()
    {
        TrackedQuest = null;
        Waypoint = null;
        RadioStation = null;
        RadioMuted = false;
        _discovered.Clear();
        Favourites = new InMemoryFavouriteStore();
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (IsEmpty) return new Dictionary<string, byte[]>();
        var row = new JsonObject
        {
            ["tracked"] = TrackedQuest,
            ["station"] = RadioStation,
            ["muted"] = RadioMuted,
            ["favourites"] = new JsonArray(Favourites.Load().OrderBy(key => key, StringComparer.Ordinal).Select(key => (JsonNode)key).ToArray()),
            ["discovered"] = new JsonArray(_discovered.OrderBy(key => key, StringComparer.Ordinal).Select(key => (JsonNode)key).ToArray()),
        };
        if (Waypoint is { } point) row["waypoint"] = new JsonObject { ["context"] = point.ContextId, ["x"] = point.X, ["y"] = point.Y };
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(row.ToJsonString()) };
    }

    // Constraint: replaces everything; an absent section empties the memory, and a malformed one throws FormatException before anything changes.
    public void Load(IReadOnlyDictionary<string, byte[]> sections)
    {
        if (!sections.TryGetValue(SectionName, out var payload))
        {
            Clear();
            return;
        }
        string? tracked, station;
        bool muted;
        MapWaypoint? waypoint = null;
        string[] favourites, discovered;
        try
        {
            var row = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("pip-boy memory is not an object");
            tracked = row["tracked"]?.GetValue<string>();
            station = row["station"]?.GetValue<string>();
            muted = row["muted"]?.GetValue<bool>() ?? false;
            favourites = (row["favourites"] as JsonArray ?? throw new FormatException("pip-boy favourites are missing")).Select(node => node!.GetValue<string>()).ToArray();
            discovered = (row["discovered"] as JsonArray ?? throw new FormatException("pip-boy discovered markers are missing")).Select(node => node!.GetValue<string>()).ToArray();
            if (row["waypoint"] is JsonObject point)
                waypoint = new MapWaypoint(point["context"]!.GetValue<string>(), point["x"]!.GetValue<double>(), point["y"]!.GetValue<double>());
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NullReferenceException or FormatException)
        {
            throw new FormatException("pip-boy memory is malformed", ex);
        }
        Clear();
        TrackedQuest = tracked;
        RadioStation = station;
        RadioMuted = muted;
        Waypoint = waypoint;
        Favourites = new InMemoryFavouriteStore(favourites);
        foreach (var id in discovered) _discovered.Add(id);
    }
}
