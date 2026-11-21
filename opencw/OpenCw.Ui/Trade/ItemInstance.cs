using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Trade;

// Constraint: immutable; equal states share a stack, Plain is the state of ids without instance data. StolenFrom is the owner key the copy was taken from without permission; Deposited marks a copy the player put into an owned container, so taking it back is not a theft.
public sealed class ItemState : IEquatable<ItemState>
{
    public const double FullCondition = 1;

    public static ItemState Plain { get; } = new([]);

    public ItemState(IEnumerable<string> mods, double condition = FullCondition, int rank = 0, string? stolenFrom = null, bool deposited = false)
    {
        Mods = mods.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (Mods.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Mods.Count) throw new ArgumentException("duplicate mod", nameof(mods));
        if (!double.IsFinite(condition) || condition < 0 || condition > FullCondition) throw new ArgumentOutOfRangeException(nameof(condition));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Condition = condition;
        Rank = rank;
        StolenFrom = string.IsNullOrWhiteSpace(stolenFrom) ? null : stolenFrom;
        Deposited = deposited;
    }

    public IReadOnlyList<string> Mods { get; }

    public double Condition { get; }

    public int Rank { get; }

    public string? StolenFrom { get; }

    public bool Deposited { get; }

    public bool IsPlain => Mods.Count == 0 && Condition == FullCondition && Rank == 0 && StolenFrom is null && !Deposited;

    public bool Has(string mod) => Mods.Contains(mod, StringComparer.OrdinalIgnoreCase);

    public ItemState With(string mod) => new(Mods.Append(mod), Condition, Rank, StolenFrom, Deposited);

    public ItemState Without(string mod) => new(Mods.Where(held => !string.Equals(held, mod, StringComparison.OrdinalIgnoreCase)), Condition, Rank, StolenFrom, Deposited);

    public ItemState WithCondition(double condition) => new(Mods, condition, Rank, StolenFrom, Deposited);

    public ItemState WithRank(int rank) => new(Mods, Condition, rank, StolenFrom, Deposited);

    public ItemState WithStolenFrom(string? owner) => new(Mods, Condition, Rank, owner, Deposited);

    public ItemState WithDeposited(bool deposited) => new(Mods, Condition, Rank, StolenFrom, deposited);

    public bool Equals(ItemState? other) =>
        other is not null && Condition == other.Condition && Rank == other.Rank && Deposited == other.Deposited
        && string.Equals(StolenFrom, other.StolenFrom, StringComparison.OrdinalIgnoreCase)
        && Mods.SequenceEqual(other.Mods, StringComparer.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ItemState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Condition);
        hash.Add(Rank);
        hash.Add(Deposited);
        hash.Add(StolenFrom, StringComparer.OrdinalIgnoreCase);
        foreach (var mod in Mods) hash.Add(mod, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

public sealed record ItemEntry(string Id, ItemState State, int Count);
