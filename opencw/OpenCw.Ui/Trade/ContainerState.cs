using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.PipBoy;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Trade;

public sealed record WeaponSave(string Key, int Clip, bool Drawn, ItemState Item);

public sealed record PowerArmorFrameSave(string Ref, string Name, string? Core, double Charge, IReadOnlyList<double>? Conditions = null);

public sealed record PowerArmorSave(string? Occupied, IReadOnlyList<PowerArmorFrameSave> Frames);

public sealed class ContainerLedger
{
    private readonly Dictionary<string, PlayerItems> _containers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _lockouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _opened = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _caught = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _noPickpocket = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _caughtAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deathLoot = new(StringComparer.OrdinalIgnoreCase);

    public static ContainerLedger Current { get; } = new();

    public IEnumerable<string> Opened => _opened;

    public IEnumerable<string> Taken => _taken;

    public const double CaughtRecoveryHours = 24;

    public IEnumerable<string> Caught => _caught;

    public IEnumerable<string> PickpocketForbidden => _noPickpocket;

    public bool IsPickpocketForbidden(string refKey) => _noPickpocket.Contains(refKey);

    public void ForbidPickpocket(string refKey, bool forbidden = true)
    {
        if (forbidden) _noPickpocket.Add(refKey);
        else _noPickpocket.Remove(refKey);
    }

    public IEnumerable<KeyValuePair<string, double>> CaughtAt => _caughtAt;

    public IEnumerable<string> DeathLoot => _deathLoot;

    public WeaponSave? Weapon { get; set; }

    public PowerArmorSave? PowerArmor { get; set; }

    public IReadOnlyList<EquippedArmor> Armor { get; set; } = [];

    public CompanionRoster Companions { get; } = new();

    public bool IsEmpty => _containers.Count == 0 && _unlocked.Count == 0 && _opened.Count == 0 && _taken.Count == 0 && _caught.Count == 0 && _noPickpocket.Count == 0 && _deathLoot.Count == 0 && Weapon is null && PowerArmor is null && Armor.Count == 0 && Companions.Current is null;

    public bool IsOpened(string refKey) => _opened.Contains(refKey);

    public void SetOpened(string refKey, bool open)
    {
        if (open) _opened.Add(refKey);
        else _opened.Remove(refKey);
    }

    public bool IsTaken(string refKey) => _taken.Contains(refKey);

    public void MarkTaken(string refKey) => _taken.Add(refKey);

    // Invariant: with a clock reading (game hours) a flag expires CaughtRecoveryHours after it was marked; a flag with no stamp (an old save) is stamped at its first read, so it recovers too; without a reading the flag never expires.
    public bool IsCaught(string refKey, double? now = null)
    {
        if (!_caught.Contains(refKey)) return false;
        if (now is not { } hours || !double.IsFinite(hours)) return true;
        if (!_caughtAt.TryGetValue(refKey, out var at))
        {
            _caughtAt[refKey] = hours;
            return true;
        }
        if (hours - at < CaughtRecoveryHours) return true;
        ClearCaught(refKey);
        return false;
    }

    public void MarkCaught(string refKey, double? now = null)
    {
        _caught.Add(refKey);
        if (now is { } hours && double.IsFinite(hours)) _caughtAt[refKey] = hours;
        else _caughtAt.Remove(refKey);
    }

    public void ClearCaught(string refKey)
    {
        _caught.Remove(refKey);
        _caughtAt.Remove(refKey);
    }

    public void StampCaught(string refKey, double at)
    {
        if (_caught.Contains(refKey) && double.IsFinite(at)) _caughtAt[refKey] = at;
    }

    public bool HasDeathLoot(string refKey) => _deathLoot.Contains(refKey);

    public void MarkDeathLoot(string refKey) => _deathLoot.Add(refKey);

    public IEnumerable<string> References => _containers.Keys;

    public IEnumerable<string> Unlocked => _unlocked;

    public bool IsUnlocked(string refKey) => _unlocked.Contains(refKey);

    public event Action<string>? UnlockMarked;

    public void MarkUnlocked(string refKey)
    {
        if (_unlocked.Add(refKey)) UnlockMarked?.Invoke(refKey);
    }

    public IEnumerable<KeyValuePair<string, double>> Lockouts => _lockouts;

    public double? LockedOutUntil(string refKey) => _lockouts.TryGetValue(refKey, out var until) ? until : null;

    public void LockOut(string refKey, double until) => _lockouts[refKey] = until;

    public PlayerItems? Find(string refKey) => _containers.GetValueOrDefault(refKey);

    // Invariant: base stacks and later stateless adds to a held inventory take DefaultState; Freeze(refKey, entries), Reload and explicit-state adds keep the states they are given.
    public Func<string, ItemState>? DefaultState { get; set; }

    public PlayerItems Freeze(string refKey, IEnumerable<InventoryStack> stacks) =>
        Freeze(refKey, stacks.Select(stack => new ItemEntry(stack.Id, DefaultState?.Invoke(stack.Id) ?? ItemState.Plain, stack.Count)));

    public PlayerItems Freeze(string refKey, IEnumerable<ItemEntry> entries)
    {
        if (_containers.TryGetValue(refKey, out var held)) return held;
        var created = new PlayerItems { DefaultState = id => DefaultState?.Invoke(id) ?? ItemState.Plain };
        created.ReplaceEntries(entries);
        _containers[refKey] = created;
        return created;
    }

    // Constraint: a PlacedContainerInventory holds its PlayerItems by reference, so a reload refills the held instance instead of replacing it.
    public void Reload(IEnumerable<(string Ref, List<ItemEntry> Stacks)> containers)
    {
        var keep = new Dictionary<string, List<ItemEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, stacks) in containers) keep.TryAdd(key, stacks);
        foreach (var gone in _containers.Keys.Where(key => !keep.ContainsKey(key)).ToList())
        {
            _containers[gone].Clear();
            _containers.Remove(gone);
        }
        foreach (var (key, stacks) in keep)
        {
            if (_containers.TryGetValue(key, out var held)) held.ReplaceEntries(stacks);
            else Freeze(key, stacks);
        }
        ClearFlags();
    }

    public void Clear()
    {
        _containers.Clear();
        ClearFlags();
    }

    private void ClearFlags()
    {
        _unlocked.Clear();
        _lockouts.Clear();
        _opened.Clear();
        _taken.Clear();
        _caught.Clear();
        _caughtAt.Clear();
        _noPickpocket.Clear();
        _deathLoot.Clear();
        Weapon = null;
        PowerArmor = null;
        Armor = [];
        Companions.Clear();
    }
}

public static class ContainerStateSave
{
    public const string SectionName = "container.state.json";
    public const int CurrentVersion = 1;

    // Constraint: lockout times are on the caller's clock; `now` must be that clock's current reading. Saved as seconds remaining.
    public static IReadOnlyDictionary<string, byte[]> Sections(ContainerLedger ledger, PlayerItems player, double now = 0)
    {
        var lockouts = ledger.Lockouts.Where(pair => pair.Value > now).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        if (player.Entries.Count == 0 && ledger.IsEmpty && lockouts.Count == 0) return new Dictionary<string, byte[]>();
        var containers = new JsonArray();
        foreach (var key in ledger.References.Order(StringComparer.Ordinal))
            containers.Add(new JsonObject { ["ref"] = key, ["stacks"] = Rows(ledger.Find(key)!.Entries) });
        var root = new JsonObject { ["version"] = CurrentVersion, ["player"] = Rows(player.Entries), ["containers"] = containers };
        if (ledger.Unlocked.Any()) root["unlocked"] = Keys(ledger.Unlocked);
        if (ledger.Opened.Any()) root["opened"] = Keys(ledger.Opened);
        if (ledger.Taken.Any()) root["taken"] = Keys(ledger.Taken);
        if (ledger.Caught.Any()) root["caught"] = Keys(ledger.Caught);
        if (ledger.PickpocketForbidden.Any()) root["noPickpocket"] = Keys(ledger.PickpocketForbidden);
        if (ledger.CaughtAt.Any())
            root["caughtAt"] = new JsonArray(ledger.CaughtAt.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (JsonNode)new JsonObject { ["ref"] = pair.Key, ["hours"] = pair.Value }).ToArray());
        if (ledger.DeathLoot.Any()) root["deathLoot"] = Keys(ledger.DeathLoot);
        if (ledger.Weapon is { } weapon)
            root["weapon"] = new JsonObject { ["key"] = weapon.Key, ["clip"] = weapon.Clip, ["drawn"] = weapon.Drawn, ["item"] = Rows([new ItemEntry(weapon.Key, weapon.Item, 1)])[0]!.DeepClone() };
        if (ledger.PowerArmor is { } armor) root["powerArmor"] = ArmorRow(armor);
        if (ledger.Armor.Count > 0) root["worn"] = WornRows(ledger.Armor);
        if (ledger.Companions.Current is { } companion)
            root["companions"] = new JsonArray(new JsonObject { ["ref"] = companion.RefKey, ["base"] = companion.BaseKey, ["state"] = companion.State.ToString(), ["distance"] = companion.Distance.ToString(), ["stance"] = companion.Stance.ToString() });
        if (lockouts.Count > 0) root["lockouts"] = new JsonArray(lockouts.Select(pair => (JsonNode)new JsonObject { ["ref"] = pair.Key, ["seconds"] = pair.Value - now }).ToArray());
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" });
        return new Dictionary<string, byte[]> { [SectionName] = Encoding.UTF8.GetBytes(json) };
    }

    // Constraint: throws FormatException on an unknown version or a malformed row and leaves ledger and player untouched.
    public static void Apply(IReadOnlyDictionary<string, byte[]> sections, ContainerLedger ledger, PlayerItems player, double now = 0)
    {
        var heldLockouts = new List<(string Ref, double Until)>();
        var heldPlayer = new List<ItemEntry>();
        var heldContainers = new List<(string Ref, List<ItemEntry> Stacks)>();
        var heldUnlocked = new List<string>();
        var heldOpened = new List<string>();
        var heldTaken = new List<string>();
        var heldCaught = new List<string>();
        var heldNoPickpocket = new List<string>();
        var heldCaughtAt = new List<(string Ref, double Hours)>();
        var heldDeathLoot = new List<string>();
        WeaponSave? heldWeapon = null;
        PowerArmorSave? heldArmor = null;
        var heldWorn = new List<EquippedArmor>();
        CompanionEntry? heldCompanion = null;
        if (sections.TryGetValue(SectionName, out var payload))
        {
            try
            {
                var root = JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject ?? throw new FormatException("container state is not an object");
                if ((int?)root["version"] != CurrentVersion) throw new FormatException("unsupported container state version");
                heldPlayer = Read(root["player"]);
                var seenContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in root["containers"] as JsonArray ?? throw new FormatException("container state has no containers"))
                {
                    var containerRef = Text(node?["ref"], "container ref");
                    if (!seenContainers.Add(containerRef)) throw new FormatException("container " + containerRef + " appears more than once");
                    heldContainers.Add((containerRef, Read(node!["stacks"])));
                }
                ReadKeys(root["unlocked"], heldUnlocked, "unlocked");
                ReadKeys(root["opened"], heldOpened, "opened");
                ReadKeys(root["taken"], heldTaken, "taken");
                ReadKeys(root["caught"], heldCaught, "caught");
                ReadKeys(root["noPickpocket"], heldNoPickpocket, "noPickpocket");
                if (root["caughtAt"] is { } caughtAt)
                    foreach (var node in caughtAt as JsonArray ?? throw new FormatException("caughtAt list is malformed"))
                    {
                        var hours = node?["hours"]?.GetValue<double>();
                        if (node?["ref"]?.GetValue<string>() is not { Length: > 0 } key || hours is not { } at || !double.IsFinite(at)) throw new FormatException("caughtAt row is malformed");
                        heldCaughtAt.Add((key, at));
                    }
                ReadKeys(root["deathLoot"], heldDeathLoot, "deathLoot");
                if (root["weapon"] is { } weapon)
                {
                    var key = Text(weapon["key"], "weapon key");
                    var item = weapon["item"] is { } row ? Read(new JsonArray(row.DeepClone()))[0].State : ItemState.Plain;
                    heldWeapon = new WeaponSave(key, weapon["clip"]?.GetValue<int>() is { } clip and >= 0 ? clip : throw new FormatException("weapon clip is malformed"), weapon["drawn"]?.GetValue<bool>() ?? false, item);
                }
                if (root["powerArmor"] is { } armor) heldArmor = ReadArmor(armor);
                if (root["worn"] is { } worn) heldWorn = ReadWorn(worn);
                if (root["companions"] is { } companions) heldCompanion = ReadCompanion(companions);
                if (root["lockouts"] is { } lockouts)
                    foreach (var node in lockouts as JsonArray ?? throw new FormatException("lockout list is malformed"))
                    {
                        var seconds = node?["seconds"]?.GetValue<double>();
                        if (node?["ref"]?.GetValue<string>() is not { Length: > 0 } key || seconds is not > 0 || !double.IsFinite(seconds.Value)) throw new FormatException("lockout row is malformed");
                        heldLockouts.Add((key, now + seconds.Value));
                    }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new FormatException("container state is malformed: " + ex.Message, ex);
            }
        }
        player.ReplaceEntries(heldPlayer);
        ledger.Reload(heldContainers);
        foreach (var key in heldUnlocked) ledger.MarkUnlocked(key);
        foreach (var (key, until) in heldLockouts) ledger.LockOut(key, until);
        foreach (var key in heldOpened) ledger.SetOpened(key, true);
        foreach (var key in heldTaken) ledger.MarkTaken(key);
        foreach (var key in heldCaught) ledger.MarkCaught(key);
        foreach (var key in heldNoPickpocket) ledger.ForbidPickpocket(key);
        foreach (var (key, hours) in heldCaughtAt) ledger.StampCaught(key, hours);
        foreach (var key in heldDeathLoot) ledger.MarkDeathLoot(key);
        ledger.Weapon = heldWeapon;
        ledger.PowerArmor = heldArmor;
        ledger.Armor = heldWorn;
        ledger.Companions.Restore(heldCompanion);
    }

    private static JsonArray Keys(IEnumerable<string> keys) =>
        new(keys.Order(StringComparer.Ordinal).Select(key => (JsonNode)JsonValue.Create(key)!).ToArray());

    private static string Text(JsonNode? node, string what) =>
        node?.GetValue<string>() is { Length: > 0 } text ? text : throw new FormatException(what + " is malformed");

    private static void ReadKeys(JsonNode? node, List<string> into, string what)
    {
        if (node is null) return;
        foreach (var key in node as JsonArray ?? throw new FormatException(what + " list is malformed")) into.Add(Text(key, what + " row"));
    }

    private static JsonArray WornRows(IEnumerable<EquippedArmor> worn) =>
        new(worn.Select(piece =>
        {
            var row = (JsonObject)Rows([new ItemEntry(piece.Id, piece.State, 1)])[0]!.DeepClone();
            row["mask"] = piece.Mask;
            return (JsonNode)row;
        }).ToArray());

    private static List<EquippedArmor> ReadWorn(JsonNode node)
    {
        var worn = new List<EquippedArmor>();
        foreach (var row in node as JsonArray ?? throw new FormatException("worn armor list is malformed"))
        {
            var mask = row?["mask"]?.GetValue<uint>() ?? throw new FormatException("worn armor mask is malformed");
            var entry = Read(new JsonArray(row.DeepClone()))[0];
            worn.Add(new EquippedArmor(entry.Id, entry.State, mask));
        }
        return worn;
    }

    private static CompanionEntry? ReadCompanion(JsonNode node)
    {
        var rows = node as JsonArray ?? throw new FormatException("companion list is malformed");
        if (rows.Count > 1) throw new FormatException("companion list holds more than one companion");
        if (rows.Count == 0) return null;
        var row = rows[0];
        return new CompanionEntry(Text(row?["ref"], "companion ref"), Text(row?["base"], "companion base"), Choice<CompanionState>(row?["state"], "companion state"), Choice<CompanionDistance>(row?["distance"], "companion distance"), Choice<CompanionStance>(row?["stance"], "companion stance"));
    }

    private static T Choice<T>(JsonNode? node, string what) where T : struct, Enum
    {
        var text = Text(node, what);
        if (!Enum.GetNames<T>().Contains(text)) throw new FormatException(what + " is malformed");
        return Enum.Parse<T>(text);
    }

    private static JsonObject ArmorRow(PowerArmorSave armor)
    {
        var frames = new JsonArray(armor.Frames.Select(frame =>
        {
            var row = new JsonObject { ["ref"] = frame.Ref, ["name"] = frame.Name, ["charge"] = frame.Charge };
            if (frame.Core is { } core) row["core"] = core;
            if (frame.Conditions is { } conditions) row["parts"] = new JsonArray(conditions.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
            return (JsonNode)row;
        }).ToArray());
        var root = new JsonObject { ["frames"] = frames };
        if (armor.Occupied is { } occupied) root["occupied"] = occupied;
        return root;
    }

    private static PowerArmorSave ReadArmor(JsonNode node)
    {
        var frames = new List<PowerArmorFrameSave>();
        foreach (var row in node["frames"] as JsonArray ?? throw new FormatException("power armor frames are malformed"))
        {
            var charge = row?["charge"]?.GetValue<double>() ?? throw new FormatException("power armor charge is malformed");
            frames.Add(new PowerArmorFrameSave(Text(row["ref"], "frame ref"), row["name"]?.GetValue<string>() ?? "", row["core"] is { } core ? Text(core, "frame core") : null, Math.Clamp(charge, 0, 1), ReadConditions(row["parts"])));
        }
        var occupied = node["occupied"] is { } held ? Text(held, "occupied frame") : null;
        if (occupied != null && !frames.Exists(frame => frame.Ref == occupied)) throw new FormatException("occupied frame is not in the frame list");
        return new PowerArmorSave(occupied, frames);
    }

    // Invariant: a missing, wrong-length or malformed parts array loads as null (pieces full) and never refuses the container section.
    private static double[]? ReadConditions(JsonNode? node)
    {
        if (node is not JsonArray { Count: PowerArmorHudModel.PartCount } array) return null;
        var values = new double[array.Count];
        for (var i = 0; i < values.Length; i++)
        {
            if (array[i] is not JsonValue value || !value.TryGetValue<double>(out var v) || !double.IsFinite(v)) return null;
            values[i] = Math.Clamp(v, -1, 1);
        }
        return values;
    }

    private static JsonArray Rows(IEnumerable<ItemEntry> entries) =>
        new(entries.Select(entry =>
        {
            var row = new JsonObject { ["id"] = entry.Id, ["count"] = entry.Count };
            if (entry.State.Mods.Count > 0) row["mods"] = new JsonArray(entry.State.Mods.Select(mod => (JsonNode)JsonValue.Create(mod)!).ToArray());
            if (entry.State.Condition != ItemState.FullCondition) row["condition"] = entry.State.Condition;
            if (entry.State.Rank != 0) row["rank"] = entry.State.Rank;
            if (entry.State.StolenFrom is { } stolen) row["stolenFrom"] = stolen;
            if (entry.State.Deposited) row["deposited"] = true;
            return (JsonNode)row;
        }).ToArray());

    private static List<ItemEntry> Read(JsonNode? node)
    {
        var rows = node as JsonArray ?? throw new FormatException("stack list is missing");
        var entries = new List<ItemEntry>();
        foreach (var row in rows)
        {
            var id = row?["id"]?.GetValue<string>();
            var count = row?["count"]?.GetValue<int>();
            if (string.IsNullOrWhiteSpace(id) || count is not > 0) throw new FormatException("stack row is malformed");
            if (row!["mods"] is not null and not JsonArray) throw new FormatException("stack row mods is not a list");
            var mods = row["mods"] is JsonArray list ? list.Select(mod => mod?.GetValue<string>()).ToList() : [];
            if (mods.Any(string.IsNullOrWhiteSpace)) throw new FormatException("stack row has an empty mod");
            var condition = row["condition"]?.GetValue<double>() ?? ItemState.FullCondition;
            var rank = row["rank"]?.GetValue<int>() ?? 0;
            if (!double.IsFinite(condition) || condition < 0 || condition > ItemState.FullCondition || rank < 0
                || mods.Distinct(StringComparer.OrdinalIgnoreCase).Count() != mods.Count)
                throw new FormatException("stack row instance data is invalid");
            entries.Add(new ItemEntry(id, new ItemState(mods!, condition, rank, row["stolenFrom"]?.GetValue<string>(), row["deposited"]?.GetValue<bool>() ?? false), count.Value));
        }
        return entries;
    }
}
