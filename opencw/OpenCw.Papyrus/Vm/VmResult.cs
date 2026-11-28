namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record VmResult(
    VmStopReason Reason,
    PapyrusValue Value,
    string? Key,
    string? Message,
    int Instructions)
{
    public bool Completed => Reason == VmStopReason.Completed;
}
