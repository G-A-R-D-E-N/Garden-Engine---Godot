namespace OpenCommonwealth.Services.Ui.Vats;

public enum VatsShotResult
{
    Hit,
    Miss,
    Skipped,
}

public readonly record struct VatsShot(VatsQueued Action, int Chance, VatsShotResult Result);

// Requires: main thread; ApplyDamage is the only damage seam and carries no VATS multiplier (none is evidenced).
public interface IVatsShotSink
{
    bool CanTarget(string targetId);

    bool TryConsumeRound();

    void SpendActionPoints(int cost);

    void ApplyDamage(string targetId, string partId);
}

public static class VatsExecution
{
    // Invariant: a shot is skipped (no AP, no roll, no damage) when the part is not in the snapshot, the target can no longer be shot or the clip is empty; otherwise one round is consumed, AP is spent, one roll is drawn and the shot hits when roll * 100 < chance, so 0 never hits and 100 always does for a roll in [0, 1).
    public static IReadOnlyList<VatsShot> Run(IReadOnlyList<VatsQueued> queue, VatsSnapshot snapshot, IVatsShotSink sink, Func<double> roll)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(roll);
        var shots = new List<VatsShot>(queue.Count);
        foreach (var action in queue)
            shots.Add(Resolve(action, snapshot, sink, roll));
        return shots;
    }

    public static VatsShot Resolve(VatsQueued action, VatsSnapshot snapshot, IVatsShotSink sink, Func<double> roll)
    {
        var part = snapshot.Targets.FirstOrDefault(t => t.Id == action.TargetId)?.Parts.FirstOrDefault(p => p.Id == action.PartId);
        if (part is null || !sink.CanTarget(action.TargetId) || !sink.TryConsumeRound())
            return new VatsShot(action, 0, VatsShotResult.Skipped);
        var chance = VatsMenuModel.DisplayChance(part.HitChance);
        sink.SpendActionPoints(action.Cost);
        var hit = roll() * 100 < chance;
        if (hit)
            sink.ApplyDamage(action.TargetId, action.PartId);
        return new VatsShot(action, chance, hit ? VatsShotResult.Hit : VatsShotResult.Miss);
    }
}
