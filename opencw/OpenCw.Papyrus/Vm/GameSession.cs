using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Vmad;
#if RADICLE_HOST_AVAILABLE
using OpenCommonwealth.Services.Papyrus.Host;
#endif

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed record ReferenceAttachResult(IReadOnlyList<ScriptInstance> Instances,
    IReadOnlyList<VmResult> Initialization, string? Refusal);

public sealed class GameSession
{
    private readonly Dictionary<PapyrusOwnerId, (string Cell, string Metadata, ReferenceAttachResult Result)> _references = new();
    private readonly EvidenceGate _gate;

    public GameSession(ScriptLibrary library, EvidenceGate gate, IReadOnlyList<QuestStartupInput>? questStartup = null)
        : this(new ScriptHost(library, gate), gate, questStartup)
    {
    }

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
    }

    public IScriptHost Host { get; }
    public NativeSet NativeSinks { get; }
    public CellScriptSession Cells { get; }
    public PapyrusEventBridge Events { get; }
    public QuestRuntime Quests { get; }
    public IReadOnlyList<QuestStartupInput> QuestStartup { get; }
    public IReadOnlyDictionary<string, byte[]> PexScripts { get; init; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    public QuestStartupResult? StartupResult { get; private set; }
    public int RefusedReferences { get; private set; }
    public int FailedInitializations { get; private set; }
    public int RefusedActivations { get; private set; }
    public string? LastReferenceRefusal { get; private set; }

    public static GameSession Load(string cacheRoot
#if RADICLE_HOST_AVAILABLE
        , bool useRadicleHost = false
#endif
        )
    {
        if (File.Exists(ScriptCacheLayout.MarkerPath(cacheRoot)))
            throw new InvalidOperationException("the script cache swap is incomplete; rebuild or recover it before play");
        var manifestJson = File.ReadAllText(ScriptCacheLayout.ManifestPath(cacheRoot));
        var manifest = ScriptCacheManifest.FromJson(manifestJson);
        if (manifest.FormatVersion != ScriptCacheManifest.CurrentFormatVersion
            || manifest.IrFormatVersion != PapyrusIr.FormatVersion || manifest.LoweringVersion != 1
            || !manifest.MatchesRadiclePin() || manifest.ScriptCount != manifest.Entries.Count)
            throw new InvalidOperationException("the script cache format, lowering version, count or Radicle pin does not match");
        var library = new ScriptLibrary();
        var startupBytes = File.ReadAllBytes(ScriptCacheLayout.QuestStartupPath(cacheRoot));
        var startup = QuestStartupDatabase.FromJson(System.Text.Encoding.UTF8.GetString(startupBytes));
        if (ScriptCacheBuilder.Fingerprint(startupBytes) != manifest.QuestStartupSha256
            || startup.Inputs.Count != manifest.QuestStartupCount)
            throw new InvalidOperationException("the converted quest startup payload does not match the script cache manifest");
        var pex = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in manifest.Entries)
        {
            var bytes = File.ReadAllBytes(ScriptCacheLayout.ScriptPath(cacheRoot, entry.Name));
            if (ScriptCacheBuilder.Fingerprint(bytes) != entry.IrSha256)
                throw new InvalidOperationException($"the cached IR for {entry.Name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
            foreach (var script in PapyrusIr.FromJson(System.Text.Encoding.UTF8.GetString(bytes)))
                library.Add(script);
            var pexBytes = File.ReadAllBytes(ScriptCacheLayout.PexPath(cacheRoot, entry.Name));
            if (entry.PexFile != ScriptCacheLayout.PexRelativePath(entry.Name)
                || ScriptCacheBuilder.Fingerprint(pexBytes) != entry.PexSha256)
                throw new InvalidOperationException($"the cached PEX for {entry.Name} does not match its manifest fingerprint; reconvert assets into a fresh cache");
            pex[entry.Name] = pexBytes;
        }
        if (File.Exists(ScriptCacheLayout.MarkerPath(cacheRoot))
            || File.ReadAllText(ScriptCacheLayout.ManifestPath(cacheRoot)) != manifestJson
            || !File.ReadAllBytes(ScriptCacheLayout.QuestStartupPath(cacheRoot)).SequenceEqual(startupBytes))
            throw new InvalidOperationException("the script cache changed while loading; restart with a stable cache");
        using var resource = typeof(GameSession).Assembly.GetManifestResourceStream("OpenCw.Papyrus.EvidenceRegistry")
            ?? throw new InvalidOperationException("the canonical Papyrus evidence registry is missing");
        using var reader = new StreamReader(resource);
        var gate = new EvidenceGate(EvidenceRegistry.Load(reader.ReadToEnd()));
#if RADICLE_HOST_AVAILABLE
        var radicleHost = useRadicleHost ? new RadicleScriptHost(gate, pex.Values) : null;
        GameSession session;
        if (radicleHost is null)
        {
            session = new GameSession(library, gate, startup.Inputs) { PexScripts = pex };
        }
        else
        {
            try
            {
                session = new GameSession(radicleHost, gate, startup.Inputs) { PexScripts = pex };
            }
            catch
            {
                radicleHost.Dispose();
                throw;
            }
        }
#else
        var session = new GameSession(library, gate, startup.Inputs) { PexScripts = pex };
#endif
        session.NativeSinks.BindBaseSet();
        session.NativeSinks.Install(session.Host);
        return session;
    }

    public QuestStartupResult StartNewGame()
    {
        if (StartupResult is not null) return StartupResult;
        StartupResult = Quests.StartNewGame(QuestStartup, Host);
        foreach (var input in QuestStartup)
            foreach (var instance in Quests.InstancesFor(input.Quest))
                Events.Register(PapyrusOwnerId.ForForm(input.Quest), instance);
        return StartupResult;
    }

    public ReferenceAttachResult AttachReference(string cell, PapyrusOwnerId owner, string metadata,
        bool sourceUnsupported = false)
    {
        ReferenceAttachResult Refuse(string reason)
        {
            RefusedReferences++;
            LastReferenceRefusal = reason;
            return new ReferenceAttachResult(Array.Empty<ScriptInstance>(), Array.Empty<VmResult>(), reason);
        }
        var alreadyBound = _references.TryGetValue(owner, out var existing);
        if (alreadyBound && existing.Cell != cell) return Refuse("the owner is already bound to a different cell");
        if (owner.Kind != PapyrusOwnerKind.PlacedReference)
            return Refuse("compiled reference attachments require a placed-reference owner");
        foreach (var key in new[] { "vm.session.cell_lifecycle", "vmad.reference_compiled_attachments",
                     "vmad.reference_source", "owner.engine_identity" })
        {
            var decision = _gate.Gate(key);
            if (!decision.Allowed) return Refuse($"{decision.Key}: {decision.Reason}");
        }
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
                if (attachment.Values.Count > 0)
                {
                    var decision = _gate.Gate("vmad.value_binding");
                    if (!decision.Allowed) return Refuse($"{decision.Key}: {decision.Reason}");
                }
                var bound = VmadValueBinder.Bind(owner, attachment.Values.Select(Shape));
                if (bound.Unsupported != 0) return Refuse(string.Join("; ", bound.UnsupportedShapes));
                var properties = new Dictionary<string, PapyrusValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in bound.Properties)
                    if (!properties.TryAdd(property.Name, property.Value))
                        return Refuse("duplicate property names are unsupported");
                prepared.Add((attachment.Name, properties));
            }
            if (prepared.Count > 0)
            {
                var decision = _gate.Gate(EvidenceKeys.OnInitBarrier, countApproximation: false);
                if (!decision.Allowed) return Refuse($"{decision.Key}: {decision.Reason}");
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

    public EventBatch Activate(PapyrusOwnerId reference, PapyrusOwnerId activator)
    {
        var decision = _gate.Gate("vm.session.activation_routing");
        if (!decision.Allowed)
        {
            RefusedActivations++;
            return new EventBatch("OnActivate", new[] { new EventOutcome(reference, EventDelivery.Refused, decision.Reason) },
                0, 1, 0, 0, 0);
        }
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
