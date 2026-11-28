using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Trade;

public enum ContainerMode
{
    Loot,
    Steal,
    Pickpocket,
    Teammate,
    PowerArmor,
    Reload,
    Workbench,
}

public enum ContainerSide
{
    Container,
    Player,
}

public enum ContainerOutcome
{
    Open,
    Closed,
    Caught,
}

public enum TransferStatus
{
    Done,
    Refused,
    Caught,
}

public sealed record ContainerStack(string Id, string Name, int Count, double Weight = 0, int Value = 0, bool MoveWholeStack = false);

public sealed record ContainerTransferResult(TransferStatus Status, string Reason = "");

public interface IContainerRules
{
    ContainerMode Mode { get; }

    string Title { get; }

    string PlayerListHeader { get; }

    double CarryLimit { get; }

    int Revision { get; }

    IReadOnlyList<ContainerStack> ContainerStacks { get; }

    IReadOnlyList<ContainerStack> PlayerStacks { get; }

    string Refusal(ContainerSide from, string itemId, int count);

    int Chance(ContainerSide from, string itemId);

    int Chance(ContainerSide from, string itemId, int count) => Chance(from, itemId);

    ContainerTransferResult Transfer(ContainerSide from, string itemId, int count);
}
