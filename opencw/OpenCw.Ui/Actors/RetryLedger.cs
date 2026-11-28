namespace OpenCommonwealth;

public sealed class RetryLedger(Func<double>? clock = null)
{
    public const int MaxAttempts = 3;
    public const double CooldownSeconds = 30;

    public sealed record RetryBatch(string InstallRoot, IReadOnlyList<string> Entries);

    private readonly Dictionary<string, (int Count, double Next)> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<double> _now = clock ?? (() => Environment.TickCount64 / 1000.0);

    private bool Due(string key, double now) =>
        !_attempts.TryGetValue(key, out var a) || (a.Count < MaxAttempts && now >= a.Next);

    private bool ClaimDue(string key, double now)
    {
        if (!Due(key, now)) return false;
        _attempts[key] = (_attempts.GetValueOrDefault(key).Count + 1, now + CooldownSeconds);
        return true;
    }

    public bool HasUnclaimed(string skeletonEntry, IEnumerable<string> entries)
    {
        double now = _now();
        lock (_attempts) return entries.Any(e => Due(skeletonEntry + "|" + e, now));
    }

    public List<string> Claim(string skeletonEntry, IEnumerable<string> entries)
    {
        double now = _now();
        lock (_attempts) return entries.Where(e => ClaimDue(skeletonEntry + "|" + e, now)).ToList();
    }

    public RetryBatch? TryClaim(string skeletonEntry, IEnumerable<string> pending, Func<string?> installRoot)
    {
        double now = _now();
        lock (_attempts)
        {
            var due = pending.Where(e => Due(skeletonEntry + "|" + e, now)).ToList();
            if (due.Count == 0 || installRoot() is not { } root) return null;
            foreach (string entry in due) ClaimDue(skeletonEntry + "|" + entry, now);
            return new RetryBatch(root, due);
        }
    }
}
