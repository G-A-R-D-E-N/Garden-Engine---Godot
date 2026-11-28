using System.Collections.Generic;

namespace OpenCommonwealth.Services.Papyrus.Scenes;

public enum SceneActionKind
{
    Dialogue,
    Package,
    Timer,
    PlayerDialogue,
}

public sealed record SceneActionDef(string Name, SceneActionKind Kind, int Alias, int StartPhase, int EndPhase, bool IgnoreForCompletion, string? Target, double Seconds);

public sealed record ScenePhaseDef(int Index, string Name, int? StartStage, int? CompletionStage);

public sealed record SceneDef(string Key, string Quest, IReadOnlyList<ScenePhaseDef> Phases, IReadOnlyList<SceneActionDef> Actions,
    int? BeginStage, int? EndStage, string? Refusal = null, string? ConditionRefusal = null, string? HostRefusal = null, bool PhaseConditionsOnly = false);

public enum SpeechStatus
{
    Spoken,
    NoLine,
    MissingAudio,
    ActorAbsent,
}

public sealed record SpeechResult(SpeechStatus Status, double Seconds, string Detail);

public sealed record PackageResult(bool Applied, string Detail);

public interface ISceneWorld
{
    string? ResolveActor(string quest, int alias);

    SpeechResult Speak(string context, string topic, string? actor);

    void StopSpeech(string context, string actor);

    PackageResult BeginPackage(string context, string package, string? actor);

    void EndPackage(string package, string? actor);

    bool SetStage(string quest, int stage);
}

internal sealed class FormKeyComparer : IEqualityComparer<string>
{
    public static readonly FormKeyComparer Instance = new();

    public bool Equals(string? a, string? b) =>
        a == b || a is not null && b is not null && RadicleSceneSource.TryParseKey(a, out var left) && RadicleSceneSource.TryParseKey(b, out var right) && left == right;

    public int GetHashCode(string key) => RadicleSceneSource.TryParseKey(key, out var form) ? form.GetHashCode() : key.GetHashCode();
}
