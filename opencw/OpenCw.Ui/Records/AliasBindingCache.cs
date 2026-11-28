using OpenCommonwealth.Services.Ui.TextTags;

namespace OpenCommonwealth.Services.Ui.Records;

public sealed class AliasBindingCache
{
    private int _session = -1;
    private int _failedSession = -1;
    private ReferenceTagResolvers? _resolvers;
    private AliasBinder? _binder;

    public AliasBinder? Binder => _binder;

    public int SessionForTests => _session;

    public ReferenceTagResolvers? CachedIfCurrentSession(bool hasLoadOrder, int sessionId)
    {
        if (!hasLoadOrder)
            return null;
        return TryGetCached(sessionId, out var resolvers) ? resolvers : null;
    }

    public ReferenceTagResolvers? Resolve(
        bool hasLoadOrder,
        int sessionId,
        object? activeRecords,
        object? recordEnvironment,
        object? liveEnvironment,
        Func<object, (ReferenceTagResolvers Resolvers, AliasBinder Binder)> build)
    {
        if (!hasLoadOrder)
            return null;
        if (CachedIfCurrentSession(true, sessionId) is { } cached)
            return cached;
        return RebuildWhenStale(sessionId, activeRecords, recordEnvironment, liveEnvironment, build);
    }

    public ReferenceTagResolvers? RebuildWhenStale(
        int sessionId,
        object? activeRecords,
        object? recordEnvironment,
        object? liveEnvironment,
        Func<object, (ReferenceTagResolvers Resolvers, AliasBinder Binder)> build)
    {
        if (TryGetCached(sessionId, out var cached))
            return cached;
        if (UiRecordSession.IsCurrentSession(_failedSession, sessionId))
            return null;
        if (activeRecords is null || !UiRecordSession.IsLiveEnvironment(recordEnvironment, liveEnvironment))
            return null;
        _session = sessionId;
        _failedSession = -1;
        _resolvers = null;
        _binder = null;
        (ReferenceTagResolvers Resolvers, AliasBinder Binder) built;
        try
        {
            built = build(activeRecords);
        }
        catch
        {
            _failedSession = sessionId;
            throw;
        }
        _binder = built.Binder;
        _resolvers = built.Resolvers;
        return _resolvers;
    }

    public static string? NameFor(object heldRecords, object? activeRecords, Func<string?> lookup) =>
        UiRecordSession.IsSameRecords(heldRecords, activeRecords) ? lookup() : null;

    private bool TryGetCached(int sessionId, out ReferenceTagResolvers? resolvers)
    {
        if (UiRecordSession.IsCurrentSession(_session, sessionId) && _resolvers is not null)
        {
            resolvers = _resolvers;
            return true;
        }
        resolvers = null;
        return false;
    }
}