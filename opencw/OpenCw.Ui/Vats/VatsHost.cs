namespace OpenCommonwealth.Services.Ui.Vats;

// Requires: WeaponAccuracy is the engine's weapon accuracy score or a named conservative default (null lists no parts) and AimConeDegrees null means no cone bonus; CrippledArms counts the shooter's crippled arms.
public sealed record VatsShooter(
    float Perception,
    int CrippledArms,
    int ActionPoints,
    int MaxActionPoints,
    double Critical,
    bool CriticalReady,
    int WeaponType,
    float WeaponMaxRange,
    float? AimConeDegrees,
    float? WeaponAccuracy,
    int Rounds = int.MaxValue);

public sealed record VatsCandidate(
    string Id,
    string Name,
    int Level,
    double Health,
    bool Alive,
    string BaseKey,
    float Distance,
    IReadOnlyList<VatsResistance> Resistances);

public interface IVatsScene
{
    string? InitialTargetId { get; }

    VatsShooter? Shooter();

    IReadOnlyList<VatsCandidate> Candidates();

    // Invariant: null means the part cannot be offered (not in line of sight, no screen point or no usable AP cost).
    VatsPartState? PartState(VatsCandidate candidate, VatsBodyPart part);

    bool StillValid();

    VatsPoint? ViewSize => null;
}

public sealed class VatsHost(IVatsScene scene, IBodyPartSource bodyParts, VatsChanceSettings settings, TimeProvider? clock = null) : IVatsTargets
{
    public const double RetrySeconds = 1.0;

    public const int MaxAttempts = 8;

    private readonly Dictionary<string, IReadOnlyList<VatsBodyPart>> _parts = new();
    private readonly Dictionary<string, (int Attempts, long Stamp)> _unconfirmed = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public string? InitialTargetId => scene.InitialTargetId;

    // Invariant: a target is listed only with at least one offered part; no shooter, no accuracy or a melee weapon list none.
    public VatsSnapshot Snapshot()
    {
        if (scene.Shooter() is not { } shooter)
            return new VatsSnapshot([], 0, 0, 0, false, false);
        var targets = new List<VatsTarget>();
        foreach (var c in scene.Candidates())
        {
            var parts = PartsOf(shooter, c);
            if (parts.Count > 0)
                targets.Add(new VatsTarget(c.Id, c.Name, c.Level, c.Health, c.Alive, c.Resistances, parts));
        }
        var view = scene.ViewSize ?? new VatsPoint(1920, 1080);
        return new VatsSnapshot(targets, shooter.ActionPoints, shooter.MaxActionPoints, shooter.Critical, shooter.CriticalReady, scene.StillValid(), view.X, view.Y, shooter.Rounds);
    }

    private IReadOnlyList<VatsPart> PartsOf(VatsShooter shooter, VatsCandidate c)
    {
        if (!c.Alive || shooter.WeaponAccuracy is not { } accuracy)
            return [];
        var body = BodyPartsOf(c.BaseKey);
        var aim = new VatsAim(c.Distance, shooter.WeaponType, shooter.WeaponMaxRange, shooter.AimConeDegrees, accuracy, shooter.Perception, shooter.CrippledArms);
        return VatsPartChances.Build(settings, body, aim, part => scene.PartState(c, part)) ?? [];
    }

    // Invariant: a null read is unconfirmed (not cached) and is retried at most every RetrySeconds, MaxAttempts times per base key; any non-null read, even empty, is final.
    private IReadOnlyList<VatsBodyPart> BodyPartsOf(string baseKey)
    {
        if (_parts.TryGetValue(baseKey, out var known))
            return known;
        var now = _clock.GetTimestamp();
        if (_unconfirmed.TryGetValue(baseKey, out var last) && (last.Attempts >= MaxAttempts || _clock.GetElapsedTime(last.Stamp, now).TotalSeconds < RetrySeconds))
            return [];
        if (bodyParts.Parts(baseKey) is { } rows)
            return _parts[baseKey] = VatsBodyParts.Targetable(rows);
        _unconfirmed[baseKey] = (last.Attempts + 1, now);
        return [];
    }
}
