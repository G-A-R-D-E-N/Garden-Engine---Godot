using System;
using OpenCommonwealth.Services.Ui.Records;
using LockKind = Radicle.Interop.Native.LockKind;

namespace OpenCommonwealth.Services.Ui.Lockpick;

public interface ILockpickSessions
{
    ILockpickSession? Start(string lockId, LockLevel level);
}

// Requires: lock ids are REFR or ACHR keys and the caller has checked the lock is engaged.
public sealed class RecordLockpickRules : ILockpickRules
{
    private readonly IRecordReader _reader;
    private readonly ILockpickSessions _sessions;
    private readonly LockpickOptions _options;

    public RecordLockpickRules(IRecordReader reader, ILockpickSessions sessions, LockpickOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(sessions);
        _reader = reader;
        _sessions = sessions;
        _options = options ?? new LockpickOptions();
    }

    public LockpickEntry Begin(string lockId)
    {
        if (_reader.Lock(lockId) is not { HasLockData: true } data || Level(data.Kind) is not { } level)
            return new LockpickEntry(_options.RefusedText, null);
        var session = _sessions.Start(lockId, level);
        return session is null ? new LockpickEntry(_options.OutOfPinsText, null) : new LockpickEntry(string.Empty, session);
    }

    public static LockLevel? Level(LockKind kind) => kind switch
    {
        LockKind.Novice => LockLevel.Novice,
        LockKind.Advanced => LockLevel.Advanced,
        LockKind.Expert => LockLevel.Expert,
        LockKind.Master => LockLevel.Master,
        _ => null,
    };
}
