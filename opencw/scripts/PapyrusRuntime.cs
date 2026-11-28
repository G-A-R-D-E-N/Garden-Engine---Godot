using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Papyrus;
using OpenCommonwealth.Services.Papyrus.Actors;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.LiveState;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.UI.Bootstrap;
using OpenCommonwealth.UI.Bridge;

namespace OpenCommonwealth;

public partial class PapyrusRuntime : Node
{
    private const string RuntimeKey = "papyrus_runtime";
    private readonly Dictionary<Node, string> _cells = new();
    private Task<GameSession>? _loading;
    private GameSession? _loaded;
    private ActorDeathEvents? _deaths;
    private bool _exited;
    private OpenCommonwealth.Services.Dialogue.IQuestState? _boundQuests;

    public PapyrusRuntime() { }
    public PapyrusRuntime(GameSession session)
    {
        Session = session;
        session.FragmentFailed = failure => GD.PushWarning($"[Papyrus] {failure}");
    }

    public GameSession? Session { get; private set; }
    public string? Refusal { get; private set; }
    public string? LoadFailure { get; private set; }
    public bool QuestCleanupFailed { get; private set; }
    public bool Loading => Session is null && LoadFailure is null;
    public int QuestRevision => Session is null ? 0 : 1 + Session.QuestStages.StageChanges;
    public int RefusedActivations { get; private set; }
    public int RefusedMetadata { get; private set; }
    public AdapterReport? CombatNatives { get; private set; }
    public AdapterReport? ActivationNatives { get; private set; }
    public AdapterReport? EnableNatives { get; private set; }
    public AdapterReport? TeammateNatives { get; private set; }

    public AdapterReport? XpNatives { get; private set; }
    public ActivationBlocks Blocks { get; } = new();
    public PlayerTeammates Teammates { get; } = new();
    public int UnresolvedKillers { get; private set; }
    public int InstructionBudget { get; set; } = PapyrusHostDefaults.StepBudget;

    public static PapyrusRuntime? Find(Node context)
    {
        if (!context.IsInsideTree()) return null;
        var root = context.GetTree().Root;
        return root.HasMeta(RuntimeKey) && root.GetMeta(RuntimeKey).AsGodotObject() is PapyrusRuntime runtime
            && IsInstanceValid(runtime) ? runtime : null;
    }

    public static PapyrusRuntime Ensure(Node context)
    {
        if (Find(context) is { } found) return found;
        var runtime = new PapyrusRuntime { Name = "PapyrusRuntime" };
        var root = context.GetTree().Root;
        root.SetMeta(RuntimeKey, runtime);
        root.CallDeferred(Node.MethodName.AddChild, runtime);
        return runtime;
    }

    public override void _EnterTree()
    {
        _exited = false;
        GetTree().Root.SetMeta(RuntimeKey, this);
        ActorCombat.Killed += OnActorKilled;
    }

    public override void _Ready()
    {
        if (Session is not null)
        {
            EngineLiveState.Readiness.Ready(Subsystem.Vm);
            BindActorSystems();
            StartQuests();
            if (QuestCleanupFailed) return;
        }
        AttachExistingStreams(GetTree().Root);
        if (Session is not null) return;
        EngineLiveState.Readiness.Pending(Subsystem.Vm);
        _loading = Task.Run(() => GameSession.Load(ScriptCacheLayout.DefaultRoot,
            GameLoader.DataRoots.Count > 0 ? GameLoader.DataRoots : null,
            ScriptCacheLoadOrder.Current(),
            ProjectSettings.GlobalizePath(AssetCachePaths.Root), QuestStartupCacheReader.Read, EngineLiveState.Store));
    }

    public override void _ExitTree()
    {
        _exited = true;
        EngineLiveState.Store.ClearQuestsIf(_boundQuests);
        ActorCombat.Killed -= OnActorKilled;
        var root = GetTree().Root;
        if (root.HasMeta(RuntimeKey) && root.GetMeta(RuntimeKey).AsGodotObject() == this) root.RemoveMeta(RuntimeKey);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        _loaded?.Dispose();
        _loading?.ContinueWith(loaded => loaded.Result.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    public override void _Process(double delta)
    {
        if (QuestCleanupFailed) return;
        if (_loading?.IsCompleted == true)
        {
            try
            {
                Session = _loaded = _loading.GetAwaiter().GetResult();
                EngineLiveState.Readiness.Ready(Subsystem.Vm);
                Session.FragmentFailed = failure => GD.PushWarning($"[Papyrus] {failure}");
                GD.Print($"[Papyrus] session loaded: scripts={Session.PexScripts.Count} questStartup={Session.QuestStartup.Count} cachedCells={_cells.Count} cache={CacheFingerprint()}");
                BindActorSystems();
                StartQuests();
                if (QuestCleanupFailed) { _loading = null; return; }
                foreach (var entry in _cells)
                {
                    if (!IsInstanceValid(entry.Key) || !entry.Key.IsInsideTree()) continue;
                    var attached = new List<(Node Node, PapyrusOwnerId Owner)>();
                    AttachReferences(entry.Value, entry.Key, null, attached);
                    Announce(attached);
                }
            }
            catch (Exception error)
            {
                if (EngineLiveState.Readiness.StatusOf(Subsystem.Vm).State is ReadyState.NotStarted or ReadyState.Pending) EngineLiveState.Readiness.Fail(Subsystem.Vm, error);
                LoadFailure = Refusal = error.Message;
                GD.PrintErr($"[Papyrus] {Refusal}");
            }
            _loading = null;
        }
        RunReferenceEvents(delta);
        TickAttachRetries(delta);
        TickScenes(delta);
        PumpHeadlessUi();
        Session?.RunQueuedFragments();
        Session?.Host.RunStep(InstructionBudget);
        if (Session?.Host is RadicleScriptHost radicle && radicle.LastNativeFailure is { } failure && failure != _reportedNativeFailure)
        {
            _reportedNativeFailure = failure;
            GD.Print($"[Papyrus] native failure: {failure}");
        }
    }

    private string? _reportedNativeFailure;

    private void BindActorSystems()
    {
        try { BindActorSystemsOnce(); }
        catch (Exception error)
        {
            EngineLiveState.Readiness.Fail(Subsystem.Vm, error);
            throw;
        }
    }

    private void BindActorSystemsOnce()
    {
        if (Session is null || _deaths is not null) return;
        CombatNatives = new CombatNativeAdapters(Session.Host, Session.Gate, new ActorLifeSource()).Bind();
        foreach (var outcome in CombatNatives.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        ActivationNatives = new ActivationNativeAdapters(Session.Host, Session.Gate, Blocks.Set, Blocks.IsBlocked).Bind();
        foreach (var outcome in ActivationNatives.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        EnableNatives = new EnableNativeAdapters(Session.Host, Session.Gate, (refKey, enabled) => SpawnService.SetEnabled(refKey, enabled), refKey => SpawnService.SetEnabled(refKey, false)).Bind();
        foreach (var outcome in EnableNatives.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        TeammateNatives = new TeammateNativeAdapters(Session.Host, Session.Gate, Teammates.Set, key => ContainerLedger.Current.Companions.IsTeammate(key, Teammates.IsTeammate(key)), Teammates.SetGivesXp).Bind();
        foreach (var outcome in TeammateNatives.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        XpNatives = new XpNativeAdapters(Session.Host, Session.Gate, PlayerXp.Reward, PlayerXp.TotalFor).Bind();
        foreach (var outcome in XpNatives.Natives)
            if (outcome.State != AdapterState.Bound) GD.Print($"[Papyrus] {outcome.Family.Native} is not bound: {outcome.Reason}");
        BindActorStateNatives();
        BindPerkNatives();
        BindFactionNatives();
        BindCrimeNatives();
        BindReferenceNatives();
        BindSceneNatives();
        BindThirdRailNatives();
        _deaths = new ActorDeathEvents(new ActorEventFamilies(Session.Events, new ScriptRegistrationRegistry(), Session.Gate));
    }

    private void OnActorKilled(ActorCombat combat, string? killer)
    {
        if (killer is not null && Teammates.GivesXp(killer)) PlayerXp.Kill(combat);
        if (Session is null || _deaths is null || combat.Identity.Owner is not { } dead || !Session.Events.IsRegistered(dead)) return;
        PapyrusOwnerId? killerOwner = killer is null ? null : ActorRegistry.Find(killer)?.Owner;
        if (killer is not null && killerOwner is null) UnresolvedKillers++;
        _deaths.Died(dead, killerOwner);
    }

    private UiServices? _headlessUi;
    private HeadlessUiService? _headlessUiService;

    public int PumpHeadlessUi() => _headlessUiService?.Pump() ?? 0;

    public UiReport? HeadlessUiReport { get; private set; }

    private void AttachHeadlessUi()
    {
        _headlessUi?.Dispose();
        _headlessUiService = new HeadlessUiService();
        _headlessUi = new UiServices(Session!.Host, _headlessUiService, Session.Gate, messageText: RadicleFormLookup.MessageText);
        HeadlessUiReport = _headlessUi.Attach();
        GD.Print($"[Papyrus] headless UI service attached: bound={HeadlessUiReport.Bound} blocked={HeadlessUiReport.Blocked}");
    }

    private void StartQuests()
    {
        if (UiBootstrap.Instance is { } uiBootstrap) uiBootstrap.AttachPapyrus(Session!);
        else AttachHeadlessUi();
        ActorEnableState.ClearAll();
        Teammates.Clear();
        ClearReferenceEvents();
        EngineLiveState.Store.Quests = _boundQuests = OpenCommonwealth.Services.Dialogue.SessionQuestState.From(Session!);
        EngineLiveState.Readiness.Pending(Subsystem.Quests);
        OpenCommonwealth.Services.Papyrus.Quest.QuestStartupResult result;
        try
        {
            Session!.BindQuestNatives(QuestOwners(Session));
            result = Session.StartNewGame();
        }
        catch (QuestCleanupException error)
        {
            EngineLiveState.Readiness.Fail(Subsystem.Quests, error);
            EngineLiveState.Readiness.Fail(Subsystem.Vm, error);
            EngineLiveState.Store.ClearQuestsIf(_boundQuests);
            _boundQuests = null;
            QuestCleanupFailed = true;
            LoadFailure = Refusal = error.Message;
            if (Session is { } failedSession) UiBootstrap.Instance?.DetachPapyrus(failedSession);
            _headlessUi?.Dispose();
            _headlessUi = null;
            _headlessUiService = null;
            Session = null;
            GD.PrintErr($"[Papyrus] {Refusal}");
            return;
        }
        catch (Exception error)
        {
            EngineLiveState.Readiness.Fail(Subsystem.Quests, error);
            throw;
        }
        if (result.Reasons.Count == 0)
        {
            EngineLiveState.Readiness.Ready(Subsystem.Quests);
            return;
        }
        Refusal = string.Join("; ", result.Reasons);
        EngineLiveState.Readiness.Fail(Subsystem.Quests, new InvalidOperationException(Refusal));
        GD.PrintErr($"[Papyrus] quest startup blocked {result.Blocked.Count} quest(s), {result.Started} started: {Refusal}");
    }

    private static IEnumerable<string> QuestOwners(GameSession session)
    {
        var startup = session.QuestStartup.Select(input => input.Quest);
        if (GameLoader.ActiveRecords is not { } records) return startup;
        try
        {
            return LoadSession.TryRead(records.Environment, () => QuestOwnerCache.Resolve(records,
                ProjectSettings.GlobalizePath(AssetCachePaths.Root)), out var owners) ? owners : startup;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Papyrus] quest owner cache unavailable, using startup quests: {ex.Message}");
            return startup;
        }
    }

    public void AttachCell(string cell, Node root)
    {
        if (_cells.ContainsKey(root)) return;
        _cells.Add(root, cell);
        if (Session is null)
        {
            GD.Print($"[Papyrus] cell {cell} waits for the script session ({(LoadFailure is null ? "still loading" : "load failed: " + LoadFailure)}); no owner is attached yet.");
            return;
        }
        ResumeParkedScenes(cell);
        var admitted = new HashSet<PapyrusOwnerId>();
        var attached = new List<(Node Node, PapyrusOwnerId Owner)>();
        AttachReferences(cell, root, admitted, attached);
        Session.ReattachCell(cell, admitted);
        Announce(attached);
    }

    public void DetachCell(Node root)
    {
        if (!_cells.Remove(root, out var cell) || _cells.ContainsValue(cell)) return;
        SuspendScenesIn(root, cell);
        if (Session is not null) Unannounce(root);
        Session?.DetachCell(cell);
    }

    public EventBatch? Activate(Node3D reference, Node3D activator)
    {
        if (Session is null || !TryOwner(reference, out var owner) || !TryOwner(activator, out var source))
        {
            RefusedActivations++;
            Refusal = "activation requires a ready Papyrus session and explicit reference/activator identities";
            return null;
        }
        return Session.Activate(owner, source, ActorTypeOf(source));
    }

    private void AttachReferences(string cell, Node node, HashSet<PapyrusOwnerId>? admitted = null,
        List<(Node Node, PapyrusOwnerId Owner)>? attached = null, bool children = true)
    {
        if (node.HasMeta(ScriptAttachments.MetadataKey))
        {
            if (node.HasMeta("fo4_script_source_unsupported")
                && node.GetMeta("fo4_script_source_unsupported").VariantType != Variant.Type.Bool)
            {
                RefusedMetadata++;
                Refusal = "script source status must be boolean metadata";
            }
            else if (TryOwner(node, out var owner))
            {
                var metadata = node.GetMeta(ScriptAttachments.MetadataKey).AsString();
                var result = Session!.AttachReference(cell, owner, metadata,
                    node.HasMeta("fo4_script_source_unsupported") && node.GetMeta("fo4_script_source_unsupported").AsBool());
                TraceAttach(node, owner, metadata, result);
                if (result.Refusal is not null) Refusal = result.Refusal;
                else foreach (var initialization in result.Initialization)
                    if (!initialization.Completed && initialization.Reason != VmStopReason.Waiting) Refusal = initialization.Message ?? initialization.Reason.ToString();
                if (result.Ready)
                {
                    admitted?.Add(owner);
                    attached?.Add((node, owner));
                }
            }
            else
            {
                RefusedMetadata++;
                Refusal = "script metadata has no explicit placed-reference and base-form identities";
                var waiting = node;
                Session!.AttachRetries.Enqueue(waiting.GetInstanceId().ToString(), () => RetryAttach(cell, waiting));
            }
        }
        if (children) foreach (var child in node.GetChildren()) AttachReferences(cell, child, admitted, attached);
    }

    private void TickAttachRetries(double delta)
    {
        if (Session is null) return;
        var abandoned = Session.AttachRetries.Abandoned;
        Session.AttachRetries.Tick(delta);
        if (Session.AttachRetries.Abandoned != abandoned)
            GD.PrintErr($"[Papyrus] attach retry abandoned {Session.AttachRetries.Abandoned - abandoned} reference(s), last: {Session.AttachRetries.LastAbandonReason}");
    }

    private string? RetryAttach(string cell, Node node)
    {
        if (!IsInstanceValid(node) || !node.IsInsideTree()) return null;
        if (!TryOwner(node, out _)) return "no explicit placed-reference and base-form identities yet";
        var attached = new List<(Node Node, PapyrusOwnerId Owner)>();
        AttachReferences(cell, node, new HashSet<PapyrusOwnerId>(), attached, false);
        Announce(attached);
        return null;
    }

    private static string CacheFingerprint()
    {
        try
        {
            var manifest = ScriptCacheManifest.FromJson(System.IO.File.ReadAllText(ScriptCacheLayout.ManifestPath(ScriptCacheLayout.DefaultRoot)));
            return $"format={manifest.FormatVersion} pin={manifest.RadicleCommit} sourceSet={manifest.SourceSetSha256} startup={manifest.QuestStartupSha256}";
        }
        catch (Exception error) { return $"unreadable({error.Message})"; }
    }

    private static void TraceAttach(Node node, PapyrusOwnerId owner, string metadata, ReferenceAttachResult result)
    {
        string scripts;
        try { scripts = string.Join(",", ScriptAttachments.Parse(metadata).Select(attachment => attachment.Name)); }
        catch (Exception error) { scripts = $"unparsed({error.Message})"; }
        string outcome = result.Refusal is not null ? $"refused reason=\"{result.Refusal}\" {result.Detail}"
            : !result.Ready
                ? "init-failed " + string.Join("; ", result.Initialization.Select((init, i) => $"{result.Instances[i].ScriptName}:{init.Reason}{(init.Message is null ? "" : " " + init.Message)} failure=\"{result.Instances[i].Failure ?? "none"}\" key={init.Key ?? "none"}").Where((_, i) => !result.Initialization[i].Completed && result.Initialization[i].Reason != VmStopReason.Waiting))
                : $"attached instances={result.Instances.Count}";
        var issues = result.Issues.Count == 0 ? "" : " issues=[" + string.Join("; ", result.Issues) + "]";
        GD.Print($"[Papyrus] attach owner={owner.Key} node={node.Name} scripts=[{scripts}] {outcome}{issues}");
    }

    private void Announce(List<(Node Node, PapyrusOwnerId Owner)> attached)
    {
        foreach (var (node, owner) in attached) Announce(node, owner);
    }

    private void Announce(Node node, PapyrusOwnerId owner)
    {
        foreach (var transition in new[] { LifecycleTransition.CellAttached, LifecycleTransition.Loaded })
        {
            var outcome = Session!.Lifecycle.Transition(owner, transition);
            GD.Print($"[Papyrus] {outcome.EventName} {owner.Key} delivered={outcome.Delivered} {outcome.Reason}");
        }
        if (node is not Area3D area || !area.IsInGroup("trigger_volumes") || area.HasMeta("papyrus_trigger_wired")) return;
        area.SetMeta("papyrus_trigger_wired", true);
        area.BodyEntered += body => Trigger(area, owner, body, true);
        area.BodyExited += body => Trigger(area, owner, body, false);
    }

    private void Trigger(Area3D volume, PapyrusOwnerId area, Node body, bool entering)
    {
        if (!ActorEnableState.IsEnabled(volume)) return;
        Node? holder = body;
        PapyrusOwnerId actor = default;
        while (holder is not null && !TryOwner(holder, out actor)) holder = holder.GetParent();
        var name = entering ? "OnTriggerEnter" : "OnTriggerLeave";
        if (_exited || !IsInstanceValid(this) || Session is null || holder is null)
        {
            GD.Print($"[Papyrus] {name} {area.Key} skipped: {body.Name} carries no reference identity");
            return;
        }
        var batch = Session.Trigger(area, actor, entering, ActorTypeOf(actor));
        GD.Print($"[Papyrus] {name} {area.Key} by {actor.Key}: delivered={batch.Delivered} refused={batch.Refused} no-handler={batch.NoHandler}");
        foreach (var outcome in batch.Outcomes.Where(outcome => outcome.Result != EventDelivery.Delivered))
            GD.Print($"[Papyrus] {name} {outcome.Owner.Key} {outcome.Result}: {outcome.Reason ?? "no reason given"}{(Session.Host is RadicleScriptHost radicle && outcome.Result == EventDelivery.Refused && radicle.LastNativeFailure is { } failure ? $"; last native failure: {failure}" : "")}");
    }

    private void Unannounce(Node node)
    {
        if (TryOwner(node, out var owner) && Session!.Events.IsRegistered(owner))
            foreach (var transition in new[] { LifecycleTransition.Unloaded, LifecycleTransition.CellDetached })
                Session.Lifecycle.Transition(owner, transition);
        foreach (var child in node.GetChildren()) Unannounce(child);
    }

    private static void AttachExistingStreams(Node node)
    {
        if (node is WorldspaceStreamer streamer) streamer.ReattachPapyrusCells();
        foreach (var child in node.GetChildren()) AttachExistingStreams(child);
    }

    public static bool TryOwner(Node node, out PapyrusOwnerId owner)
    {
        owner = default;
        if (!node.HasMeta("fo4_form_key") || !node.HasMeta("fo4_base_form_key")) return false;
        if (node.GetMeta("fo4_form_key").VariantType is not (Variant.Type.String or Variant.Type.StringName)
            || node.GetMeta("fo4_base_form_key").VariantType is not (Variant.Type.String or Variant.Type.StringName)) return false;
        var reference = node.GetMeta("fo4_form_key").AsString();
        var basis = node.GetMeta("fo4_base_form_key").AsString();
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(basis)) return false;
        owner = node.GetMeta("fo4_dynamic_ref", false).AsBool()
            ? PapyrusOwnerId.ForDynamicReference(basis, reference)
            : PapyrusOwnerId.ForPlacedReference(basis, reference);
        return true;
    }
}
