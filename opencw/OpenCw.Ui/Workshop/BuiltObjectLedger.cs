using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed record BuiltObject(string Token, string Cell, string Recipe, string Base, double X, double Y, double Z, double Yaw);

public sealed record BuiltReconcile(IReadOnlyList<string> Free, IReadOnlyList<BuiltObject> Spawn);

// Requires: main thread only; positions are local to the cell root named by Cell, so a moved cell origin does not move built objects.
public sealed class BuiltObjectLedger
{
    public const string SectionName = "workshop.built.json";
    public const string TokenPrefix = "Built:";

    private readonly List<BuiltObject> _objects = [];
    private int _next;

    public static BuiltObjectLedger Current { get; } = new();

    public IReadOnlyList<BuiltObject> All => _objects;

    public bool IsEmpty => _objects.Count == 0;

    // Invariant: tokens run 1..int.MaxValue, the range Apply accepts; callers must check CanAdd before they place an object.
    public bool CanAdd => _next < int.MaxValue;

    public BuiltObject Add(string cell, string recipe, string baseKey, double x, double y, double z, double yaw)
    {
        if (!CanAdd) throw new InvalidOperationException("built object tokens are exhausted");
        var built = new BuiltObject(TokenPrefix + (++_next).ToString("D6", CultureInfo.InvariantCulture), cell, recipe, baseKey, x, y, z, yaw);
        _objects.Add(built);
        return built;
    }

    public bool Remove(string token) => _objects.RemoveAll(built => string.Equals(built.Token, token, StringComparison.Ordinal)) > 0;

    public BuiltObject? Find(string token) => _objects.FirstOrDefault(built => string.Equals(built.Token, token, StringComparison.Ordinal));

    public bool Contains(string token) => _objects.Any(built => string.Equals(built.Token, token, StringComparison.Ordinal));

    public IEnumerable<BuiltObject> InCell(string cell) =>
        _objects.Where(built => string.Equals(built.Cell, cell, StringComparison.OrdinalIgnoreCase));

    public BuiltReconcile Reconcile(string cell, IReadOnlyCollection<string> liveTokens, Func<string, bool> isTaken)
    {
        var free = liveTokens.Where(token => !Contains(token) || isTaken(token)).ToArray();
        var spawn = InCell(cell).Where(built => !liveTokens.Contains(built.Token, StringComparer.Ordinal) && !isTaken(built.Token)).ToArray();
        return new BuiltReconcile(free, spawn);
    }

    public void Clear()
    {
        _objects.Clear();
        _next = 0;
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (_objects.Count == 0) return new Dictionary<string, byte[]>();
        var rows = new JsonArray(_objects.Select(built => (JsonNode)new JsonObject
        {
            ["token"] = built.Token, ["cell"] = built.Cell, ["recipe"] = built.Recipe, ["base"] = built.Base,
            ["x"] = built.X, ["y"] = built.Y, ["z"] = built.Z, ["yaw"] = built.Yaw,
        }).ToArray());
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(rows.ToJsonString()) };
    }

    // Constraint: replaces every built object; throws FormatException on a malformed section and leaves the ledger untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var loaded = new List<BuiltObject>();
        var next = 0;
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                foreach (var node in JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonArray ?? throw new FormatException("built objects are not a list"))
                {
                    var token = Text(node, "token");
                    if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal) || !int.TryParse(token.AsSpan(TokenPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                        || loaded.Any(built => built.Token == token))
                        throw new FormatException("built object token is malformed");
                    loaded.Add(new BuiltObject(token, Text(node, "cell"), Text(node, "recipe"), Text(node, "base"),
                        Number(node, "x"), Number(node, "y"), Number(node, "z"), Number(node, "yaw")));
                    next = Math.Max(next, number);
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("built objects are malformed", ex); }
        }
        _objects.Clear();
        _objects.AddRange(loaded);
        _next = next;
    }

    private static string Text(JsonNode? node, string name) =>
        node?[name]?.GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException($"built object {name} is missing");

    private static double Number(JsonNode? node, string name) =>
        node?[name]?.GetValue<double>() is { } value && Math.Abs(value) <= float.MaxValue ? value : throw new FormatException($"built object {name} is not a finite float");
}
