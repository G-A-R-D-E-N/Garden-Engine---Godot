using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCw.Ui.Tests;

public class TrE2eWiringTests
{
    internal static string Source(params string[] path)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(TrE2eWiringTests).Assembly.Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "opencw", "scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "opencw" }.Concat(path).ToArray()));
    }

    [Fact]
    public void ThePackageReevaluatorDefaultsToTheSpawnerSoEvaluatePackageReachesALivePuppet()
    {
        Assert.Contains("PackageReevaluator { get; set; } = NpcPuppetSpawner.ReevaluatePackage;", Source("scripts", "PapyrusRuntime.ThirdRail.cs"));
        var spawner = Source("scripts", "Actors", "NpcPuppetSpawner.cs");
        Assert.Contains("ActorRegistry.Find(refKey)?.Node.GetNodeOrNull<PackageRunner>(PackageRunner.NodeName)", spawner);
        Assert.Contains("runner.Reevaluate();", spawner);
    }

    [Fact]
    public void TheConversationTakesItsSceneStarterFromTheRuntimeThatOwnsTheReference()
    {
        var dialogue = Source("scripts", "Actors", "NpcDialogue.cs");
        Assert.Contains("SceneStarter ?? StarterFor(reference)", dialogue);
        Assert.Contains("PapyrusRuntime.Find(reference)?.Scenes is { } scenes ? new SceneRuntimeStarter(scenes) : null", dialogue);
    }

    [Fact]
    public void TheDialogueTurnsTheNpcToThePlayerOnOpenAndRestoresTheHeadingOnEnd()
    {
        var dialogue = Source("scripts", "Actors", "NpcDialogue.cs");
        Assert.Contains("faced = NpcDialogueFacing.Begin(reference);", dialogue);
        Assert.Contains("try { NpcDialogueFacing.End(reference, faced); }", dialogue);
        var facing = Source("scripts", "Actors", "NpcDialogueFacing.cs");
        Assert.Contains("NpcAnimation.TurnTo(puppet, player.GlobalPosition, out var withClip)", facing);
        Assert.Contains("facing?.Cancel();", facing);
        Assert.Contains("NpcLocomotionStates.RestorePlan(busy, clip, turnWasRunning)", facing);
        Assert.Contains("public void Cancel() => _left = 0f;", Source("scripts", "Actors", "NpcFacing.cs"));
    }

    [Fact]
    public void TheNpcRigCacheFolderIsOneConstantAndNoOtherSourceNamesAnNpcFolder()
    {
        Assert.Contains("public const string CacheFolder = \"npc6\";", Source("scripts", "Actors", "NpcRigSharing.cs"));
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(TrE2eWiringTests).Assembly.Location)!, "..", "..", "..", "..", ".."));
        var literal = new System.Text.RegularExpressions.Regex("\"npc[0-9]+[/\"]");
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "opencw"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !file.EndsWith("NpcRigSharing.cs"))
            .Where(file => literal.IsMatch(File.ReadAllText(file))).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void TheSaveWritesAndTheLoadRestoresTheSceneStateAndRestartsThePackagesBeforeTheNpcsMove()
    {
        var save = Source("scripts", "Save", "GameSaveService.cs");
        Assert.Contains("runtime.SceneStateSections()", save);
        Assert.Contains("scenes = SceneStateSave.Read(read.Sections);", save);
        Assert.Contains("runtime.RestoreSceneState(scenes), null, LoadStage.Quests, LoadStage.Scripts);", save);
        Assert.Contains("PapyrusRuntime.Find(cellRoot)?.Session", save);
        Assert.Contains("SceneStateSave.SectionName, ScriptEventsSave.SectionName }", save);
        Assert.True(save.IndexOf("foreach (var cell in cells) RestartPackages(cell);", StringComparison.Ordinal) < save.IndexOf("foreach (var cell in cells) ApplyToCell(cell, loaded.Actors, resetUnlisted: true);", StringComparison.Ordinal));
        Assert.True(save.IndexOf("papyrus?.ClearRuntimeOverlays();", StringComparison.Ordinal) < save.IndexOf("runtime.RestoreSceneState(scenes)", StringComparison.Ordinal));
        Assert.Contains("?.RestartAfterLoad()", save);
        Assert.Contains("public void RestartAfterLoad()", Source("scripts", "Actors", "Packages", "PackageRunner.cs"));
    }

    [Fact]
    public void LeavingACellParksItsScenesBeforeTheNodesGoAndReturningQueuesThemToResume()
    {
        var runtime = Source("scripts", "PapyrusRuntime.cs");
        Assert.True(runtime.IndexOf("SuspendScenesIn(root, cell);", StringComparison.Ordinal) < runtime.IndexOf("Session?.DetachCell(cell);", StringComparison.Ordinal));
        Assert.Contains("ResumeParkedScenes(cell);", runtime);
        Assert.Contains("if (Scenes is not null) SceneResumes.Tick(delta, Scenes);", Source("scripts", "PapyrusRuntime.Scenes.cs"));
        var overlays = Source("scripts", "PapyrusRuntime.Interaction.cs");
        Assert.Contains("SceneResumes.Clear();", overlays);
        Assert.Contains("_parkedScenes.Clear();", overlays);
        Assert.Contains("CustomEvents.Clear();", overlays.Substring(overlays.IndexOf("public void ClearRuntimeOverlays()", StringComparison.Ordinal)));
    }

    [Fact]
    public void ASceneWorldPackageIsForcedOnThePackageRunnerAndEveryClearPathReleasesIt()
    {
        var world = Source("scripts", "Scenes", "SceneWorld.cs");
        Assert.Contains("Overrides.Begin(actor, package, active);", world);
        Assert.Contains("RadiclePackageSource.Instance.Forced(package)", world);
        Assert.Contains("Overrides.End(actor, package);", world);
        Assert.Contains("Overrides.Clear();", world.Substring(world.IndexOf("public void ClearAll()", StringComparison.Ordinal)));
        Assert.DoesNotContain("override absent", world);
        var runner = Source("scripts", "Actors", "Packages", "PackageRunner.cs");
        Assert.Matches(@"Overrides\.Of\(Actor\.RefKey\)\s*\?\?\s*SettlerAssignmentPolicy\.Apply\(Source\.Active\(Actor, clock\)", runner);
        Assert.Contains("overrides != _overrideRevision", runner);
        Assert.Contains("public ActivePackage? Forced(string package)", Source("scripts", "Actors", "Packages", "RadiclePackageSource.cs"));
    }

    [Fact]
    public void TheSaveWritesAndTheLoadRestoresTheScriptTimersRegistrationsAndReceiversAfterTheInstances()
    {
        var save = Source("scripts", "Save", "GameSaveService.cs");
        Assert.Contains("runtime.ScriptEventSections()", save);
        Assert.Contains("scriptEvents = ScriptEventsSave.Read(read.Sections);", save);
        Assert.Contains("runtime.RestoreScriptEvents(scriptEvents), null, LoadStage.Quests, LoadStage.Scripts);", save);
        Assert.Contains("ScriptEventsSave.SectionName }", save);
        Assert.True(save.IndexOf("SessionRestore.Register(transaction, session, instances, scripts, stages)", StringComparison.Ordinal) < save.IndexOf("runtime.RestoreScriptEvents(scriptEvents)", StringComparison.Ordinal));
        Assert.Contains("ScriptEventsSave.Restore(snapshot, Session!.Host.Instances, Timers, Registrations, CustomEvents)", Source("scripts", "PapyrusRuntime.SceneState.cs"));
    }

    [Fact]
    public void TheSaveWritesAndTheLoadValidatesThenRestoresTheGameTimeTimersAfterTheInstancesAndNewGameClearsThem()
    {
        var save = Source("scripts", "Save", "GameSaveService.cs");
        Assert.Contains("runtime.GameTimerSections()", save);
        Assert.Contains("gameTimers = GameTimersSave.Read(read.Sections);", save);
        Assert.Contains("runtime.RestoreGameTimers(gameTimers), null, LoadStage.Quests, LoadStage.Scripts);", save);
        Assert.Contains("GameTimersSave.SectionName, SceneStateSave.SectionName", save);
        Assert.True(save.IndexOf("gameTimers = GameTimersSave.Read(read.Sections);", StringComparison.Ordinal) < save.IndexOf("SessionRestore.Register(transaction, session, instances, scripts, stages)", StringComparison.Ordinal));
        Assert.True(save.IndexOf("SessionRestore.Register(transaction, session, instances, scripts, stages)", StringComparison.Ordinal) < save.IndexOf("runtime.RestoreGameTimers(gameTimers)", StringComparison.Ordinal));
        Assert.True(save.IndexOf("papyrus?.ClearRuntimeOverlays();", StringComparison.Ordinal) < save.IndexOf("runtime.RestoreGameTimers(gameTimers)", StringComparison.Ordinal));
        Assert.Contains("GameTimersSave.Restore(saved, Session!.Host.Instances, GameTimers)", Source("scripts", "PapyrusRuntime.SceneState.cs"));
        var interaction = Source("scripts", "PapyrusRuntime.Interaction.cs");
        Assert.Contains("Timers.Clear();\n        GameTimers.Clear();", interaction.Replace("\r\n", "\n"));
        var runtime = Source("scripts", "PapyrusRuntime.cs");
        Assert.Contains("ClearReferenceEvents();", runtime);
        Assert.True(runtime.IndexOf("ClearReferenceEvents();", StringComparison.Ordinal) < runtime.IndexOf("Session!.BindQuestNatives", StringComparison.Ordinal));
    }

    [Fact]
    public void ACollectionMemberCarriesTheScriptTypeOfItsRecordAndTheMessageBoxesReadTheirRecordText()
    {
        var scenes = Source("scripts", "PapyrusRuntime.Scenes.cs");
        Assert.Contains("SceneHost.Aliases, MemberHandle).Bind();", scenes);
        Assert.Contains("new EngineObjectReference(refKey) { ScriptType = type }", scenes);
        Assert.Contains("FormScriptTypes.For(signature)", scenes);
        Assert.Contains("messageText: RadicleFormLookup.MessageText", Source("scripts", "PapyrusRuntime.cs"));
        Assert.Contains("messageText: RadicleFormLookup.MessageText", Source("scripts", "UI", "Bridge", "GameUiAttach.cs"));
        Assert.Contains("records.Environment.Message(record, records.Strings)", Source("scripts", "RadicleFormLookup.cs"));
    }
}
