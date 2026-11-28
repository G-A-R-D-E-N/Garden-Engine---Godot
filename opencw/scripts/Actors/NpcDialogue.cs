using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Audio;
using OpenCommonwealth.Services.Dialogue;
using OpenCommonwealth.Services.Interaction;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Trade;
using OpenCommonwealth.UI.Bootstrap;
using OpenCommonwealth.Lighting;
using Radicle.Interop;

namespace OpenCommonwealth;

public static class NpcDialogue
{
    private static readonly LoadSession.EnvStamp EnvStamp = new();
    private static readonly IndexLoader<RadicleDialogueRecords> Index = new();
    private static GameSession? _questSession;
    private static int _ticket;

    private sealed class BinderAliases(GameSession? session) : IAliasState
    {
        public void Prime() => OpenCommonwealth.UI.Bootstrap.AliasBindings.Prime();

        public bool? IsAliasRef(FormKey quest, int alias, FormKey reference, FormKey? baseForm) =>
            alias < 0 ? null : OpenCommonwealth.UI.Bootstrap.AliasBindings.IsAliasRef(Services.Ui.Records.RecordKey.Format(quest), (uint)alias,
                Services.Ui.Records.RecordKey.Format(reference), baseForm is { } b ? Services.Ui.Records.RecordKey.Format(b) : null,
                quest => session?.QuestStages.StateOf(quest) == OpenCommonwealth.Services.Papyrus.Quest.QuestState.Running);
    }

    public static DialogueSession? Active { get; private set; }

    public static string? CapsItemId { get; set; } = BarterPricing.CapsItemId;

    public static IVendorChests VendorChests { get; set; } = new NoVendorChests();

    public static bool IsNpc(Node3D reference) =>
        reference.HasMeta(NpcPuppetSpawner.RefKeyMeta) && reference.HasMeta(NpcPuppetSpawner.BaseKeyMeta) &&
        CanEngage(reference);

    public static bool CanEngage(Node3D reference) => NpcInteraction.CanEngage(NpcSnapshots.Read(reference));

    public static bool CanLoot(bool alive, bool enabled) => !alive && enabled;

    public static bool CanLoot(Node3D reference) =>
        reference.IsInGroup("actors") && reference.HasMeta(NpcPuppetSpawner.RefKeyMeta) && reference.HasMeta(NpcPuppetSpawner.BaseKeyMeta) &&
        CanLoot(NpcPuppetSpawner.IsAlive(reference), NpcPuppetSpawner.IsEnabled(reference));

    // Requires: GameLoader.EnvLifetime read lock for the lookup; taken here unless the caller already holds it. A reload or failed read keeps the fixed caps id.
    private static string ResolveCaps(RecordSource sources)
    {
        bool lockHere = !GameLoader.EnvLifetime.IsReadLockHeld;
        if (lockHere) GameLoader.EnvLifetime.EnterReadLock();
        try
        {
            return BarterPricing.ResolveCapsItemId((editorId, signature) =>
                sources.FindEditorId(editorId, signature) is { } record ? RecordKey.Format(sources.Info(record).FormKey) : null);
        }
        catch (RadicleException) { return BarterPricing.CapsItemId; }
        finally { if (lockHere) GameLoader.EnvLifetime.ExitReadLock(); }
    }

    public static void Warm(RecordSource sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var env = sources.Environment;
        VendorChests = new VendorChests(new RadicleVendorLookup(sources));
        CapsItemId = ResolveCaps(sources);
        if (EnvStamp.Changed(env))
        {
            Index.Reset();
            _questSession = null;
        }
        if (Index.Current is null)
            GD.Print("[Dialogue] indexing topics.");
        Index.Start(() =>
        {
            GameLoader.EnvLifetime.EnterReadLock();
            try
            {
                var records = new RadicleDialogueRecords(sources, () => EnvironmentController.Active?.Clock.HourOfDay ?? 12.0);
                records.BuildIndex();
                return records;
            }
            finally { GameLoader.EnvLifetime.ExitReadLock(); }
        }, (failure, attempt, retry) =>
            GD.PrintErr($"[Dialogue] topic index failed ({failure}); attempt {attempt} of {IndexLoader<RadicleDialogueRecords>.MaxAttempts}, {(retry ? "the next dialogue start retries it" : "giving up for this load")}."));
    }

    public static bool TryStart(Node3D reference, Action onEnded, Func<bool> stillActive)
    {
        var bootstrap = UiBootstrap.Instance;
        if (!IsNpc(reference) || !GameLoader.HasLoadOrder || bootstrap is null)
        {
            GD.Print($"[Dialogue] start refused for {reference.Name}: isNpc={IsNpc(reference)} hasLoadOrder={GameLoader.HasLoadOrder} uiBootstrap={(bootstrap is null ? "absent" : "present")}");
            return false;
        }
        SettlerCommands.End();
        RadicleEnvironment env;
        try { env = GameLoader.Records.Environment; }
        catch (InvalidOperationException e) when (e is not RadicleException)
        {
            GD.Print($"[Dialogue] start refused for {reference.Name}: environment unavailable ({e.Message})");
            return false;
        }
        string key = reference.GetMeta(NpcPuppetSpawner.ResolvedMeta, reference.GetMeta(NpcPuppetSpawner.BaseKeyMeta, "")).AsString();
        if (!GameLoader.TryParseKey(key, out var npc))
        {
            GD.Print($"[Dialogue] start refused for {reference.Name}: npc key '{key}' does not parse");
            return false;
        }
        int ticket = ++_ticket;
        Task? indexing;
        try
        {
            Warm(GameLoader.Records);
            indexing = Index.Current;
            if (indexing is null)
            {
                Warm(GameLoader.Records);
                indexing = Index.Current;
            }
        }
        catch (Exception error)
        {
            GD.PushWarning($"[Dialogue] record warm refused: {error.Message}");
            return false;
        }
        if (indexing is null)
        {
            GD.Print($"[Dialogue] start refused for {reference.Name}: the topic index is being retried ({Index.Failure}).");
            return false;
        }
        if (!indexing.IsCompleted)
        {
            indexing.ContinueWith(_ => Callable.From(() =>
            {
                if (ticket != _ticket || !stillActive() || !LoadSession.IsCurrent(env))
                    return;
                if (!GodotObject.IsInstanceValid(reference) || !Open(reference, npc, bootstrap, onEnded, env))
                    onEnded();
            }).CallDeferred());
            return true;
        }
        if (!LoadSession.IsCurrent(env) || !Open(reference, npc, bootstrap, onEnded, env))
            Callable.From(() => { if (ticket == _ticket && stillActive()) onEnded(); }).CallDeferred();
        return true;
    }

    public static DialogueConversation? BuildConversation(Node3D reference, FormKey npc, RadicleEnvironment env)
    {
        if (Index.Value is not { } records)
        {
            GD.Print($"[Dialogue] {npc}: no applicable dialogue (the topic index is unavailable{(Index.Failure is null ? ", still building" : ", it failed: " + Index.Failure)}).");
            return null;
        }
        var session = PapyrusRuntime.Find(reference)?.Session;
        if (!ReferenceEquals(session, _questSession))
        {
            if (session is not null) records.Quests = SessionQuestState.From(session);
            else if (_questSession is { } previous) records.Store.ClearQuestsIf(SessionQuestState.From(previous));
            records.Aliases = new BinderAliases(session);
            _questSession = session;
        }
        var refKey = reference.GetMeta(NpcPuppetSpawner.RefKeyMeta, "").AsString();
        var baseKey = NpcSnapshots.BaseKeyOf(reference);
        GD.Print($"[Dialogue] open ref={refKey} base={baseKey} papyrusSession={(session is null ? "unavailable" : "ready")} indexUnevaluableSoFar={Skipped(records)}");
        var offersCommand = SettlerCommands.CanCommand(reference);
        if (!LoadSession.TryRead(env, () => new DialogueConversation(npc, records, () => VendorChests.ChestKey(refKey, baseKey) != null, () => offersCommand,
            session is null ? null : QuestStageEffects.For(session, refKey), SceneStarter ?? StarterFor(reference), GameLoader.TryParseKey(refKey, out var reference0) ? reference0 : null), out var conversation))
        {
            GD.Print($"[Dialogue] {npc}: not opened, the game environment was replaced while the conversation was built.");
            return null;
        }
        if (!GodotObject.IsInstanceValid(reference) || (conversation.First.Line.Length == 0 && conversation.First.Choices.Count < 2))
        {
            GD.Print($"[Dialogue] {npc}: no applicable dialogue ({Skipped(records)}).");
            return null;
        }
        conversation.EffectReported += ReportEffect;
        return conversation;
    }

    private static void ReportEffect(FormKey info, EffectOutcome outcome)
    {
        if (outcome.Status != EffectStatus.Applied)
            GD.PushWarning($"[Dialogue] effect of {info} was {outcome.Status}: {outcome.Effect} ({outcome.Detail})");
        else if (DialogueConversation.Trace is not null)
            GD.Print($"[Dialogue] effect of {info} applied: {outcome.Effect} ({outcome.Detail})");
    }

    public static ISceneStarter? SceneStarter { get; set; }

    private static ISceneStarter? StarterFor(Node3D reference) =>
        PapyrusRuntime.Find(reference)?.Scenes is { } scenes ? new SceneRuntimeStarter(scenes) : null;

    public static bool IndexReady => Index.Value is not null;

    public static void WarmForProbe() => Warm(GameLoader.Records);

    private static bool Open(Node3D reference, FormKey npc, UiBootstrap bootstrap, Action onEnded, RadicleEnvironment env)
    {
        DialogueSession? dialogue = null;
        bool ended = false;
        try
        {
            if (BuildConversation(reference, npc, env) is not { } conversation)
                return false;
            var refKey = reference.GetMeta(NpcPuppetSpawner.RefKeyMeta, "").AsString();
            var baseKey = NpcSnapshots.BaseKeyOf(reference);
            var voice = NpcVoice.For(reference);
            voice.Reset();
            var first = voice.Prepare(conversation.First);
            dialogue = new DialogueSession(first, stillValid: () => GodotObject.IsInstanceValid(reference));
            dialogue.NodeStarted += voice.Play;
            dialogue.NodeStarted += node => conversation.NodeStarted(node.Id);
            dialogue.NodeFinished += node => conversation.NodeFinished(node.Id);
            dialogue.SkipRequested += voice.Stop;
            dialogue.Closing += voice.Stop;
            Godot.Vector3? faced = null;
            dialogue.Ended += _ =>
            {
                voice.Reset();
                conversation.Close();
                if (ReferenceEquals(Active, dialogue)) Active = null;
                try { NpcDialogueFacing.End(reference, faced); }
                catch (Exception error) when (error is not OutOfMemoryException) { GD.PushWarning($"[Dialogue] {npc}: restoring the NPC heading failed ({error.GetType().Name}: {error.Message})."); }
            };
            Active = dialogue;
            dialogue.ChoiceSelected += (id, slot) =>
            {
                GD.Print($"[Dialogue] choice node={id} slot={slot}");
                if (conversation.Next(id, slot) is { } next) dialogue.Advance(voice.Prepare(next));
                else if (dialogue.State == DialogueState.Waiting) dialogue.Finish();
            };
            if (bootstrap.Ui.OpenDialogue(dialogue, outcome => { try { RouteTrade(outcome, refKey, baseKey); RouteCommand(outcome, reference); } finally { ended = true; onEnded(); } }) is null)
            {
                Active = null;
                return false;
            }
            faced = NpcDialogueFacing.Begin(reference);
            voice.Play(first);
            conversation.NodeStarted(first.Id);
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Dialogue] {npc}: opening failed ({ex.GetType().Name}: {ex.Message}).");
            if (dialogue is not null)
            {
                try { dialogue.Abort(); }
                catch (Exception abort) { GD.PrintErr($"[Dialogue] {npc}: aborting the failed dialogue threw ({abort.GetType().Name}: {abort.Message})."); }
                if (ReferenceEquals(Active, dialogue))
                    Active = null;
            }
            return ended;
        }
    }

    private static void RouteTrade(DialogueOutcome outcome, string refKey, string baseKey)
    {
        var decision = NpcTradeRoute.Decide(outcome, VendorChests, refKey, baseKey);
        if (decision.Route == TradeRoute.NoVendorChest)
            GD.Print($"[Dialogue] {refKey}: trade requested but no vendor chest is known; barter not opened.");
        else if (decision.Route == TradeRoute.Barter)
        {
            if (!BarterLauncher.Open(refKey, baseKey, decision.ChestKey!, CapsItemId))
                GD.PushWarning($"[Dialogue] {refKey}: vendor chest {decision.ChestKey} found but the barter menu could not open (caps item id unset or UI unavailable).");
        }
    }

    private static void RouteCommand(DialogueOutcome outcome, Node3D reference)
    {
        if (outcome == DialogueOutcome.Command && GodotObject.IsInstanceValid(reference)) SettlerCommands.Start(reference);
    }

    private static string Skipped(RadicleDialogueRecords records) =>
        records.Unevaluable.Count == 0 ? "no skipped conditions" : "skipped condition functions " + string.Join(", ", records.Unevaluable.Select(p => $"{p.Key}x{p.Value}"));
}
