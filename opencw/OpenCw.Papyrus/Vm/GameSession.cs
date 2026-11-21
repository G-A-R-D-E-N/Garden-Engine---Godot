using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vmad;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record ReferenceAttachResult(IReadOnlyList<ScriptInstance> Instances,
    IReadOnlyList<VmResult> Initialization, string? Refusal);

public sealed class GameSession : IDisposable
{
    private readonly Dictionary<PapyrusOwnerId, (string Cell, string Metadata, ReferenceAttachResult Result)> _references = new();
    private readonly Dictionary<(string Quest, string Script), ScriptInstance> _fragmentScripts = new();
    private readonly EvidenceGate _gate;

    public GameSession(IScriptHost host, EvidenceGate gate, IReadOnlyList<QuestStartupInput>? questStartup = null)
    {
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
    public EvidenceGate Gate => _gate;
    public NativeSet NativeSinks { get; }
    public CellScriptSession Cells { get; }
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
    public int FailedInitializations { get; private set; }

    public delegate byte[] QuestStartupReader(string cacheRoot, ScriptCacheManifest manifest, string? assetCacheRoot);

    public static GameSession Load(string cacheRoot, IReadOnlyList<string>? expectedLoadOrderRoots = null,
        IReadOnlyList<string>? expectedPlugins = null, string? assetCacheRoot = null,
        QuestStartupReader? readQuestStartup = null)
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
            var session = new GameSession(host, gate, startup.Inputs) { PexScripts = pex };
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

    // Requires: called outside a native callback, because the host refuses to attach or run a script inside one.
    public int RunQueuedFragments()
    {
        var ran = 0;
        foreach (var fragment in QuestStages.TakeQueuedFragments())
        {
            var done = false;
            try { done = RunFragment(fragment); }
            catch (Exception) { done = false; } // Invariant: one failing fragment must not drop the rest of the drain.
            if (done) ran++;
            else FragmentsRefused++;
        }
        FragmentsRun += ran;
        return ran;
    }

    private bool RunFragment(QueuedFragment fragment)
    {
        if (!Quests.Started.Contains(fragment.Quest)) return false;
        var key = (fragment.Quest, fragment.Script.ToLowerInvariant());
        if (_fragmentScripts.TryGetValue(key, out var instance) && instance.Detached)
        {
            _fragmentScripts.Remove(key);
            instance = null!;
        }
        if (instance is null)
        {
            instance = Quests.InstancesFor(fragment.Quest).FirstOrDefault(entry =>
                string.Equals(entry.ScriptName, fragment.Script, StringComparison.OrdinalIgnoreCase));
            if (instance is null)
            {
                var input = QuestStartup.First(entry => entry.Quest == fragment.Quest);
                var values = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
                if (!Host.HasScript(fragment.Script) || Host.HasInvalidParentChain(fragment.Script)) return false;
                if (string.Equals(fragment.Script, input.Metadata?.FragmentScript, StringComparison.OrdinalIgnoreCase)
                    && QuestStartupBinding.PrepareFragmentScript(input, out values) is not null) return false;
                try
                {
                    instance = Host.Attach(fragment.Script, values, PapyrusOwnerId.ForForm(fragment.Quest));
                    if (!Host.Initialize(instance).Completed)
                    {
                        Host.Discard(instance);
                        return false;
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException)
                {
                    return false;
                }
            }
            _fragmentScripts[key] = instance;
        }
        try
        {
            return Host.RunEvent(instance, fragment.Function).Completed;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            _fragmentScripts.Remove(key);
            return false;
        }
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
        ReferenceAttachResult Refuse(string reason)
        {
            RefusedReferences++;
            return new ReferenceAttachResult(Array.Empty<ScriptInstance>(), Array.Empty<VmResult>(), reason);
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
            foreach (var attachment in attachments)
            {
                if (!names.Add(attachment.Name)) return Refuse("duplicate script attachments are unsupported");
                if (!Host.HasScript(attachment.Name)) return Refuse($"the cache has no script named {attachment.Name}");
                if (Host.HasInvalidParentChain(attachment.Name)) return Refuse($"{attachment.Name}: missing or cyclic parent chain");
                if (attachment.Values.Any(value => !value.IsSupported))
                    return Refuse("an attachment carries an unsupported property value");
                var bound = VmadValueBinder.Bind(owner, attachment.Values.Select(Shape));
                if (bound.Unsupported != 0) return Refuse(string.Join("; ", bound.UnsupportedShapes));
                var properties = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in bound.Properties)
                    if (!properties.TryAdd(property.Name, property.Value))
                        return Refuse("duplicate property names are unsupported");
                prepared.Add((attachment.Name, properties));
            }
            var instances = prepared.Select(script => Cells.Bind(cell, owner.Key, script.Name, script.Properties, owner)).ToArray();
            foreach (var instance in instances) Events.Register(owner, instance);
            var results = instances.Select(Host.Initialize).ToArray();
            FailedInitializations += results.Count(result => !result.Completed);
            var result = new ReferenceAttachResult(instances, results, null);
            _references.Add(owner, (cell, normalized, result));
            return result;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or JsonException)
        {
            return Refuse(error.Message);
        }
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

    public EventBatch Trigger(PapyrusOwnerId area, PapyrusOwnerId actor, bool entering) =>
        Events.Send(new EngineEvent(entering ? "OnTriggerEnter" : "OnTriggerLeave",
            new[] { PapyrusValue.FromObject(new EngineObjectReference(actor)) }, area));

    public EventBatch Activate(PapyrusOwnerId reference, PapyrusOwnerId activator)
    {
        return Events.Send(new EngineEvent("OnActivate",
            new[] { PapyrusValue.FromObject(new EngineObjectReference(activator)) }, reference));
    }

    private static VmadValueShape Shape(ScriptValue value) => value.Kind switch
    {
        ScriptValueKind.Int => new(value.Name, "Int", Integer: long.Parse(value.Value!, CultureInfo.InvariantCulture)),
        ScriptValueKind.Float => new(value.Name, "Float", Real: float.Parse(value.Value!, CultureInfo.InvariantCulture)),
        ScriptValueKind.Bool => new(value.Name, "Bool", Boolean: bool.Parse(value.Value!)),
        ScriptValueKind.String => new(value.Name, "String", Text: value.Value),
        ScriptValueKind.Object => new(value.Name, "Object", Object: value.Value),
        _ => new(value.Name, "Unsupported"),
    };
}
