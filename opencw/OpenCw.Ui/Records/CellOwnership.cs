using Radicle.Interop;

namespace OpenCommonwealth.Services.Ui.Records;

public readonly record struct CellOwner(FormKey Owner, int? Rank);

public static class CellOwnership
{
    // Requires: the CELL record is loaded; an absent or zero-local owner link is no owner.
    public static CellOwner? Read(RadicleEnvironment env, Record cell) => From(env.Cell(cell));

    public static CellOwner? From(CellInfo cell) =>
        cell.Owner is { LocalId: not 0 } owner ? new CellOwner(owner, cell.OwnerRank) : null;
}
