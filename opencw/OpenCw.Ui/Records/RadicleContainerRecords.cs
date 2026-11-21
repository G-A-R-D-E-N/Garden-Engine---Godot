using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Trade;
using Radicle.Interop;
using LockKind = Radicle.Interop.Native.LockKind;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class RadicleContainerRecords : IContainerRecords
{
    public const int MaxItemRuns = 4096;

    private readonly IRecordSource _source;
    private readonly uint _playerLevel;
    private readonly Func<FormKey, int, bool>? _ownerAllows;

    public RadicleContainerRecords(IRecordSource source, uint playerLevel, Func<FormKey, int, bool>? ownerAllows = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _playerLevel = Math.Max(1u, playerLevel);
        _ownerAllows = ownerAllows;
    }

    public RadicleContainerRecords(RadicleEnvironment env, StringTables strings, uint playerLevel, Func<FormKey, int, bool>? ownerAllows = null)
        : this(new PairRecordSource(env, strings), playerLevel, ownerAllows)
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    private StringTables _strings => _source.Strings;

    public ContainerSetup? Read(string refKey, string baseKey)
    {
        try
        {
            if (Find(refKey) is not { } placed || Find(baseKey) is not { } cont || _env.Info(cont).Signature != "CONT") return null;
            var reference = _env.PlacedReference(placed);
            var (owner, rank) = ContainerOwnership.Effective(reference.Owner, reference.FactionRank, () => CellOwnerOf(placed));
            var owned = ContainerOwnership.IsTheft(owner, reference.NoCrime, rank, OwnerAllows);
            var lockData = _env.Lock(placed);
            var locked = lockData.HasLockData && lockData.Kind != LockKind.None;
            var info = _env.Info(cont);
            var definition = _env.ContainerDefinition(cont, _strings);
            var title = definition.FullName.Text is { Length: > 0 } name ? name : info.NameText;
            var stacks = Expand(Runs(definition.Items), refKey);
            return new ContainerSetup(string.IsNullOrWhiteSpace(title) ? info.EditorId ?? baseKey : title, owned, locked, stacks, owned && owner is { } who ? RecordKey.Format(who) : null);
        }
        catch (Exception e) when (e is RadicleException or InvalidOperationException)
        {
            return null;
        }
    }

    internal IReadOnlyList<InventoryStack> Expand(IEnumerable<InventoryStack> runs, string refKey) =>
        ContainerContents.Expand(runs, refKey, IsLeveled, Pick);

    internal static List<InventoryStack> Runs(IEnumerable<ItemAmount> rows) =>
        rows.Take(MaxItemRuns).Select(row => new InventoryStack(RecordKey.Format(row.Item), row.Count)).ToList();

    private bool IsLeveled(string id) => Find(id) is { } record && _env.Info(record).Signature == "LVLI";

    private IReadOnlyList<InventoryStack> Pick(string listId, int count, ulong seed)
    {
        if (!RecordKey.TryParse(listId, out var plugin, out var local)) return [];
        var outcome = _env.LeveledPickCount(new FormKey(plugin, local), _playerLevel, seed, (uint)Math.Clamp(count, 1, 32767));
        if (outcome.Reason != LeveledPickReason.Picked) return [];
        var rows = outcome.EntryCounts.Count == outcome.Entries.Count;
        var entries = new List<(string Key, int Row)>(outcome.Entries.Count);
        for (var i = 0; i < outcome.Entries.Count; i++) entries.Add((RecordKey.Format(outcome.Entries[i]), rows ? outcome.EntryCounts[i] : 1));
        return ContainerContents.Stacks(entries, outcome.Multiplier);
    }

    private CellOwner? CellOwnerOf(Record placed)
    {
        try { return _env.Container(placed).Cell is { } cell && _env.Winner(cell) is { } record ? CellOwnership.Read(_env, record) : null; }
        catch (RadicleException) { return null; }
    }

    private bool OwnerAllows(FormKey owner, int rank)
    {
        try { return _ownerAllows?.Invoke(owner, rank) == true; }
        catch (RadicleException) { return false; }
    }

    private Record? Find(string id) =>
        RecordKey.TryParse(id, out var plugin, out var local) ? _env.Winner(new FormKey(plugin, local)) : null;
}

public static class ContainerOwnership
{
    // Constraint: a reference with no owner of its own takes its parent cell's owner and faction rank; the cell carries no no-crime flag.
    public static (FormKey? Owner, int? Rank) Effective(FormKey? owner, int? rank, Func<CellOwner?> cell) =>
        owner is not null ? (owner, rank) : cell() is { } inherited ? (inherited.Owner, inherited.Rank) : (null, rank);

    // Constraint: a reference owner counts as theft unless NoCrime is set or the player passes the owner rule for the stored faction rank.
    public static bool IsTheft(FormKey? owner, bool? noCrime, int? factionRank, Func<FormKey, int, bool>? ownerAllows) =>
        owner is { } who && noCrime != true && ownerAllows?.Invoke(who, factionRank ?? 0) != true;
}
