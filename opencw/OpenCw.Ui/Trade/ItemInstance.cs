using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Trade;

// Constraint: immutable; equal states share a stack, Plain is the state of ids without instance data.
public sealed class ItemState : IEquatable<ItemState>
{
    public const double FullCondition = 1;

    public static ItemState Plain { get; } = new([]);

    public ItemState(IEnumerable<string> mods, double condition = FullCondition, int rank = 0)
    {
        Mods = mods.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (Mods.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Mods.Count) throw new ArgumentException("duplicate mod", nameof(mods));
        if (!double.IsFinite(condition) || condition < 0 || condition > FullCondition) throw new ArgumentOutOfRangeException(nameof(condition));
        if (rank < 0) throw new ArgumentOutOfRangeException(nameof(rank));
        Condition = condition;
        Rank = rank;
    }

    public IReadOnlyList<string> Mods { get; }

    public double Condition { get; }

    public int Rank { get; }

    public bool IsPlain => Mods.Count == 0 && Condition == FullCondition && Rank == 0;

    public bool Has(string mod) => Mods.Contains(mod, StringComparer.OrdinalIgnoreCase);

    public ItemState With(string mod) => new(Mods.Append(mod), Condition, Rank);

    public ItemState Without(string mod) => new(Mods.Where(held => !string.Equals(held, mod, StringComparison.OrdinalIgnoreCase)), Condition, Rank);

    public ItemState WithCondition(double condition) => new(Mods, condition, Rank);

    public ItemState WithRank(int rank) => new(Mods, Condition, rank);

    public bool Equals(ItemState? other) =>
        other is not null && Condition == other.Condition && Rank == other.Rank
        && Mods.SequenceEqual(other.Mods, StringComparer.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ItemState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Condition);
        hash.Add(Rank);
        foreach (var mod in Mods) hash.Add(mod, StringComparer.OrdinalIgnoreCase);
        return hash.ToHashCode();
    }
}

public sealed record ItemEntry(string Id, ItemState State, int Count);
