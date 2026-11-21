namespace OpenCommonwealth.Services.Ui.Vats;

public readonly record struct VatsPoint(double X, double Y);

public sealed record VatsPart(
    string Id,
    string Name,
    int HitChance,
    double Health,
    bool Crippled,
    int ApCost,
    VatsPoint? Screen = null);

public sealed record VatsResistance(string Label, string Value, bool Immune = false);

public sealed record VatsTarget(
    string Id,
    string Name,
    int Level,
    double Health,
    bool Alive,
    IReadOnlyList<VatsResistance> Resistances,
    IReadOnlyList<VatsPart> Parts);

public sealed record VatsSnapshot(
    IReadOnlyList<VatsTarget> Targets,
    int ActionPoints,
    int MaxActionPoints,
    double Critical,
    bool CriticalReady,
    bool StillValid,
    double ViewWidth = 1920,
    double ViewHeight = 1080,
    int Rounds = int.MaxValue);

public sealed record VatsQueued(string TargetId, string PartId, int Cost);

public interface IVatsTargets
{
    string? InitialTargetId => null;

    int MaxQueue => 16;

    VatsSnapshot Snapshot();
}

public enum VatsPhase
{
    Targeting,
    Playback,
    Closed,
}

public enum VatsOutcome
{
    Cancelled,
    Executed,
    Aborted,
    Interrupted,
    Failed,
}

public enum VatsCommand
{
    PartUp,
    PartDown,
    Queue,
    CycleNext,
    CyclePrevious,
    Return,
    Accept,
    Critical,
}
