using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Interaction;

public sealed record DroppedRef(string Token, string Cell, string Id, string BaseType, int Count, ItemState State, double X, double Y, double Z, double Yaw);

public sealed record DroppedReconcile(IReadOnlyList<string> Free, IReadOnlyList<DroppedRef> Spawn);

// Requires: main thread only; holds the instance state and, for refs placed in a cell, the cell-local pose of a dropped world reference until ItemPickup takes it back.
public sealed class DroppedItemStates
{
    public const string TokenPrefix = "Dropped:";
    public const string SectionName = "interaction.dropped.json";

    private readonly Dictionary<string, ItemState> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DroppedRef> _placed = new(StringComparer.Ordinal);
    private int _next;

    public static DroppedItemStates Current { get; } = new();

    public bool IsEmpty => _placed.Count == 0;

    public IReadOnlyCollection<DroppedRef> Placed => _placed.Values;

    public string Remember(ItemState state)
    {
        var token = TokenPrefix + (++_next).ToString("X8", CultureInfo.InvariantCulture);
        if (!state.IsPlain) _states[token] = state;
        return token;
    }

    // Invariant: the pose is local to the cell root named by Cell, so a moved cell origin does not move the drop.
    public void Place(DroppedRef drop)
    {
        if (!IsPoseValue(drop.X) || !IsPoseValue(drop.Y) || !IsPoseValue(drop.Z) || !IsPoseValue(drop.Yaw))
            throw new ArgumentException("dropped item pose is not a finite float", nameof(drop));
        _placed[drop.Token] = drop;
    }

    // Invariant: keeps the token counter so a cleared registry never reissues a token.
    public void Clear()
    {
        _placed.Clear();
        _states.Clear();
    }

    public ItemState? Take(string refKey)
    {
        _placed.Remove(refKey);
        return _states.Remove(refKey, out var state) ? state : null;
    }

    public IEnumerable<DroppedRef> InCell(string cell) =>
        _placed.Values.Where(drop => string.Equals(drop.Cell, cell, StringComparison.OrdinalIgnoreCase));

    public DroppedReconcile Reconcile(string cell, IReadOnlyCollection<string> liveTokens)
    {
        var free = liveTokens.Where(token => !_placed.ContainsKey(token)).ToArray();
        var spawn = InCell(cell).Where(drop => !liveTokens.Contains(drop.Token, StringComparer.Ordinal)).ToArray();
        return new DroppedReconcile(free, spawn);
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_placed.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_placed.Values.OrderBy(drop => drop.Token, StringComparer.Ordinal).Select(drop => (JsonNode)new JsonObject
        {
            ["token"] = drop.Token, ["cell"] = drop.Cell, ["id"] = drop.Id, ["type"] = drop.BaseType, ["count"] = drop.Count,
            ["mods"] = new JsonArray(drop.State.Mods.Select(mod => (JsonNode)JsonValue.Create(mod)!).ToArray()),
            ["condition"] = drop.State.Condition, ["rank"] = drop.State.Rank,
            ["x"] = drop.X, ["y"] = drop.Y, ["z"] = drop.Z, ["yaw"] = drop.Yaw,
        }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Requires: callers treat a FormatException as a refused section; Apply replaces every placed drop (a save without the section holds none); throws FormatException on a malformed section and leaves the registry untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var loaded = new List<DroppedRef>();
        var highest = 0;
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("dropped items are not a list"))
                {
                    var token = Text(node, "token");
                    if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal)
                        || !int.TryParse(token.AsSpan(TokenPrefix.Length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number) || number < 0
                        || loaded.Any(drop => drop.Token == token))
                        throw new FormatException("dropped item token is malformed");
                    var count = node?["count"]?.GetValue<int>() ?? 0;
                    if (count < 1) throw new FormatException("dropped item count is not positive");
                    var mods = node?["mods"] is JsonArray list ? list.Select(mod => mod?.GetValue<string>() is { Length: > 0 } name ? name : throw new FormatException("dropped item mod is empty")).ToArray() : Array.Empty<string>();
                    var condition = node?["condition"] is { } stored ? stored.GetValue<double>() : ItemState.FullCondition;
                    var rank = node?["rank"] is { } storedRank ? storedRank.GetValue<int>() : 0;
                    loaded.Add(new DroppedRef(token, Text(node, "cell"), Text(node, "id"), Text(node, "type"), count, new ItemState(mods, condition, rank),
                        Number(node, "x"), Number(node, "y"), Number(node, "z"), Number(node, "yaw")));
                    highest = Math.Max(highest, number);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { throw new FormatException("dropped items are malformed", ex); }
        }
        _placed.Clear();
        _states.Clear();
        foreach (var drop in loaded)
        {
            _placed[drop.Token] = drop;
            if (!drop.State.IsPlain) _states[drop.Token] = drop.State;
        }
        _next = Math.Max(_next, highest);
    }

    private static string Text(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException($"dropped item {name} is missing");

    private static double Number(JsonNode? node, string name) =>
        node?[name]?.GetValue<double>() is { } value && IsPoseValue(value) ? value : throw new FormatException($"dropped item {name} is not a finite float");

    private static bool IsPoseValue(double value) => Math.Abs(value) <= float.MaxValue;
}
