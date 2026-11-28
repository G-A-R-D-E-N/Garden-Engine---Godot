namespace OpenCommonwealth.Services.Papyrus.Save;

public sealed record RestoredStateSnapshot(ScriptEventsSnapshot ScriptEvents, IReadOnlyList<SavedTimer> GameTimers, SceneStateSnapshot Scenes);