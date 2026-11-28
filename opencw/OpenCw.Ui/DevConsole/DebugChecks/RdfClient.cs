using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;

public interface IRdfProcess
{
    Task<int> RunAsync(IReadOnlyList<string> args, Action<string> onStdout, Action<string> onStderr, CancellationToken cancel);
}

public sealed class RdfClient
{
    public const string DefaultScript = @"C:\ci\real-data-fast\bin\rdf.ps1";
    public const string ScriptVariable = "RDF_SCRIPT";
    public const string DefaultTier = "fast";
    public const int StderrKept = 20;

    private static readonly Regex RefPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._/@~^-]{0,199}$", RegexOptions.CultureInvariant);

    private readonly IRdfProcess _process;
    private int _busy;
    private CancellationTokenSource? _cancel;
    private volatile RdfList? _boxes;

    public RdfClient(IRdfProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
    }

    public static string ScriptPath() =>
        Environment.GetEnvironmentVariable(ScriptVariable) is { Length: > 0 } path ? path : DefaultScript;

    public static RdfClient CreateDefault() => new(new RdfProcess(ScriptPath()));

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public RdfList? Boxes => _boxes;

    public void Cancel()
    {
        try
        {
            _cancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public Task<RdfList> ListAsync() => Exclusive(ListCore);

    public Task<RdfTests> TestsAsync(string box, string? tier) => Exclusive(async cancel =>
    {
        var (resolvedBox, resolvedTier) = await Resolve(box, tier, cancel).ConfigureAwait(false);
        return await TestsCore(resolvedBox.Id, resolvedTier, cancel).ConfigureAwait(false);
    });

    public Task<RdfEvent?> RunBoxAsync(string box, string? tier, string? test, Action<RdfEvent> onEvent) => Exclusive(async cancel =>
    {
        var (resolvedBox, resolvedTier) = await Resolve(box, tier, cancel).ConfigureAwait(false);
        var args = new List<string> { "-Box", resolvedBox.Id, "-Tier", resolvedTier };
        if (!string.IsNullOrEmpty(test))
        {
            var tests = await TestsCore(resolvedBox.Id, resolvedTier, cancel).ConfigureAwait(false);
            if (tests.Unavailable is { } reason)
                throw new RdfException("Single tests are not available for " + resolvedBox.Id + "/" + resolvedTier + ": " + reason);
            if (test.StartsWith('-') || !tests.Tests.Contains(test, StringComparer.Ordinal))
                throw new RdfException("Unknown test for " + resolvedBox.Id + "/" + resolvedTier + ": " + test);
            args.AddRange(new[] { "-Test", test });
        }
        args.Add("-Json");
        return await RunEvents(args, onEvent, cancel).ConfigureAwait(false);
    });

    public Task<RdfEvent?> RunChangedAsync(string? baseRef, Action<RdfEvent> onEvent) => Exclusive(cancel =>
    {
        var args = new List<string> { "-Changed" };
        if (!string.IsNullOrEmpty(baseRef))
        {
            if (!IsSafeRef(baseRef)) throw new RdfException("Not a valid base ref: " + baseRef);
            args.AddRange(new[] { "-Base", baseRef });
        }
        args.Add("-Json");
        return RunEvents(args, onEvent, cancel);
    });

    public Task<RdfEvent?> RunAllAsync(Action<RdfEvent> onEvent) =>
        Exclusive(cancel => RunEvents(new[] { "-All", "-Json" }, onEvent, cancel));

    public Task<RdfList> ResultsAsync() => Exclusive(async cancel =>
    {
        var (code, lines, errors) = await Collect(new[] { "-Results", "-Json" }, cancel).ConfigureAwait(false);
        if (code != 0) throw Failure("-Results", code, errors);
        return RdfJson.ParseResults(string.Join('\n', lines));
    });

    public static bool IsSafeRef(string value) => RefPattern.IsMatch(value) && !value.Contains("..", StringComparison.Ordinal);

    private async Task<T> Exclusive<T>(Func<CancellationToken, Task<T>> body)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new RdfBusyException();
        var source = new CancellationTokenSource();
        _cancel = source;
        try
        {
            return await Task.Run(() => body(source.Token)).ConfigureAwait(false);
        }
        finally
        {
            _cancel = null;
            source.Dispose();
            Volatile.Write(ref _busy, 0);
        }
    }

    private async Task<RdfList> ListCore(CancellationToken cancel)
    {
        var (code, lines, errors) = await Collect(new[] { "-List", "-Json" }, cancel).ConfigureAwait(false);
        if (code != 0) throw Failure("-List", code, errors);
        var list = RdfJson.ParseList(string.Join('\n', lines));
        _boxes = list;
        return list;
    }

    private async Task<RdfTests> TestsCore(string box, string tier, CancellationToken cancel)
    {
        var (code, lines, errors) = await Collect(new[] { "-Tests", "-Box", box, "-Tier", tier, "-Json" }, cancel).ConfigureAwait(false);
        if (code != 0) throw Failure("-Tests", code, errors);
        return RdfJson.ParseTests(string.Join('\n', lines));
    }

    private async Task<(RdfBox Box, string Tier)> Resolve(string box, string? tier, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(box) || box.StartsWith('-'))
            throw new RdfException("Not a valid box id: " + box);
        var list = _boxes ?? await ListCore(cancel).ConfigureAwait(false);
        var found = Find(list, box, tier);
        if (found is null)
        {
            list = await ListCore(cancel).ConfigureAwait(false);
            found = Find(list, box, tier);
        }
        if (found is { } hit) return hit;
        var known = list.Boxes.FirstOrDefault(b => string.Equals(b.Id, box, StringComparison.OrdinalIgnoreCase));
        throw new RdfException(known is null
            ? "Unknown box: " + box
            : "Unknown tier " + tier + " for " + known.Id + ". Tiers: " + string.Join(", ", known.Tiers));
    }

    private static (RdfBox, string)? Find(RdfList list, string box, string? tier)
    {
        var hit = list.Boxes.FirstOrDefault(b => string.Equals(b.Id, box, StringComparison.OrdinalIgnoreCase));
        if (hit is null || hit.Tiers.Count == 0) return null;
        if (string.IsNullOrEmpty(tier))
            return (hit, hit.Tiers.FirstOrDefault(t => t == DefaultTier) ?? hit.Tiers[0]);
        var match = hit.Tiers.FirstOrDefault(t => string.Equals(t, tier, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : (hit, match);
    }

    private async Task<RdfEvent?> RunEvents(IReadOnlyList<string> args, Action<RdfEvent> onEvent, CancellationToken cancel)
    {
        RdfEvent? done = null;
        var emptyPlan = false;
        var errors = new List<string>();
        var code = await _process.RunAsync(args, line =>
        {
            if (RdfJson.ParseEvent(line) is not { } evt) return;
            if (evt.Kind == RdfEvent.Done) done = evt;
            if (evt.Kind == RdfEvent.Plan && evt.Line == RdfEvent.NoChecksAffected) emptyPlan = true;
            onEvent(evt);
        }, Ring(errors), cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (done is null && emptyPlan && code == 0)
        {
            done = new RdfEvent(RdfEvent.Done, Status: "PASS", Line: RdfEvent.NoChecksAffected);
            onEvent(done);
        }
        if (done is null) throw Failure(args[0], code, errors);
        return done;
    }

    private async Task<(int Code, List<string> Lines, List<string> Errors)> Collect(IReadOnlyList<string> args, CancellationToken cancel)
    {
        var lines = new List<string>();
        var errors = new List<string>();
        var code = await _process.RunAsync(args, lines.Add, Ring(errors), cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        return (code, lines, errors);
    }

    private static Action<string> Ring(List<string> kept) => line =>
    {
        lock (kept)
        {
            kept.Add(line);
            if (kept.Count > StderrKept) kept.RemoveAt(0);
        }
    };

    private static RdfException Failure(string what, int code, IReadOnlyList<string> errors)
    {
        var tail = string.Join(' ', errors.Where(e => !string.IsNullOrWhiteSpace(e)).TakeLast(3));
        return new RdfException("The launcher failed (" + what + ", exit " + code + ")" + (tail.Length > 0 ? ": " + tail : "."));
    }
}
