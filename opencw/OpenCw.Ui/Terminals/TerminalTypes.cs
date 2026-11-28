namespace OpenCommonwealth.Services.Ui.Terminals;

public enum TerminalEntryKind
{
    Submenu,
    ReturnToTop,
    Redraw,
    Text,
    Image,
    Unsupported,
}

public sealed record TerminalEntry(
    string Id,
    string Text,
    TerminalEntryKind Kind,
    string? Response = null,
    string? TargetNode = null,
    string? PageText = null,
    string? Unavailable = null);

public sealed record TerminalNode(
    string Id,
    string Header,
    string Welcome,
    string Body,
    IReadOnlyList<TerminalEntry> Entries);

public enum TerminalOpenStatus
{
    Open,
    Unavailable,
    Locked,
    LockedOut,
}

public sealed record TerminalOpening(
    TerminalOpenStatus Status,
    string RootNode = "",
    string Reason = "",
    IHackingSession? Hack = null);

public interface ITerminalSource
{
    TerminalOpening Open(string terminalId);

    TerminalNode? Node(string terminalId, string nodeId);

    bool IsValid(string terminalId);

    void EntryRun(string terminalId, string nodeId, string entryId);

    void HackFinished(string terminalId, HackingOutcome outcome);
}
