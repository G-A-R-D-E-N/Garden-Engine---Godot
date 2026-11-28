using System;
using OpenCommonwealth.Services.Ui.Lockpick;
using OpenCommonwealth.Services.Ui.Terminals;
using Radicle.Interop;
using LockKind = Radicle.Interop.Native.LockKind;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class RadicleTerminalRecords : ITerminalRecords
{
    public const string LockedText = "This terminal is locked.";

    private readonly IRecordSource _source;
    private readonly ILockFacts? _facts;

    public RadicleTerminalRecords(IRecordSource source, ILockFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _facts = facts;
    }

    public RadicleTerminalRecords(RadicleEnvironment env, StringTables strings, ILockFacts? facts = null)
        : this(new PairRecordSource(env, strings), facts)
    {
    }

    private RadicleEnvironment _env => _source.Environment;

    private StringTables _strings => _source.Strings;

    public TerminalRecord? Terminal(string baseKey)
    {
        try
        {
            return Find(baseKey) is { } record && _env.Info(record).Signature == "TERM" ? _env.Terminal(record, _strings) : null;
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    public TerminalPlacement? Placement(string refKey)
    {
        try
        {
            if (Find(refKey) is not { } placed || _env.PlacedReference(placed).Base is not { } baseKey)
                return null;
            var lockData = _env.Lock(placed);
            var key = RecordKey.Format(baseKey);
            if (!lockData.HasLockData || lockData.Kind == LockKind.None)
                return new TerminalPlacement(key, TerminalLockState.Unlocked);
            return lockData.Kind is >= LockKind.Novice and <= LockKind.Master
                ? new TerminalPlacement(key, TerminalLockState.Password, (int)lockData.Kind - (int)LockKind.Novice, HasKey: _facts?.HasKey(refKey) ?? false)
                : new TerminalPlacement(key, TerminalLockState.Refused, RefusedReason: LockedText, HasKey: _facts?.HasKey(refKey) ?? false);
        }
        catch (RadicleException)
        {
            return null;
        }
    }

    private Record? Find(string id) =>
        RecordKey.TryParse(id, out var plugin, out var local) ? _env.Winner(new FormKey(plugin, local)) : null;
}
