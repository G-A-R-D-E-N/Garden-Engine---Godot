using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum CrimeKind { Theft, Pickpocket, Trespass, Attack, Murder }

public sealed record CrimeEvent(CrimeKind Kind, string Owner, string? Faction, string Item, int Count, int Value, bool Witnessed, string Ref);

public sealed record FactionCrime(int Bounty, int WitnessedValue, int UnwitnessedValue);

public interface ICrimeFacts
{
    string? FactionOf(string owner);

    int ValueOf(string item);

    double? StealMultiplier { get; }

    double? StealMultiplierFor(string faction) => StealMultiplier;

    CrimeSettings? Settings => null;

    int? PickpocketCrimeGold(string faction) => null;

    bool FactionIgnoresPickpocket(string faction) => false;
}

public sealed record CrimeSettings(int? Attack, int? Murder, int? Pickpocket, int? Trespass, int? StealHorse, int? MinValue, int? Escape);

// Requires: main thread only. Facts is null until the record sources exist; a report without facts is still recorded, with no faction, value 0 and no bounty.
public sealed class CrimeLedger
{
    public const string SectionName = "crime.ledger.json";
    public const int SectionVersion = 1;
    public const int MaxEvents = 256;

    private readonly List<CrimeEvent> _events = [];
    private readonly Dictionary<string, FactionCrime> _factions = new(StringComparer.OrdinalIgnoreCase);

    public static CrimeLedger Current { get; } = new();

    public ICrimeFacts? Facts { get; set; }

    public IWitnessSource? Witnesses { get; set; }

    public CrimeFactionOverrides CrimeFactions { get; } = new();

    // Invariant: null adds no bounty; CrimeHostWiring installs BountyFor (value times multiplier), UNVERIFIED until a game check.
    public Func<int, double?, int>? Formula { get; set; }

    public IReadOnlyList<CrimeEvent> Events => _events;

    public IReadOnlyDictionary<string, FactionCrime> Factions => _factions;

    public bool IsEmpty => _events.Count == 0 && _factions.Count == 0 && CrimeFactions.IsEmpty;

    public int Bounty(string faction) => _factions.TryGetValue(faction, out var crime) ? crime.Bounty : 0;

    public int StolenValue(string faction, bool witnessed) =>
        _factions.TryGetValue(faction, out var crime) ? witnessed ? crime.WitnessedValue : crime.UnwitnessedValue : 0;

    public CrimeEvent Report(CrimeKind kind, string owner, string item, int count, string refKey, bool witnessed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        var n = Math.Max(count, 1);
        var facts = Facts;
        var faction = Guarded<string>(() => facts?.FactionOf(owner));
        var unit = Math.Max(Guarded<int?>(() => facts?.ValueOf(item)) ?? 0, 0);
        var value = (int)Math.Min((long)unit * n, int.MaxValue);
        var seen = witnessed || (kind is CrimeKind.Theft or CrimeKind.Trespass) && Seen(kind);
        var crime = new CrimeEvent(kind, owner, string.IsNullOrWhiteSpace(faction) ? null : faction, item, n, value, seen, refKey ?? "");
        Add(crime);
        return crime;
    }

    // Requires: main thread; a source that throws or is absent means nobody saw it.
    public bool Seen(CrimeKind kind)
    {
        var source = Witnesses;
        return Guarded<bool?>(() => source is not null && WitnessRules.AnySees(source.Candidates(), kind)) == true;
    }

    public void Clear()
    {
        _events.Clear();
        _factions.Clear();
        CrimeFactions.Clear();
    }

    // Invariant: an event with no faction is kept in the event list only; only a theft and a pickpocket tally stolen value, and a trespass, attack or murder row exists only once a witnessed flat amount is added to it.
    private void Add(CrimeEvent crime)
    {
        _events.Add(crime);
        if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
        if (crime.Faction is not { } faction) return;
        var added = AddedBounty(crime, faction);
        var tallies = crime.Kind is CrimeKind.Theft or CrimeKind.Pickpocket;
        if (!tallies && added == 0) return;
        var held = _factions.GetValueOrDefault(faction) ?? new FactionCrime(0, 0, 0);
        _factions[faction] = held with
        {
            Bounty = Saturate(held.Bounty, added),
            WitnessedValue = tallies && crime.Witnessed ? Saturate(held.WitnessedValue, crime.Value) : held.WitnessedValue,
            UnwitnessedValue = tallies && !crime.Witnessed ? Saturate(held.UnwitnessedValue, crime.Value) : held.UnwitnessedValue,
        };
    }

    // Invariant: only a witnessed crime adds bounty; a theft uses the installed formula, a pickpocket from a faction that ignores pickpocket adds none, every other kind its flat amount (UNVERIFIED), and an absent amount adds nothing.
    private int AddedBounty(CrimeEvent crime, string faction)
    {
        if (!crime.Witnessed) return 0;
        if (crime.Kind == CrimeKind.Theft)
            return Formula is { } formula ? Math.Max(formula(crime.Value, Guarded<double?>(() => Facts?.StealMultiplierFor(faction))), 0) : 0;
        if (crime.Kind == CrimeKind.Pickpocket && Guarded<bool?>(() => Facts?.FactionIgnoresPickpocket(faction)) == true) return 0;
        return Math.Max(FlatAmount(crime.Kind, Guarded<CrimeSettings>(() => Facts?.Settings), crime.Kind == CrimeKind.Pickpocket ? Guarded<int?>(() => Facts?.PickpocketCrimeGold(faction)) : null) ?? 0, 0);
    }

    // Invariant: UNVERIFIED mapping; the flat amount named after the kind, a pickpocket takes the victim faction's value when it is non-zero, a theft has none (it uses the formula), and "Use Defaults" is not consulted.
    public static int? FlatAmount(CrimeKind kind, CrimeSettings? settings, int? factionPickpocket = null) => kind switch
    {
        CrimeKind.Pickpocket => factionPickpocket is > 0 ? factionPickpocket : settings?.Pickpocket,
        CrimeKind.Trespass => settings?.Trespass,
        CrimeKind.Attack => settings?.Attack,
        CrimeKind.Murder => settings?.Murder,
        _ => null,
    };

    // Invariant: the formula value times the steal setting is UNVERIFIED; an absent, non-finite or non-positive setting adds nothing.
    public static int BountyFor(int totalValue, double? multiplier) =>
        multiplier is { } m && double.IsFinite(m) && m > 0 && totalValue > 0 ? (int)Math.Min(Math.Round(totalValue * m, MidpointRounding.AwayFromZero), int.MaxValue) : 0;

    private static int Saturate(int held, int added) => (int)Math.Min((long)held + added, int.MaxValue);

    private static T? Guarded<T>(Func<T?> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return default; }
    }

    public IReadOnlyDictionary<string, byte[]> Sections()
    {
        if (IsEmpty) return new Dictionary<string, byte[]>();
        var events = new JsonArray(_events.Select(crime =>
        {
            var row = new JsonObject
            {
                ["kind"] = crime.Kind.ToString(), ["owner"] = crime.Owner, ["item"] = crime.Item, ["count"] = crime.Count,
                ["value"] = crime.Value, ["witnessed"] = crime.Witnessed, ["ref"] = crime.Ref,
            };
            if (crime.Faction is { } faction) row["faction"] = faction;
            return (JsonNode)row;
        }).ToArray());
        var factions = new JsonArray(_factions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (JsonNode)new JsonObject
        {
            ["faction"] = pair.Key, ["bounty"] = pair.Value.Bounty, ["witnessed"] = pair.Value.WitnessedValue, ["unwitnessed"] = pair.Value.UnwitnessedValue,
        }).ToArray());
        var root = new JsonObject { ["version"] = SectionVersion, ["events"] = events, ["factions"] = factions };
        if (!CrimeFactions.IsEmpty) root["crimeFactions"] = CrimeFactions.ToJson();
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(root.ToJsonString()) };
    }

    // Requires: replaces the whole ledger; throws FormatException on an unknown version or a malformed row and leaves the current ledger untouched.
    public void Apply(IReadOnlyDictionary<string, byte[]> sections)
    {
        var events = new List<CrimeEvent>();
        var factions = new Dictionary<string, FactionCrime>(StringComparer.OrdinalIgnoreCase);
        CrimeFactionOverrides overrides = new();
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("crime ledger is not an object");
                if ((int?)root["version"] != SectionVersion) throw new FormatException("unsupported crime ledger version");
                foreach (var node in root["events"] as JsonArray ?? throw new FormatException("crime ledger has no events"))
                {
                    var owner = node?["owner"]?.GetValue<string>();
                    var item = node?["item"]?.GetValue<string>();
                    var count = node?["count"]?.GetValue<int>();
                    var value = node?["value"]?.GetValue<int>();
                    var witnessed = node?["witnessed"]?.GetValue<bool>();
                    if (!Enum.TryParse<CrimeKind>(node?["kind"]?.GetValue<string>(), out var kind) || !Enum.IsDefined(kind)
                        || string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(item) || count is not > 0 || value is not >= 0 || witnessed is null)
                        throw new FormatException("crime event row is malformed");
                    var faction = node!["faction"]?.GetValue<string>();
                    events.Add(new CrimeEvent(kind, owner, string.IsNullOrWhiteSpace(faction) ? null : faction, item, count.Value, value.Value, witnessed.Value, node["ref"]?.GetValue<string>() ?? ""));
                }
                foreach (var node in root["factions"] as JsonArray ?? throw new FormatException("crime ledger has no factions"))
                {
                    var faction = node?["faction"]?.GetValue<string>();
                    var bounty = node?["bounty"]?.GetValue<int>();
                    var witnessed = node?["witnessed"]?.GetValue<int>();
                    var unwitnessed = node?["unwitnessed"]?.GetValue<int>();
                    if (string.IsNullOrWhiteSpace(faction) || bounty is not >= 0 || witnessed is not >= 0 || unwitnessed is not >= 0)
                        throw new FormatException("crime faction row is malformed");
                    factions[faction] = new FactionCrime(bounty.Value, witnessed.Value, unwitnessed.Value);
                }
                if (root["crimeFactions"] is { } rows) overrides.LoadJson(rows);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { throw new FormatException("crime ledger is malformed", ex); }
        }
        _events.Clear();
        _events.AddRange(events.Count > MaxEvents ? events.Skip(events.Count - MaxEvents) : events);
        _factions.Clear();
        foreach (var (faction, crime) in factions) _factions[faction] = crime;
        CrimeFactions.ReplaceWith(overrides);
    }
}
