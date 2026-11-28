using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed class SceneNativeAdapters(IScriptHost host, EvidenceGate gate, SceneRuntime scenes, ISceneWorld world, Action<string> log)
{
    public const string SystemKey = "system.scene";

    public static readonly NativeId StartNative = new("Scene", "Start");
    public static readonly NativeId SayNative = new("ObjectReference", "Say");

    private static readonly string[] SceneFunctions = { "Start", "ForceStart", "Stop", "IsPlaying", "GetOwningQuest" };

    public AdapterReport Bind()
    {
        // Requires: this gate call stays; it counts the reliance on the system key.
        gate.Gate(SystemKey);
        var outcomes = new List<AdapterOutcome>();
        foreach (var function in SceneFunctions)
        {
            var name = function;
            outcomes.Add(Register(new NativeId("Scene", name), (receiver, arguments) => Scene(name, receiver, arguments)));
        }
        outcomes.Add(Register(SayNative, Say));
        var bound = outcomes.FindAll(outcome => outcome.State == AdapterState.Bound).Count;
        return new AdapterReport(outcomes, bound, outcomes.Count - bound);
    }

    private AdapterOutcome Register(NativeId native, Func<PapyrusValue, IReadOnlyList<PapyrusValue>, PapyrusValue> handler)
    {
        var family = new GameplayNativeFamily("scene", "Garden.Scene", SystemKey, native);
        return host.TryRegisterNative(native, handler, NativeTargetKind.EngineObject)
            ? new AdapterOutcome(family, AdapterState.Bound, "bound to the scene runtime")
            : new AdapterOutcome(family, AdapterState.Conflict, "registration conflict: the existing handler was preserved");
    }

    internal static string FormKeyOf(PapyrusValue value)
    {
        if (value.Kind != PapyrusValueKind.Object || value.Reference is not EngineObjectReference reference)
            throw new InvalidOperationException("the value is not a form");
        if (reference.Owner is { } owner)
            return owner.Kind == PapyrusOwnerKind.Form ? owner.BaseFormKey : throw new InvalidOperationException($"the value owner kind {owner.Kind} is not a form");
        if (!reference.Identity.Contains(':') || reference.Identity.Contains('#'))
            throw new InvalidOperationException("the value identity is not a form key");
        return reference.Identity;
    }

    private PapyrusValue Scene(string function, PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count != 0) throw new InvalidOperationException($"Scene.{function} takes no arguments");
        var scene = FormKeyOf(receiver);
        switch (function)
        {
            case "Stop":
                scenes.Stop(scene);
                return PapyrusValue.None;
            case "IsPlaying":
                return PapyrusValue.FromBool(scenes.IsPlaying(scene));
            case "GetOwningQuest":
                return scenes.QuestOf(scene) is { } quest ? PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(quest))) : PapyrusValue.None;
        }
        var result = scenes.Start(scene, force: function == "ForceStart");
        log($"Scene.{function} {result.Outcome}: {result.Detail}");
        if (result.Outcome is SceneStartOutcome.Unknown or SceneStartOutcome.Refused)
            throw new InvalidOperationException($"Scene.{function}: {result.Detail}");
        return PapyrusValue.None;
    }

    private PapyrusValue Say(PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        if (arguments.Count is < 1 or > 4) throw new InvalidOperationException("ObjectReference.Say takes a topic and up to three more arguments");
        if (arguments.Count > 2 && arguments[2].Flag) throw new InvalidOperationException("ObjectReference.Say: speaking in the player's head is not supported");
        var topic = FormKeyOf(arguments[0]);
        var speaker = arguments.Count > 1 && arguments[1].Kind == PapyrusValueKind.Object ? EnableNativeAdapters.RefKey(arguments[1]) : EnableNativeAdapters.RefKey(receiver);
        var result = world.Speak($"ObjectReference.Say topic {topic}", topic, speaker);
        log($"ObjectReference.Say {speaker} topic={topic} {result.Status} seconds={result.Seconds:F2} {result.Detail}");
        return PapyrusValue.None;
    }
}
