using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class SceneRuntimeStarter(SceneRuntime scenes) : ISceneStarter
{
    public EffectOutcome Start(FormKey scene) => Outcome(scenes.Start(RecordKey.Format(scene)));

    public bool RunsScene(FormKey scene) => scenes.RunsItself(RecordKey.Format(scene));

    public EffectOutcome StartHosted(FormKey scene) => Outcome(scenes.StartHosted(RecordKey.Format(scene)));

    public void Phase(FormKey scene, int phase) => scenes.HostPhase(RecordKey.Format(scene), phase);

    public void End(FormKey scene) => scenes.HostEnd(RecordKey.Format(scene));

    public void Cancel(FormKey scene) => scenes.StopHosted(RecordKey.Format(scene));

    private static EffectOutcome Outcome(SceneStartResult result)
    {
        var applied = result.Outcome is SceneStartOutcome.Started or SceneStartOutcome.AlreadyPlaying;
        return new EffectOutcome("scene-start", applied ? EffectStatus.Applied : EffectStatus.Refused, result.Detail);
    }
}
