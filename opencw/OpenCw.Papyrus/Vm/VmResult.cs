namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record VmResult(
    VmStopReason Reason,
    PapyrusValue Value,
    string? Key,
    string? Message,
    int Instructions,
    ulong Ticket = 0)
{
    public bool Completed => Reason == VmStopReason.Completed;
}
