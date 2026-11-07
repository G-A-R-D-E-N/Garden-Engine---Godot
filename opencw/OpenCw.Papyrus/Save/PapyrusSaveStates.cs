using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCommonwealth.Services.Papyrus.Save;

/// <summary>One suspended thread, with the script fingerprint it was suspended against.</summary>
public sealed record SavedThread(
    string OwnerKey,
    string Script,
    string Fingerprint,
    string Function,
    int Pc,
    IReadOnlyList<string> Locals,
    long? WaitingToken = null,
    string? WaitingNative = null);

/// <summary>One typed registration, with its whole payload.</summary>
public sealed record SavedRegistration(
    string OwnerKey,
    string Kind,
    string EventName,
    double Interval,
    bool Repeats,
    string Clock,
    string? SourceOwnerKey = null);

/// <summary>One timer, with its accumulated time and fire count.</summary>
public sealed record SavedTimer(string OwnerKey, string EventName, double Elapsed, long Fires);

/// <summary>One objective's state.</summary>
public sealed record SavedObjective(string OwnerKey, int Index, int State);

public sealed record PapyrusSaveState(
    int Version,
    IReadOnlyList<SavedThread> Threads,
    IReadOnlyList<SavedRegistration> Registrations,
    IReadOnlyList<SavedTimer> Timers,
    IReadOnlyList<SavedObjective> Objectives)
{
    public string Serialize()
    {
        PapyrusSaveStates.ValidateRegistrations(Registrations);
        return JsonSerializer.Serialize(new
        {
            version = Version,
            threads = Threads.Select(thread => new
            {
                owner = thread.OwnerKey,
                script = thread.Script,
                fingerprint = thread.Fingerprint,
                function = thread.Function,
                pc = thread.Pc,
                locals = thread.Locals,
                waitingToken = thread.WaitingToken,
                waitingNative = thread.WaitingNative,
            }),
            registrations = Registrations.Select(registration => new
            {
                owner = registration.OwnerKey,
                kind = registration.Kind,
                @event = registration.EventName,
                interval = registration.Interval,
                repeats = registration.Repeats,
                clock = registration.Clock,
                source = registration.SourceOwnerKey,
            }),
            timers = Timers.Select(timer => new
            {
                owner = timer.OwnerKey,
                @event = timer.EventName,
                elapsed = timer.Elapsed,
                fires = timer.Fires,
            }),
            objectives = Objectives.Select(objective => new
            {
                owner = objective.OwnerKey,
                index = objective.Index,
                state = objective.State,
            }),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize()))).ToLowerInvariant();

    public static PapyrusSaveState Deserialize(string json)
    {
        JsonNode root;
        try
        {
            root = JsonNode.Parse(json) ?? throw new FormatException("the save state was empty");
        }
        catch (JsonException error)
        {
            throw new FormatException("the save state is not valid JSON", error);
        }
        if (root is not JsonObject state)
        {
            throw new FormatException("the save state must be an object");
        }

        var version = (int?)state["version"] ?? throw new FormatException("the save state needs a version");
        PapyrusSaveStates.ValidateVersion(version);

        var threads = new List<SavedThread>();
        foreach (var node in Entries(state, "threads"))
        {
            threads.Add(new SavedThread(
                Text(node, "owner"),
                Text(node, "script"),
                Text(node, "fingerprint"),
                Text(node, "function"),
                (int)Number(node, "pc"),
                Strings(node, "locals"),
                (long?)node["waitingToken"],
                (string?)node["waitingNative"]));
        }

        var registrations = new List<SavedRegistration>();
        foreach (var node in Entries(state, "registrations"))
        {
            registrations.Add(new SavedRegistration(
                Text(node, "owner"),
                Text(node, "kind"),
                Text(node, "event"),
                Number(node, "interval"),
                Flag(node, "repeats"),
                Text(node, "clock"),
                (string?)node["source"]));
        }
        PapyrusSaveStates.ValidateRegistrations(registrations);

        var timers = new List<SavedTimer>();
        foreach (var node in Entries(state, "timers"))
        {
            timers.Add(new SavedTimer(
                Text(node, "owner"),
                Text(node, "event"),
                Number(node, "elapsed"),
                (long?)node["fires"] ?? throw new FormatException("the save state entry needs a fires")));
        }

        var objectives = new List<SavedObjective>();
        foreach (var node in Entries(state, "objectives"))
        {
            objectives.Add(new SavedObjective(
                Text(node, "owner"),
                (int)Number(node, "index"),
                (int)Number(node, "state")));
        }

        return new PapyrusSaveState(version, threads, registrations, timers, objectives);
    }

    private static IEnumerable<JsonObject> Entries(JsonObject state, string name) =>
        state[name] is JsonArray array
            ? array.Select(entry => entry as JsonObject ?? throw new FormatException($"the {name} array needs object entries"))
            : throw new FormatException($"the save state must carry a {name} array");

    private static string Text(JsonObject node, string name) => (string?)node[name] ?? string.Empty;

    private static double Number(JsonObject node, string name) =>
        (double?)node[name] ?? throw new FormatException($"the save state entry needs a {name}");

    private static bool Flag(JsonObject node, string name) => (bool?)node[name] ?? false;

    private static IReadOnlyList<string> Strings(JsonObject node, string name) =>
        node[name] is JsonArray array ? array.Select(entry => (string?)entry ?? string.Empty).ToList() : Array.Empty<string>();
}

public enum ThreadRestoreState
{
    Resumed,

    /// <summary>The script changed, so the thread was rejected rather than resumed at a guessed position.</summary>
    RejectedChangedScript,

    /// <summary>The script is not in the current program set.</summary>
    RejectedUnknownScript,

    RejectedLatentWait,
    RejectedFrameRestore,
}

public sealed record ThreadRestore(string OwnerKey, string Script, ThreadRestoreState State, string Reason);

public sealed record RestoreReport(
    IReadOnlyList<ThreadRestore> Threads,
    int Resumed,
    int Rejected,
    IReadOnlyList<SavedRegistration> Registrations,
    IReadOnlyList<SavedTimer> Timers,
    IReadOnlyList<SavedObjective> Objectives)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"threads={Threads.Count} resumed={Resumed} rejected={Rejected} registrations={Registrations.Count} timers={Timers.Count} objectives={Objectives.Count}\n");
        foreach (var thread in Threads)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {thread.OwnerKey} {thread.Script} {thread.State.ToString().ToLowerInvariant()} {thread.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Captures and restores the Papyrus runtime state.
/// </summary>
/// <remarks>
/// Every suspended thread is stored with the fingerprint of the script it was suspended against, so a
/// restore compares the fingerprint first: a script that changed is rejected outright rather than
/// resumed at a program counter that no longer means the same thing, and a script that is absent is
/// rejected too. Owner keys round-trip unchanged, so two owners that share a base form key stay
/// distinct. Typed registrations, timers and objectives carry their whole payload through the
/// serialized form.
/// </remarks>
public static class PapyrusSaveStates
{
    public const int CurrentVersion = 2;

    internal static void ValidateRegistrations(IEnumerable<SavedRegistration> registrations)
    {
        foreach (var registration in registrations)
            if (string.Equals(registration.Kind, "RemoteEvent", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(registration.SourceOwnerKey))
                throw new FormatException("a remote event registration needs a source owner key");
    }

    internal static void ValidateVersion(int version)
    {
        if (version != CurrentVersion)
        {
            throw new FormatException($"unsupported Papyrus save version {version}");
        }
    }

    public static PapyrusSaveState Capture(
        IEnumerable<SavedThread> threads,
        IEnumerable<SavedRegistration> registrations,
        IEnumerable<SavedTimer> timers,
        IEnumerable<SavedObjective> objectives) =>
        new(CurrentVersion,
            threads.OrderBy(thread => thread.OwnerKey, StringComparer.Ordinal).ToList(),
            registrations.OrderBy(registration => registration.OwnerKey, StringComparer.Ordinal).ToList(),
            timers.OrderBy(timer => timer.OwnerKey, StringComparer.Ordinal).ToList(),
            objectives.OrderBy(objective => objective.OwnerKey, StringComparer.Ordinal)
                .ThenBy(objective => objective.Index).ToList());

    public static RestoreReport Restore(PapyrusSaveState state, IReadOnlyDictionary<string, string> currentFingerprints)
    {
        ValidateVersion(state.Version);
        ValidateRegistrations(state.Registrations);
        var outcomes = new List<ThreadRestore>(state.Threads.Count);
        var rejected = 0;

        foreach (var thread in state.Threads)
        {
            if (thread.WaitingToken is not null || thread.WaitingNative is not null)
            {
                rejected++;
                outcomes.Add(new ThreadRestore(thread.OwnerKey, thread.Script, ThreadRestoreState.RejectedLatentWait,
                    $"waiting on {thread.WaitingNative} token {thread.WaitingToken}; the external answer cannot be restored"));
                continue;
            }
            if (!currentFingerprints.TryGetValue(thread.Script, out var current))
            {
                rejected++;
                outcomes.Add(new ThreadRestore(thread.OwnerKey, thread.Script, ThreadRestoreState.RejectedUnknownScript,
                    "the script is not in the current program set, so the thread was rejected"));
                continue;
            }
            if (!string.Equals(current, thread.Fingerprint, StringComparison.Ordinal))
            {
                rejected++;
                outcomes.Add(new ThreadRestore(thread.OwnerKey, thread.Script, ThreadRestoreState.RejectedChangedScript,
                    $"the script changed from {thread.Fingerprint} to {current}, so the thread was not resumed at a guessed program counter"));
                continue;
            }

            rejected++;
            outcomes.Add(new ThreadRestore(thread.OwnerKey, thread.Script, ThreadRestoreState.RejectedFrameRestore,
                "live VM frame restoration is not implemented, so the saved thread was rejected"));
        }

        return new RestoreReport(outcomes, 0, rejected, state.Registrations, state.Timers, state.Objectives);
    }
}
