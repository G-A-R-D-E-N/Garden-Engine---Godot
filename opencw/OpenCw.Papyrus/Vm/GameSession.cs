using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.LiveState;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vmad;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record AttachIssue(string Owner, string Script, string? Property, string Reason)
{
    public override string ToString() =>
        $"owner {Owner}{(Script.Length == 0 ? "" : " script " + Script)}{(Property is null ? "" : " property " + Property)}: {Reason}";
}

public sealed record ReferenceAttachResult(IReadOnlyList<ScriptInstance> Instances,
    IReadOnlyList<VmResult> Initialization, string? Refusal, string? Detail = null)
{
    public IReadOnlyList<AttachIssue> Issues { get; init; } = Array.Empty<AttachIssue>();

    public bool Partial => Refusal is null && Issues.Count > 0;

    public bool Ready => Refusal is null && Initialization.All(init => init.Completed || init.Reason == VmStopReason.Waiting);
}

public sealed record FragmentFailure(string Quest, int Stage, string Script, string Function, string Reason)
{
    public override string ToString() => $"quest {Quest} stage {Stage} fragment {Script}.{Function} refused: {Reason}";
}

public sealed record RanFragment(string Quest, int Stage, string Script, string Function)
{
    public override string ToString() => $"quest {Quest} stage {Stage} fragment {Script}.{Function} ran";
}

public sealed class GameSession : IDisposable
{
    private const int MaxFragmentFailures = 64;
    private const int MaxFragmentRuns = 512;
    private const int MaxAttachIssues = 64;
    private readonly List<AttachIssue> _attachIssues = new();
    private readonly List<FragmentFailure> _fragmentFailures = new();
    private readonly List<RanFragment> _fragmentRuns = new();
    private readonly Dictionary<PapyrusOwnerId, (string Cell, string Metadata, ReferenceAttachResult Result)> _references = new();
    private readonly Dictionary<(string Quest, string Script), ScriptInstance> _fragmentScripts = new();
    private readonly EvidenceGate _gate;

    public GameSession(IScriptHost host, EvidenceGate gate, IReadOnlyList<QuestStartupInput>? questStartup = null, LiveStateStore? state = null)
    {
        State = state ?? new LiveStateStore();
        QuestStartup = questStartup?.ToArray() ?? Array.Empty<QuestStartupInput>();
        new QuestStartupDatabase(QuestStartupDatabase.CurrentFormatVersion, QuestStartup).Validate();
        _gate = gate;
        NativeSinks = new NativeSet(gate);
        Host = host;
        Cells = new CellScriptSession(Host);
        Events = new PapyrusEventBridge(Host, gate);
        Quests = new QuestRuntime(gate);
        QuestStages = new QuestStateMachine(gate);
        foreach (var input in QuestStartup)
            if (input.Metadata is { Fragments.Count: > 0 }) QuestStages.BindFragments(input.Quest, input.Metadata.Fragments);
        Lifecycle = new ReferenceLifecycle(Events, gate);
    }

    public NativeReport BindQuestNatives(IEnumerable<string> quests) => QuestStages.Bind(Host, quests);

    public IScriptHost Host { get; }

    // Invariant: the one store condition evaluation reads; the caller may pass the process-wide store so it outlives the session.
    public LiveStateStore State { get; }
    public EvidenceGate Gate => _gate;
    public NativeSet NativeSinks { get; }
    public CellScriptSession Cells { get; }

    public AttachRetryQueue AttachRetries { get; } = new();
    public PapyrusEventBridge Events { get; }
    public QuestRuntime Quests { get; }
    public QuestStateMachine QuestStages { get; }
    public ReferenceLifecycle Lifecycle { get; }
    public IReadOnlyList<QuestStartupInput> QuestStartup { get; }
    public IReadOnlyDictionary<string, byte[]> PexScripts { get; init; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    public QuestStartupResult? StartupResult { get; private set; }
    public StartupStageIdentification? StartupStages { get; private set; }
    public int SeededStages { get; private set; }
    public int FragmentsRun { get; private set; }
    public int FragmentsRefused { get; private set; }
    public int RefusedReferences { get; private set; }
    public int PartialReferences { get; private set; }
    public int RefusedScripts { get; private set; }
    public int UnboundProperties { get; private set; }
    public int FailedInitializations { get; private set; }

    public IReadOnlyList<AttachIssue> AttachIssues => _attachIssues;

    public delegate byte[] QuestStartupReader(string cacheRoot, ScriptCacheManifest manifest, string? assetCacheRoot);

    public static GameSession Load(string cacheRoot, IReadOnlyList<string>? expectedLoadOrderRoots = null,
        IReadOnlyList<string>? expectedPlugins = null, string? assetCacheRoot = null,
        QuestStartupReader? readQuestStartup = null, LiveStateStore? state = null)
    {
        if (File.Exists(ScriptCacheLayout.MarkerPath(cacheRoot)))
            throw new InvalidOperationException("the script cache swap is incomplete; rebuild or recover it before play");
        var manifestJson = File.ReadAllText(ScriptCacheLayout.ManifestPath(cacheRoot));
        var manifest = ScriptCacheManifest.FromJson(manifestJson);
        if (manifest.DescribeMismatch() is { } mismatch)
            throw new InvalidOperationException($"the script cache format, count or Radicle pin does not match: {mismatch}; rebuild it with the asset converter (--scripts-only)");
        var expectedRoots = ScriptCacheBuilder.NormalizeRoots(expectedLoadOrderRoots);
        if (expectedRoots.Count != 0 && !manifest.MatchesLoadOrder(expectedRoots))
            throw new InvalidOperationException(
                $"the script cache was built for {Describe(manifest.LoadOrderRoots)}; this load order is {Describe(expectedRoots)}; rebuild the cache for this profile");
        var plugins = ScriptCacheBuilder.NormalizePlugins(expectedPlugins);
        if (plugins.Count != 0 && !manifest.MatchesPluginOrder(plugins))
            throw new InvalidOperationException(
                $"the script cache was built for plugins {Describe(manifest.LoadOrderPlugins)}; this plugin order is {Describe(plugins)}; rebuild the cache for this profile");
        if (plugins.Count != 0 && !manifest.MatchesPluginStats(expectedPlugins!))
            throw new InvalidOperationException(
                "the script cache was built against different plugin files (size or modified time changed); rebuild the cache for this profile");
        var readStartup = readQuestStartup ?? ((root, man, _) => ScriptCacheBuilder.ReadQuestStartupFromScriptCache(root, man));
        var startupBytes = readStartup(cacheRoot, manifest, assetCacheRoot);
        var startup = QuestStartupDatabase.FromJson(System.Text.Encoding.UTF8.GetString(startupBytes));
        if (startup.Inputs.Count != manifest.QuestStartupCount)
            throw new InvalidOperationException("the converted quest startup payload does not match the script cache manifest");
        var pex = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            var pexBytes = File.ReadAllBytes(ScriptCacheLayout.PexPath(cacheRoot, entry.Name));
            if (entry.PexFile != ScriptCacheLayout.PexRelativePath(entry.Name)
                || ScriptCacheBuilder.Fingerprint(pexBytes) != entry.PexSha256)
                throw new InvalidOperationException($"the cached PEX for {entry.Name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
            pex[entry.Name] = pexBytes;
        }
        if (File.Exists(ScriptCacheLayout.MarkerPath(cacheRoot))
            || File.ReadAllText(ScriptCacheLayout.ManifestPath(cacheRoot)) != manifestJson
            || !readStartup(cacheRoot, manifest, assetCacheRoot).SequenceEqual(startupBytes))
            throw new InvalidOperationException("the script cache changed while loading; restart with a stable cache");
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")
            ?? throw new InvalidOperationException("the canonical Papyrus evidence registry is missing");
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
        var host = new RadicleScriptHost(gate, pex.Values);
        try
        {
            var session = new GameSession(host, gate, startup.Inputs, state) { PexScripts = pex };
            session.NativeSinks.BindBaseSet();
            session.NativeSinks.Install(session.Host);
            return session;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static string Describe(IReadOnlyList<string>? roots) =>
        roots is { Count: > 0 } ? string.Join("; ", roots) : "(no load order recorded)";

    public void Dispose() => (Host as IDisposable)?.Dispose();

    public QuestStartupResult StartNewGame()
    {
        if (Quests.CleanupFailure is { } failure) throw new QuestCleanupException(failure);
        if (StartupResult is { Applied: true }) return StartupResult;
        var result = Quests.StartNewGame(QuestStartup, Host);
        if (result.Applied)
        {
            foreach (var input in QuestStartup)
                foreach (var instance in Quests.InstancesFor(input.Quest))
                    Events.Register(PapyrusOwnerId.ForForm(input.Quest), instance);
            StartupResult = result;
            SeedStartupStages();
        }
        StartGameQuests();
        return result;
    }

    public void StartGameQuests(IReadOnlySet<string>? recorded = null)
    {
        foreach (var input in QuestStartup)
            if (input.StartGameEnabled && recorded?.Contains(input.Quest) != true
                && QuestStages.StateOf(input.Quest) == QuestState.NotStarted)
                QuestStages.Apply(input.Quest, QuestNative.Start);
    }

    public IReadOnlyList<FragmentFailure> FragmentFailures => _fragmentFailures;

    public IReadOnlyList<RanFragment> FragmentRuns => _fragmentRuns;

    public Action<FragmentFailure>? FragmentFailed { get; set; }

    public string? FragmentCallbackError { get; private set; }

    // Requires: called outside a native callback, because the host refuses to attach or run a script inside one.
    public int RunQueuedFragments()
    {
        var ran = 0;
        foreach (var fragment in QuestStages.TakeQueuedFragments())
        {
            string? failure;
            try { failure = RunFragment(fragment); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failure = $"{ex.GetType().Name}: {ex.Message}"; } // Invariant: one failing fragment must not drop the rest of the drain.
            if (failure is null)
            {
                ran++;
                if (_fragmentRuns.Count == MaxFragmentRuns) _fragmentRuns.RemoveAt(0);
                _fragmentRuns.Add(new RanFragment(fragment.Quest, fragment.Stage, fragment.Script, fragment.Function));
            }
            else
            {
                FragmentsRefused++;
                var entry = new FragmentFailure(fragment.Quest, fragment.Stage, fragment.Script, fragment.Function, failure);
                if (_fragmentFailures.Count == MaxFragmentFailures) _fragmentFailures.RemoveAt(0);
                _fragmentFailures.Add(entry);
                try { FragmentFailed?.Invoke(entry); }
                catch (Exception error) when (error is not OutOfMemoryException) { FragmentCallbackError = $"{error.GetType().Name}: {error.Message}"; }
            }
        }
        FragmentsRun += ran;
        return ran;
    }

    private static string Describe(VmResult result) =>
        $"{result.Reason} {result.Key} {result.Message}".TrimEnd();

    private string? RunFragment(QueuedFragment fragment)
    {
        if (!Quests.Started.Contains(fragment.Quest)) return "the quest never started";
        if (QuestStages.StateOf(fragment.Quest) == QuestState.Stopped) return "the quest is stopped";
        var key = (fragment.Quest, fragment.Script.ToLowerInvariant());
        if (_fragmentScripts.TryGetValue(key, out var instance) && instance.Detached)
        {
            _fragmentScripts.Remove(key);
            instance = null!;
        }
        bool attached = false;
        if (instance is null)
        {
            instance = Quests.InstancesFor(fragment.Quest).FirstOrDefault(entry =>
                string.Equals(entry.ScriptName, fragment.Script, StringComparison.OrdinalIgnoreCase));
            if (instance is null)
            {
                var input = QuestStartup.First(entry => entry.Quest == fragment.Quest);
                var values = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
                if (!Host.HasScript(fragment.Script)) return "the fragment script is not available to the Papyrus host";
                if (Host.HasInvalidParentChain(fragment.Script)) return "the fragment script has an invalid parent chain";
                if (string.Equals(fragment.Script, input.Metadata?.FragmentScript, StringComparison.OrdinalIgnoreCase)
                    && QuestStartupBinding.PrepareFragmentScript(input, out values) is { } unbound)
                    return "the fragment script properties cannot be bound: " + unbound;
                try
                {
                    instance = Host.Attach(fragment.Script, values, PapyrusOwnerId.ForForm(fragment.Quest));
                    attached = true;
                    var init = Host.Initialize(instance);
                    if (init is not { Completed: true } and not { Reason: VmStopReason.Waiting })
                    {
                        Host.Discard(instance);
                        return "OnInit did not finish: " + Describe(init);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (attached) DiscardQuietly(instance);
                    return $"attach or init threw {ex.GetType().Name}: {ex.Message}";
                }
            }
            _fragmentScripts[key] = instance;
        }
        try
        {
            var result = Host.RunEvent(instance, fragment.Function);
            return result is { Completed: true } or { Reason: VmStopReason.Waiting } ? null : "the call did not complete: " + Describe(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _fragmentScripts.Remove(key);
            if (attached || !Quests.InstancesFor(fragment.Quest).Contains(instance)) DiscardQuietly(instance);
            return $"the call threw {ex.GetType().Name}: {ex.Message}";
        }
    }

    // Requires: called outside a native callback; the speaker argument shape (ObjectReference akSpeakerRef) is UNVERIFIED.
    public (bool Completed, string Detail) RunInfoFragment(string info, InfoVmadParse parse, InfoFragmentRow row, string? speaker)
    {
        var key = (info, row.Script.ToLowerInvariant());
        if (_fragmentScripts.TryGetValue(key, out var instance) && instance.Detached)
        {
            _fragmentScripts.Remove(key);
            instance = null!;
        }
        var attached = false;
        try
        {
            if (instance is null)
            {
                if (!Host.HasScript(row.Script)) return (false, $"the fragment script {row.Script} is not available to the Papyrus host");
                if (Host.HasInvalidParentChain(row.Script)) return (false, $"the fragment script {row.Script} has an invalid parent chain");
                var owner = PapyrusOwnerId.ForForm(info);
                var bound = VmadValueBinder.Bind(owner, string.Equals(parse.Script, row.Script, StringComparison.OrdinalIgnoreCase) ? parse.Properties : Array.Empty<VmadValueShape>());
                if (bound.Unsupported != 0) return (false, "fragment properties cannot be bound: " + string.Join("; ", bound.UnsupportedShapes));
                var values = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in bound.Properties)
                    if (string.IsNullOrWhiteSpace(property.Name) || !values.TryAdd(property.Name, property.Value))
                        return (false, "duplicate or empty fragment property names are unsupported");
                instance = Host.Attach(row.Script, values, owner);
                attached = true;
                if (!Host.Initialize(instance).Completed)
                {
                    DiscardQuietly(instance);
                    return (false, $"{row.Script} did not finish OnInit");
                }
                _fragmentScripts[key] = instance;
            }
            var arguments = speaker is null ? Array.Empty<PapyrusValue>() : new[] { PapyrusValue.FromObject(new EngineObjectReference(speaker)) };
            var result = Host.RunEvent(instance, row.Function, arguments);
            if (result.Completed) return (true, $"{row.Script}.{row.Function} completed");
            _fragmentScripts.Remove(key);
            if (attached) DiscardQuietly(instance);
            return (false, $"{row.Script}.{row.Function} did not complete: {result.Reason} {result.Key} {result.Message}".TrimEnd());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _fragmentScripts.Remove(key);
            if (attached && instance is not null) DiscardQuietly(instance);
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void DiscardQuietly(ScriptInstance instance)
    {
        try { Host.Discard(instance); }
        catch (Exception) { }
    }

    private void SeedStartupStages()
    {
        var started = QuestStartup.Where(input => Quests.Started.Contains(input.Quest)).ToArray();
        if (started.Any(input => input.Stages is not null))
        {
            StartupStages = RadicleStartupStages.Identify(started);
            foreach (var (quest, stage) in StartupStages.Stages)
            {
                if (QuestStages.StateOf(quest) != QuestState.NotStarted) continue;
                var fragments = started.First(input => input.Quest == quest).Stages!.First(entry => entry.Index == stage).Fragments;
                if (fragments > 0) _ = _gate.Gate(QuestRuntime.FragmentTimingKey);
                if (QuestStages.Apply(quest, QuestNative.Start) && QuestStages.Apply(quest, QuestNative.SetStage, stage))
                    SeededStages++;
            }
        }
    }

    public ReferenceAttachResult AttachReference(string cell, PapyrusOwnerId owner, string metadata,
        bool sourceUnsupported = false)
    {
        ReferenceAttachResult Refuse(string reason, string? detail = null, IReadOnlyList<AttachIssue>? issues = null)
        {
            RefusedReferences++;
            issues ??= new[] { new AttachIssue(owner.Key, "", null, reason) };
            RecordIssues(issues);
            return new ReferenceAttachResult(Array.Empty<ScriptInstance>(), Array.Empty<VmResult>(), reason, detail) { Issues = issues };
        }
        var alreadyBound = _references.TryGetValue(owner, out var existing);
        if (alreadyBound && existing.Cell != cell) return Refuse("the owner is already bound to a different cell");
        if (owner.Kind != PapyrusOwnerKind.PlacedReference)
            return Refuse("compiled reference attachments require a placed-reference owner");
        if (sourceUnsupported) return Refuse("Radicle reported a partial reference VMAD decode");
        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(name.GetString())
                    || !entry.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array
                    || properties.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.Object)))
                return Refuse("compiled script metadata has an invalid attachment or property shape");
            var attachments = ScriptAttachments.Parse(metadata);
            var normalized = ScriptAttachments.ToMetadataJson(attachments);
            if (alreadyBound)
                return existing.Metadata == normalized ? existing.Result
                    : Refuse("the owner is already bound to different compiled script metadata");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prepared = new List<(string Name, Dictionary<string, PapyrusValue> Properties)>();
            var issues = new List<AttachIssue>();
            var refusedScripts = 0;
            var unbound = 0;
            foreach (var attachment in attachments)
            {
                if (!names.Add(attachment.Name)) return Refuse("duplicate script attachments are unsupported", $"script={attachment.Name}");
                var before = issues.Count;
                if (PrepareScript(owner, attachment, issues) is { } properties)
                {
                    unbound += issues.Count - before;
                    prepared.Add((attachment.Name, properties));
                }
                else refusedScripts++;
            }
            RefusedScripts += refusedScripts;
            if (prepared.Count == 0 && attachments.Count > 0)
            {
                var first = issues[0];
                return Refuse(first.Reason, $"script={first.Script}{(first.Property is null ? "" : " property=" + first.Property)}", issues);
            }
            var instances = prepared.Select(script => Cells.Bind(cell, owner.Key, script.Name, script.Properties, owner)).ToArray();
            foreach (var instance in instances) Events.Register(owner, instance);
            var results = instances.Select(Host.Initialize).ToArray();
            FailedInitializations += results.Count(result => !result.Completed && result.Reason != VmStopReason.Waiting);
            var result = new ReferenceAttachResult(instances, results, null) { Issues = issues };
            if (issues.Count > 0)
            {
                PartialReferences++;
                UnboundProperties += unbound;
                RecordIssues(issues);
            }
            _references.Add(owner, (cell, normalized, result));
            return result;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or JsonException)
        {
            return Refuse(error.Message);
        }
    }

    private void RecordIssues(IReadOnlyList<AttachIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (_attachIssues.Count == MaxAttachIssues) _attachIssues.RemoveAt(0);
            _attachIssues.Add(issue);
        }
    }

    // Invariant: a script that cannot bind is refused whole and named; an unsupported or unbindable property leaves only that property unbound and named, and nothing is mutated here.
    private Dictionary<string, PapyrusValue>? PrepareScript(PapyrusOwnerId owner, ScriptAttachment attachment, List<AttachIssue> issues)
    {
        AttachIssue Issue(string reason, string? property = null) => new(owner.Key, attachment.Name, property, reason);
        if (!Host.HasScript(attachment.Name))
        {
            issues.Add(Issue($"the cache has no script named {attachment.Name}"));
            return null;
        }
        if (Host.HasInvalidParentChain(attachment.Name))
        {
            issues.Add(Issue($"{attachment.Name}: missing or cyclic parent chain"));
            return null;
        }
        if (attachment.Values.GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            issues.Add(Issue("duplicate property names are unsupported", duplicate.Key));
            return null;
        }
        var supported = new List<ScriptValue>();
        foreach (var value in attachment.Values)
        {
            if (value.IsSupported) supported.Add(value);
            else issues.Add(Issue($"an attachment carries an unsupported property value: {value.Reason}", value.Name));
        }
        VmadBindReport bound;
        try { bound = VmadValueBinder.Bind(owner, supported.Select(Shape)); }
        catch (ArgumentException error)
        {
            issues.Add(Issue(error.Message));
            return null;
        }
        var properties = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in bound.Properties)
        {
            if (!property.IsBound) issues.Add(Issue(property.Reason ?? "the value could not be bound", property.Name));
            else properties[property.Name] = property.Value;
        }
        return properties;
    }

    public void DetachCell(string cell)
    {
        Cells.DetachCell(cell);
        foreach (var entry in _references.Where(entry => entry.Value.Cell == cell)) Events.Park(entry.Key);
    }

    public void ReattachCell(string cell, IReadOnlySet<PapyrusOwnerId>? admitted = null)
    {
        Cells.ReattachCell(cell, admitted?.Select(owner => owner.Key).ToHashSet(StringComparer.Ordinal));
        foreach (var entry in _references.Where(entry => entry.Value.Cell == cell
                     && (admitted is null || admitted.Contains(entry.Key)))) Events.Unpark(entry.Key);
    }

    public EventBatch Trigger(PapyrusOwnerId area, PapyrusOwnerId actor, bool entering, string? scriptType = null) =>
        Events.Send(new EngineEvent(entering ? "OnTriggerEnter" : "OnTriggerLeave",
            new[] { PapyrusValue.FromObject(new EngineObjectReference(actor) { ScriptType = scriptType }) }, area));

    public EventBatch Activate(PapyrusOwnerId reference, PapyrusOwnerId activator, string? scriptType = null)
    {
        return Events.Send(new EngineEvent("OnActivate",
            new[] { PapyrusValue.FromObject(new EngineObjectReference(activator) { ScriptType = scriptType }) }, reference));
    }

    private static VmadValueShape Shape(ScriptValue value) => value.Kind switch
    {
        ScriptValueKind.Int => new(value.Name, "Int", Integer: long.Parse(value.Value!, CultureInfo.InvariantCulture)),
        ScriptValueKind.Float => new(value.Name, "Float", Real: float.Parse(value.Value!, CultureInfo.InvariantCulture)),
        ScriptValueKind.Bool => new(value.Name, "Bool", Boolean: bool.Parse(value.Value!)),
        ScriptValueKind.String => new(value.Name, "String", Text: value.Value),
        ScriptValueKind.Object => new(value.Name, "Object", Object: value.Value),
        ScriptValueKind.Array => new(value.Name, "Array", ElementCount: value.Elements!.Count, ElementType: value.ElementType,
            Elements: value.Elements.Select(Shape).ToList()),
        ScriptValueKind.Struct => new(value.Name, "Struct", ElementCount: value.Members!.Count, Members: value.Members.Select(Shape).ToList()),
        _ => new(value.Name, "Unsupported"),
    };
}
