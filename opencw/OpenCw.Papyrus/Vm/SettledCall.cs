namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record SettledCall(ulong Ticket, ScriptInstance Instance, string Function, VmResult Result);
