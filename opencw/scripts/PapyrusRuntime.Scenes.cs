using Godot;
using System;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Scenes;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth;

public partial class PapyrusRuntime
{
    public AdapterReport? SceneNatives { get; private set; }
    public AdapterReport? AliasNatives { get; private set; }
    public AdapterReport? CollectionAliasNatives { get; private set; }
    public SceneRuntime? Scenes { get; private set; }
    public SceneWorld? SceneHost { get; private set; }

    // Requires: only collection members are typed; alias answers stay untyped for the cast hook.
    private static EngineObjectReference MemberHandle(string refKey)
    {
        var registered = ActorRegistry.Find(refKey);
        var type = registered is not null ? "Actor" : RadicleFormLookup.Resolve(refKey)?.Signature is { } signature ? FormScriptTypes.For(signature) : null;
        return registered?.Owner is { } owner ? new EngineObjectReference(owner) { ScriptType = type } : new EngineObjectReference(refKey) { ScriptType = type };
    }

    // Requires: runs before BindThirdRailNatives, because registration is first-wins and UnsupportedNativeAdapters must not shadow these.
    private void BindSceneNatives()
    {
        SceneHost = new SceneWorld(Session!);
        Scenes = new SceneRuntime(key => RadicleSceneSource.Read(GameLoader.Records.Environment, key), SceneHost, message => GD.Print($"[Scene] {message}"));
        SceneNatives = new SceneNativeAdapters(Session!.Host, Session.Gate, Scenes, SceneHost, message => GD.Print($"[Scene] {message}")).Bind();
        Func<string, EngineObjectReference?> reference = refKey => ActorRegistry.Find(refKey)?.Owner is { } owner ? new EngineObjectReference(owner) : new EngineObjectReference(refKey);
        AliasNatives = new AliasNativeAdapters(Session.Host, Session.Gate, SceneHost.Aliases, reference).Bind();
        CollectionAliasNatives = new CollectionAliasNativeAdapters(Session.Host, Session.Gate, SceneHost.Aliases, MemberHandle).Bind();
        PrintUnbound(SceneNatives);
        PrintUnbound(AliasNatives);
        PrintUnbound(CollectionAliasNatives);
    }

    // Requires: main thread, before RunQueuedFragments, so a stage set by a scene end runs its fragments in the same frame.
    private void TickScenes(double delta)
    {
        try { SceneHost?.Tick(delta); }
        catch (Exception error) { GD.PushError($"[Scene] speech tick failed: {error}"); }
        try { if (Scenes is not null) SceneResumes.Tick(delta, Scenes); }
        catch (Exception error) { GD.PushError($"[Scene] resume failed: {error}"); }
        try { Scenes?.Tick(delta); }
        catch (Exception error) { GD.PushError($"[Scene] tick failed: {error}"); }
    }
}
