using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Lighting.Weather;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.Services.Ui.Workshop;
using OpenCommonwealth.UI.MainMenu;

namespace OpenCommonwealth;

public static class GameSaveService
{
    public const string DefaultDirectory = "user://saves";
    public const string QuickSlot = "quicksave";
    public const string AutoSlot = "autosave";

    private static readonly Dictionary<string, NpcActorState> Ledger = new(StringComparer.Ordinal);

    public static event Action? CaptureInteraction;
    public static event Action? InteractionRestored;

    public static string FilePathFor(string directory, string slot) =>
        ProjectSettings.GlobalizePath(directory.TrimEnd('/') + "/" + slot + GeSaveContainer.Extension);

    public static void ClearLedger() => Ledger.Clear();

    // Requires: main thread only; clears the singletons a scene change does not reset and a new game would otherwise inherit.
    public static void ResetForNewGame()
    {
        ClearLedger();
        CrimeLedger.Current.Clear();
        PlayerFactions.Current.Clear();
        ActorFactions.Current.Clear();
        GlobalVariables.Current.Clear();
        EngineLiveState.Store.Reset();
        PlayerStats.Current.Clear();
        PlayerExperience.Current.Clear();
        OpenCommonwealth.Services.Combat.PlayerLimbs.Current.Clear();
        OpenCommonwealth.Services.Combat.PlayerHealth.Detach();
        PlayerController.ApplyActionState(null);
        PlayerSettlements.Current.Clear();
        SettlerAssignments.Current.Clear();
        DroppedItemStates.Current.Clear();
        ContainerLedger.Current.Companions.Clear();
        WeatherRun.Start();
    }

    public static void Remember(Node cellRoot)
    {
        if (cellRoot.GetNodeOrNull<Node3D>(NpcPuppetSpawner.PuppetsName) is not { } puppets) return;
        foreach (var state in NpcActorStates.Capture(puppets)) Ledger[state.RefKey] = state;
    }

    private static Node[] SaveCells(Node? root)
    {
        if (root is null) return [];
        if (root is WorldspaceStreamer streamer) return streamer.ResidentSaveCells();
        if (root.HasMeta(CellCompiler.CellFormKeyMetadata)) return [root];
        var streamers = root.GetChildren().OfType<WorldspaceStreamer>().ToArray();
        var cells = root.GetChildren().Where(child => child.HasMeta(CellCompiler.CellFormKeyMetadata) && !child.IsQueuedForDeletion())
            .Concat(streamers.SelectMany(value => value.ResidentSaveCells())).ToArray();
        return cells.Length == 0 && streamers.Length == 0 ? [root] : cells;
    }

    public static bool Save(string slot, Node? cellRoot, string directory = DefaultDirectory) =>
        Save(slot, cellRoot, SaveCells(cellRoot), directory);

    public static bool Save(string slot, Node? cellRoot, IReadOnlyList<Node> cells, string directory = DefaultDirectory)
    {
        if (!SaveInfoFile.IsValidId(slot)) return false;
        var runtime = cellRoot is null ? null : PapyrusRuntime.Find(cellRoot);
        if (runtime?.LoadFailure is { } failure)
        {
            GD.PrintErr($"[Save] Could not save {slot}: papyrus failed: {failure}");
            return false;
        }
        if (runtime is { Loading: true })
        {
            GD.PrintErr($"[Save] Could not save {slot}: papyrus not ready");
            return false;
        }
        foreach (var cell in cells) Remember(cell);
        CaptureInteraction?.Invoke();
        byte[] bytes;
        try
        {
            var quests = runtime?.Session is { } liveSession ? SessionSave.Sections(liveSession) : CarriedQuestSections(directory, slot);
            var sections = quests.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var (sectionName, payload) in ContainerStateSave.Sections(ContainerLedger.Current, PlayerItems.Current, Time.GetTicksMsec() / 1000.0)) sections[sectionName] = payload;
            if (runtime is not null) foreach (var (sectionName, payload) in runtime.Blocks.Sections()) sections[sectionName] = payload;
            if (runtime is { Session: not null }) foreach (var (sectionName, payload) in runtime.SceneStateSections()) sections[sectionName] = payload;
            if (runtime is { Session: not null }) foreach (var (sectionName, payload) in runtime.ScriptEventSections()) sections[sectionName] = payload;
            if (runtime is { Session: not null }) foreach (var (sectionName, payload) in runtime.GameTimerSections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in ActorEnableState.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in PlayerPerks.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in PlayerFactions.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in ActorFactions.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in GlobalVariables.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in PlayerStats.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in PlayerExperience.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in OpenCommonwealth.Services.Combat.PlayerVitalsSave.Sections(OpenCommonwealth.Services.Combat.PlayerHealth.Snapshot, OpenCommonwealth.Services.Combat.PlayerLimbs.Current)) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in OpenCommonwealth.Services.Combat.ControllerActionSave.Sections(PlayerController.CaptureActionState())) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in PlayerSettlements.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in CrimeLedger.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in BuiltObjectLedger.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in SettlerAssignments.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in DroppedItemStates.Current.Sections()) sections[sectionName] = payload;
            foreach (var (sectionName, payload) in WeatherSections()) sections[sectionName] = payload;
            bytes = NpcActorSave.Pack(Ledger.Values, NpcActorSave.Codec, sections);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            GD.PrintErr($"[Save] Could not pack {slot}: {ex.Message}");
            return false;
        }
        var kind = slot == QuickSlot ? SaveKind.Quick : slot == AutoSlot ? SaveKind.Auto : SaveKind.Manual;
        var name = slot == QuickSlot ? "Quick save" : slot == AutoSlot ? "Autosave" : slot;
        var location = cellRoot == null ? "" : cellRoot.Name.ToString();
        var info = new SaveSlotInfo(slot, name, "", 0, TimeSpan.Zero, location, DateTimeOffset.Now, "", kind);
        return new DiskSaveSource(directory).WriteSlot(info, bytes);
    }

    internal static string? LoadBlockReason(Node cellRoot) => PapyrusRuntime.Find(cellRoot) switch
    {
        { LoadFailure: { } failure } => $"papyrus failed: {failure}",
        { Loading: true } => "papyrus is still loading",
        _ => null,
    };

    public static bool Load(string slot, Node cellRoot, string directory = DefaultDirectory) =>
        Load(slot, cellRoot, SaveCells(cellRoot), directory);

    public static bool Load(string slot, Node cellRoot, IReadOnlyList<Node> cells, string directory = DefaultDirectory)
    {
        if (!SaveInfoFile.IsValidId(slot)) return false;
        if (LoadBlockReason(cellRoot) is { } reason)
        {
            GD.PrintErr($"[Save] Could not load {slot}: {reason}");
            return false;
        }
        var read = GeSaveContainer.ReadFile(FilePathFor(directory, slot));
        var loaded = NpcActorSave.Decode(read, NpcActorSave.Codec);
        if (loaded.Status != GeSaveStatus.Ok)
        {
            GD.PrintErr($"[Save] Could not load {slot}: {loaded.Message}");
            return false;
        }
        OpenCommonwealth.Services.Combat.VitalsSave? vitals;
        OpenCommonwealth.Services.Combat.ControllerActionSnapshot? actionState;
        SavedWeather? weather;
        SceneStateSnapshot scenes;
        ScriptEventsSnapshot? scriptEvents;
        IReadOnlyList<SavedTimer>? gameTimers;
        try
        {
            vitals = OpenCommonwealth.Services.Combat.PlayerVitalsSave.Read(read.Sections);
            actionState = OpenCommonwealth.Services.Combat.ControllerActionSave.Read(read.Sections);
            weather = WeatherSave.Read(read.Sections);
            scenes = SceneStateSave.Read(read.Sections);
            scriptEvents = ScriptEventsSave.Read(read.Sections);
            gameTimers = GameTimersSave.Read(read.Sections);
            ContainerStateSave.Apply(read.Sections, new ContainerLedger(), new PlayerItems());
            new ActivationBlocks().Apply(read.Sections);
            new PlayerPerks().Apply(read.Sections);
            new PlayerFactions().Apply(read.Sections);
            ActorFactions.Parse(read.Sections);
            GlobalVariables.Parse(read.Sections);
            new PlayerStats().Apply(read.Sections);
            new PlayerExperience().Apply(read.Sections);
            new PlayerSettlements().Apply(read.Sections);
            new CrimeLedger().Apply(read.Sections);
            new BuiltObjectLedger().Apply(read.Sections);
            new SettlerAssignments().Apply(read.Sections);
            new DroppedItemStates().Apply(read.Sections);
            ActorEnableState.Parse(read.Sections);
        }
        catch (FormatException ex)
        {
            GD.PrintErr($"[Save] Could not load {slot}: container section: {ex.Message}");
            return false;
        }
        var papyrus = PapyrusRuntime.Find(cellRoot);
        var session = papyrus?.Session;
        QuestStateSnapshot? stages;
        QuestScriptSnapshot? scripts;
        PapyrusStateSnapshot? instances;
        try
        {
            stages = read.Sections.ContainsKey(QuestStateSave.SectionName) ? QuestStateSave.Read(read.Sections) : null;
            scripts = read.Sections.ContainsKey(QuestScriptSave.SectionName) ? QuestScriptSave.Read(read.Sections) : null;
            instances = PapyrusStateSave.Read(read.Sections);
        }
        catch (FormatException ex)
        {
            GD.PrintErr($"[Save] Could not load {slot}: quest section: {ex.Message}");
            return false;
        }
        if (session is null && (stages is not null || scripts is not null || instances is not null))
            GD.Print($"[Save] {slot}: quest sections skipped, no papyrus session");

        var transaction = new LoadTransaction();
        transaction.Add(LoadStage.State, "containers, perks, factions, globals and ledgers", () =>
        {
            ContainerStateSave.Apply(read.Sections, ContainerLedger.Current, PlayerItems.Current, Time.GetTicksMsec() / 1000.0);
            papyrus?.Blocks.Apply(read.Sections);
            PlayerPerks.Current.Apply(read.Sections);
            PlayerFactions.Current.Apply(read.Sections);
            ActorFactions.Current.Apply(read.Sections);
            GlobalVariables.Current.Apply(read.Sections);
            PlayerStats.Current.Apply(read.Sections);
            PlayerExperience.Current.Apply(read.Sections);
            PlayerSettlements.Current.Apply(read.Sections);
            CrimeLedger.Current.Apply(read.Sections);
            BuiltObjectLedger.Current.Apply(read.Sections);
            SettlerAssignments.Current.Apply(read.Sections);
            DroppedItemStates.Current.Apply(read.Sections);
        }, null, LoadStage.Quests, LoadStage.Scripts);
        var restored = session is null ? null : SessionRestore.Register(transaction, session, instances, scripts, stages);
        RestoredStateSnapshot? actorsBeforeLoad = null;
        void UndoActorsClear(Exception error)
        {
            if (papyrus is null || actorsBeforeLoad is null) return;
            try { papyrus.RestoreRestoredState(actorsBeforeLoad); }
            catch (Exception undo) { error.Data[LoadTransaction.UndoFailureKey] = undo.Message; }
        }
        transaction.Add(LoadStage.Actors, "runtime overlays and spawns", () =>
        {
            if (papyrus is not null)
                actorsBeforeLoad = papyrus.CaptureRestoredState();
            papyrus?.ClearRestoredState();
            ActorEnableState.Apply(read.Sections);
            SpawnService.Refresh();
        }, UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);
        if (papyrus is { Session: not null } runtime)
        {
            transaction.Add(LoadStage.Actors, "scene state", () => runtime.RestoreSceneState(scenes), UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);
            if (scriptEvents is not null)
                transaction.Add(LoadStage.Actors, "script events", () => runtime.RestoreScriptEvents(scriptEvents), UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);
            if (gameTimers is not null)
                transaction.Add(LoadStage.Actors, "game timers", () => runtime.RestoreGameTimers(gameTimers), UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);
        }
        transaction.Add(LoadStage.Actors, "package restart", () =>
        {
            foreach (var cell in cells) RestartPackages(cell);
        }, UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);
        transaction.Add(LoadStage.Actors, "actor ledger and cell", () =>
        {
            EngineLiveState.Store.Reset();
            PlayerXp.EnsureCurve();
            Ledger.Clear();
            foreach (var state in loaded.Actors) Ledger[state.RefKey] = state;
            foreach (var cell in cells) ApplyToCell(cell, loaded.Actors, resetUnlisted: true);
            InteractionRestored?.Invoke();
            foreach (var cell in cells)
            {
                ApplyInteraction(cell);
                BuiltObjectSpawner.Restore(cell, rebuild: true);
                DroppedRefSpawner.Restore(cell, rebuild: true);
            }
        }, UndoActorsClear, LoadStage.Quests, LoadStage.Scripts);

        var result = transaction.Run();
        if (restored?.Instances is not null)
            GD.Print($"[Save] {slot}: {restored.Instances.ToLine()}");
        if (restored?.Scripts is not null)
            LogScripts(slot, restored.Scripts);
        if (restored is { DroppedQuestStages: > 0 })
            GD.Print($"[Save] {slot}: dropped {restored.DroppedQuestStages} saved quest(s) not in this plugin set");
        foreach (var warning in result.Warnings) GD.PushWarning($"[Save] {slot}: {warning.Line}");
        foreach (var skipped in result.Skipped) GD.PrintErr($"[Save] {slot}: {skipped.Line}");
        foreach (var failure in result.Failures) GD.PrintErr($"[Save] Could not load {slot}: {failure.Line}{FailureDetail(failure.Error)}");
        if (!result.Succeeded) return false;
        PlayerController.ApplyActionState(actionState);
        OpenCommonwealth.Services.Combat.PlayerHealth.ApplySnapshot(vitals?.Vitals, PlayerExperience.Current.Level);
        OpenCommonwealth.Services.Combat.PlayerVitalsSave.Apply(vitals, null, OpenCommonwealth.Services.Combat.PlayerLimbs.Current);
        ResumeWeather(weather);
        return true;
    }

    private static string FailureDetail(Exception? error)
    {
        if (error is null) return "";
        var detail = error.Data[SessionRestore.RollbackFailureKey] is string failed ? $" (restoring the earlier instance state also failed: {failed})" : "";
        if (error.Data[QuestScriptSave.UndoFailureKey] is string undo) detail += $" (script undo failed: {undo})";
        if (error.Data[SessionRestore.RollbackUnavailableKey] is string unavailable) detail += $" (earlier state unavailable: {unavailable})";
        if (error.Data[SessionRestore.StageUndoFailureKey] is string stageUndo) detail += $" (restoring the quest stages also failed: {stageUndo})";
        if (error.Data[LoadTransaction.UndoFailureKey] is string other) detail += $" (undo failed: {other})";
        return detail;
    }

    private static void ResumeWeather(SavedWeather? weather)
    {
        if (weather is null) return;
        try
        {
            WeatherRun.Resume(weather);
            Lighting.EnvironmentController.Active?.ApplyPendingWeather();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Save] Weather was not restored: {ex.Message}");
        }
    }

    private static IReadOnlyDictionary<string, byte[]> WeatherSections() =>
        (Lighting.EnvironmentController.Active?.CaptureWeather() ?? WeatherRun.Pending) is { } saved ? WeatherSave.Sections(saved) : new Dictionary<string, byte[]>();

    public static void RestoreCell(Node cellRoot)
    {
        ApplyToCell(cellRoot, Ledger.Values, resetUnlisted: false);
        ApplyInteraction(cellRoot);
        ReferenceEnable.Register(cellRoot);
        BuiltObjectSpawner.Restore(cellRoot);
        DroppedRefSpawner.Restore(cellRoot);
    }

    public static void RestoreActor(Node3D puppet)
    {
        if (puppet.HasMeta(NpcPuppetSpawner.RefKeyMeta)
            && Ledger.TryGetValue(puppet.GetMeta(NpcPuppetSpawner.RefKeyMeta).AsString(), out var state)
            && puppet.GetParent() is { } puppets)
            NpcActorStates.Apply(puppets, [state]);
    }

    private static void ApplyInteraction(Node node)
    {
        var ledger = ContainerLedger.Current;
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D { } holder && holder.HasMeta(DoorInteraction.FormKeyMeta) && holder.GetMeta(DoorInteraction.FormKeyMeta).AsString() is { Length: > 0 } key)
            {
                if (ledger.IsTaken(key)) { holder.QueueFree(); continue; }
                if (DoorInteraction.IsDoor(holder))
                {
                    var opened = ledger.IsOpened(key);
                    holder.SetMeta(DoorInteraction.OpenMeta, opened);
                    DoorToggle.Apply(holder, opened);
                    DoorSwingPlayer.Restore(holder, opened);
                }
            }
            ApplyInteraction(child);
        }
    }

    public static bool OnCellExit(Node cellRoot, string directory = DefaultDirectory)
    {
        if (!cellRoot.IsInsideTree()) return false;
        var hasPuppets = cellRoot.GetNodeOrNull<Node3D>(NpcPuppetSpawner.PuppetsName) is { } puppets && puppets.GetChildCount() > 0;
        CaptureInteraction?.Invoke();
        var hasItems = !ContainerLedger.Current.IsEmpty || ContainerLedger.Current.Lockouts.Any(pair => pair.Value > Time.GetTicksMsec() / 1000.0) || PlayerItems.Current.Stacks.Count > 0 || !PlayerPerks.Current.IsEmpty || !PlayerFactions.Current.IsEmpty || !ActorFactions.Current.IsEmpty || !GlobalVariables.Current.IsEmpty || !PlayerStats.Current.IsEmpty || !PlayerExperience.Current.IsEmpty || !PlayerSettlements.Current.IsEmpty || !CrimeLedger.Current.IsEmpty || !BuiltObjectLedger.Current.IsEmpty || !SettlerAssignments.Current.IsEmpty || !DroppedItemStates.Current.IsEmpty;
        return (hasPuppets || hasItems || Ledger.Count > 0 || PlayerController.HasActionStateForAutosave() || OpenCommonwealth.Services.Combat.PlayerHealth.Snapshot is not null || OpenCommonwealth.Services.Combat.PlayerLimbs.Current.All.Count > 0 || QuestStagesOf(cellRoot) is { } machine && !machine.Capture().IsEmpty) && Save(AutoSlot, cellRoot, directory);
    }

    private static IReadOnlyDictionary<string, byte[]> CarriedQuestSections(string directory, string slot)
    {
        var read = GeSaveContainer.ReadFile(FilePathFor(directory, slot));
        if (read.Status is not (GeSaveStatus.Ok or GeSaveStatus.Missing))
            throw new FormatException($"existing save cannot be read: {read.Message}");
        var sections = read.Sections;
        if (sections.ContainsKey(QuestStateSave.SectionName)) QuestStateSave.Read(sections);
        if (sections.ContainsKey(QuestScriptSave.SectionName)) QuestScriptSave.Read(sections);
        PapyrusStateSave.Read(sections);
        GameTimersSave.Read(sections);
        SceneStateSave.Read(sections);
        ScriptEventsSave.Read(sections);
        return new[] { QuestStateSave.SectionName, QuestScriptSave.SectionName, PapyrusStateSave.SectionName, GameTimersSave.SectionName, SceneStateSave.SectionName, ScriptEventsSave.SectionName }.Where(sections.ContainsKey).ToDictionary(name => name, name => sections[name]);
    }

    private static void LogScripts(string slot, QuestScriptRestoreReport report)
    {
        if (report.ToLine() is { } line) GD.Print($"[Save] {slot}: {line}");
    }

    private static QuestStateMachine? QuestStagesOf(Node? cellRoot) => cellRoot is null ? null : PapyrusRuntime.Find(cellRoot)?.Session?.QuestStages;

    private static void ApplyToCell(Node cellRoot, IEnumerable<NpcActorState> states, bool resetUnlisted)
    {
        if (cellRoot.GetNodeOrNull<Node3D>(NpcPuppetSpawner.PuppetsName) is not { } puppets) return;
        var listed = states.ToList();
        var listedKeys = new HashSet<string>(listed.Select(state => state.RefKey), StringComparer.Ordinal);
        var present = puppets.GetChildren().OfType<Node3D>().Where(puppet => puppet.HasMeta(NpcPuppetSpawner.RefKeyMeta)).ToList();
        if (resetUnlisted)
        {
            foreach (var puppet in present.Where(puppet => !listedKeys.Contains(puppet.GetMeta(NpcPuppetSpawner.RefKeyMeta).AsString())))
                ResetPuppet(cellRoot, puppet);
        }
        var presentKeys = new HashSet<string>(present.Select(puppet => puppet.GetMeta(NpcPuppetSpawner.RefKeyMeta).AsString()), StringComparer.Ordinal);
        var here = listed.Where(state => presentKeys.Contains(state.RefKey)).ToList();
        var report = NpcActorStates.Apply(puppets, here);
        if (report.Mismatched.Count > 0)
            GD.PrintErr($"[Save] {report.Mismatched.Count} saved NPC(s) were for another base form and were not applied.");
    }

    // Requires: runs before ApplyToCell moves the puppets, because PackageRunner.RestartAfterLoad puts a seated puppet back where it stood before the seat.
    private static void RestartPackages(Node cellRoot)
    {
        if (cellRoot.GetNodeOrNull<Node3D>(NpcPuppetSpawner.PuppetsName) is not { } puppets) return;
        foreach (var puppet in puppets.GetChildren().OfType<Node3D>())
            puppet.GetNodeOrNull<PackageRunner>(PackageRunner.NodeName)?.RestartAfterLoad();
    }

    private static void ResetPuppet(Node cellRoot, Node3D puppet)
    {
        puppet.SetMeta(NpcPuppetSpawner.EnabledMeta, true);
        puppet.SetMeta(NpcPuppetSpawner.AliveMeta, true);
        if (!puppet.HasMeta(NpcPuppetSpawner.MarkerMeta)) return;
        if (cellRoot.GetNodeOrNull<Node3D>(puppet.GetMeta(NpcPuppetSpawner.MarkerMeta).AsNodePath()) is { } marker)
            puppet.GlobalTransform = marker.GlobalTransform;
    }
}
