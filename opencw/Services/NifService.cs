using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace OpenCommonwealth.Services;

public static class NifService
{
	private static string? ResolveExe() => ToolPaths.Niftool();

	// Set when the niftool on disk predates `serve` (added 2026-07-23). Conversion still works - it
	// falls back to one process per mesh - but that is ~900ms each instead of ~8ms, so say so.
	public static string? Diagnostic { get; private set; }

	public const int RequiredSchema = 2;

	private sealed record SchemaProbe(string? Exe, DateTime Stamp, int Schema);

	private static SchemaProbe? _schema;

	public static int NiftoolSchema()
	{
		var exe = ResolveExe();
		var stamp = exe != null && File.Exists(exe) ? File.GetLastWriteTimeUtc(exe) : default;
		if (_schema is { } probe && probe.Exe == exe && probe.Stamp == stamp) return probe.Schema;
		var schema = 0;
		if (exe != null)
			try { schema = SchemaOf(ProcessRunner.Run(new ProcessStartInfo(exe, "version") { WorkingDirectory = Path.GetDirectoryName(exe) ?? "." }, TimeSpan.FromSeconds(30))); }
			catch { }
		_schema = new SchemaProbe(exe, stamp, schema);
		return schema;
	}

	public static int SchemaOf(ProcessResult run)
	{
		if (!run.Started || run.TimedOut || run.ExitCode != 0) return 0;
		try
		{
			using var reply = JsonDocument.Parse(run.StdOut);
			return reply.RootElement.ValueKind == JsonValueKind.Object
				&& reply.RootElement.TryGetProperty("schemaVersion", out var schema)
				&& schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var v) && v >= 0 ? v : 0;
		}
		catch (JsonException) { return 0; }
	}

	public static string Geo(string nifPath, bool withSkin = false, bool withCollision = false)
	{
		nifPath = (nifPath ?? "").Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
		if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
		if (!withSkin && GeoWorker.Serves(withCollision) && GeoWorker.TryGet(out var worker))
		{
			// Kill() already accounts for the worker leaving the pool, so a killed one must not
			// also go through Return() -- that double-counts it and can hand a dead process to the
			// next caller.
			try
			{
				var geo = worker!.Geo(nifPath);
				GeoWorker.Return(worker);
				return geo;
			}
			catch (Exception ex)
			{
				worker!.Kill();
				return $"Failed to run niftool: {ex.Message}";
			}
		}
		var args = new List<string> { "geo", nifPath };
		if (withSkin) args.Add("--skin");
		if (withCollision) args.Add("--collision");
		return Run(args);
	}

	// One niftool spawn costs ~900 ms while the parse is ~8 ms, so `geo` runs against a pool of
	// long-lived `niftool serve` processes; each caller borrows one rather than starting a process.
	private sealed class GeoWorker
	{
		private const string Eor = "__NIFTOOL_EOR__";
		private static readonly ConcurrentBag<GeoWorker> Idle = new();
		private static int _live;
		private static int _max = -1;
		private static bool _noServe;
		private static bool _collision;

		private readonly Process _proc;
		private GeoWorker(Process p) => _proc = p;

		public static void Configure(int max, bool withCollision)
		{
			_max = max;
			_collision = withCollision;
		}

		// The pool is process-wide and every worker was started with the same flags, so a
		// request can only be served when its collision flag matches the pool's.
        public static bool Serves(bool withCollision) => withCollision == _collision;

        public static bool TryGet(out GeoWorker? worker)
        {
            worker = null;
            if (_max == 0 || _noServe) return false;
            if (Idle.TryTake(out var w)) { if (!w._proc.HasExited) { worker = w; return true; } Interlocked.Decrement(ref _live); }

            var cap = _max > 0 ? _max : Environment.ProcessorCount * 4;
            if (Interlocked.Increment(ref _live) > cap) { Interlocked.Decrement(ref _live); return false; }

            var exe = ToolPaths.Niftool();
            if (exe == null) { Interlocked.Decrement(ref _live); return false; }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ".",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                psi.ArgumentList.Add("serve");
                if (_collision) psi.ArgumentList.Add("--collision");
                var p = ProcessRunner.Start(psi);
                if (p == null) { Interlocked.Decrement(ref _live); return false; }
                p.ErrorDataReceived += static (_, _) => { };
                p.BeginErrorReadLine();
                var started = new GeoWorker(p);
                if (!started.Answers())
                {
                    _noServe = true;
                    Diagnostic = "niftool on disk is too old - it has no `serve` mode, so every mesh "
                               + "costs a process spawn. Rebuild it: xmake -y in tools/niftool.";
                    started.Kill();
                    return false;
                }
                worker = started;
                return true;
            }
            catch { Interlocked.Decrement(ref _live); return false; }
        }

        public static void Warm(int count)
        {
            var started = new ConcurrentBag<GeoWorker>();
            Parallel.For(0, count, _ => { if (TryGet(out var w)) started.Add(w!); });
            foreach (var w in started) Return(w);
        }

        public static void Return(GeoWorker w)
        {
            if (w._proc.HasExited) { Interlocked.Decrement(ref _live); return; }
            Idle.Add(w);
        }

        // A niftool without `serve` treats the argument as an unknown command and exits, closing
        // stdout - so one round trip separates "old binary" from "working pool" with no version
        // handshake to keep in sync. The probe path is not a real file; geo answers with an error
        // block and the end-of-record marker, which is all this checks for.
        private bool Answers()
        {
            try
            {
                _proc.StandardInput.WriteLine("__NIFTOOL_PROBE__");
                _proc.StandardInput.Flush();
                string? line;
                while ((line = _proc.StandardOutput.ReadLine()) != null) if (line == Eor) return true;
                return false;
            }
            catch { return false; }
        }

        public string Geo(string nifPath)
        {
            _proc.StandardInput.WriteLine(nifPath);
            _proc.StandardInput.Flush();

            var sb = new StringBuilder();
            string? line;
            while ((line = _proc.StandardOutput.ReadLine()) != null)
            {
                if (line == Eor) return sb.ToString();
				sb.Append(line).Append('\n');
            }
            throw new IOException("niftool serve ended unexpectedly");
        }

        public void Kill()
        {
            try { _proc.Kill(true); } catch { }
            Interlocked.Decrement(ref _live);
        }

        // _live must go back to zero, or a second conversion run in the same editor session starts
        // already at its cap and silently falls back to one process per mesh.
        public static void ShutdownAll()
        {
            while (Idle.TryTake(out var w))
            {
                try { w._proc.StandardInput.WriteLine("__QUIT__"); w._proc.StandardInput.Flush(); } catch { }
                try { if (!w._proc.WaitForExit(2000)) w._proc.Kill(true); } catch { }
            }
            Interlocked.Exchange(ref _live, 0);
            _noServe = false;
            Diagnostic = null;
        }
    }

    public static void ConfigureGeoWorkers(int max, bool withCollision = false) => GeoWorker.Configure(max, withCollision);
    public static void ShutdownGeoWorkers() => GeoWorker.ShutdownAll();
    public static void WarmGeoWorkers(int count) => GeoWorker.Warm(count);

    // Matches the Creation Kit's "Materials Used" box: it counts shader-bearing shapes, not distinct
    // material files. Verified against NorthBridge.nif (8) and NorthBridgePreWar.nif (10), which have
    // only 6 and 8 distinct .bgsm paths respectively.
    public static int? MaterialCount(string nifPath) => MaterialCountFromGeo(Geo(nifPath));

    public static int? MaterialCountFromGeo(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson) || geoJson[0] != '{') return null;
        try
        {
            if (Newtonsoft.Json.Linq.JObject.Parse(geoJson)["shapes"] is not Newtonsoft.Json.Linq.JArray shapes) return null;
            var count = 0;
            foreach (var token in shapes)
            {
                if (token is not Newtonsoft.Json.Linq.JObject shape) continue;
                if (shape["lighting"] != null || (bool?)shape["effectShader"] == true) count++;
            }
            return count;
        }
        catch { return null; }
    }

    public static string Collision(string nifPath)
    {
		nifPath = (nifPath ?? "").Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(nifPath)) return "Provide 'nif_path'.";
		if (!File.Exists(nifPath)) return $"NIF not found: {nifPath}";
		return Run(new List<string> { "collision", nifPath });
    }

    private static string Run(List<string> args)
    {
        var exe = ResolveExe();
        if (exe == null)
			return "niftool not found. Build it with `xmake -y` in tools/niftool, or set the " +
				   "NIFTOOL_PATH environment variable to its full path.";

        var psi = new ProcessStartInfo
        {
            FileName = exe,
			WorkingDirectory = Path.GetDirectoryName(exe) ?? ".",
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            var run = ProcessRunner.Run(psi, TimeSpan.FromSeconds(120));
			if (!run.Started) return "Failed to start niftool.exe.";
			if (run.TimedOut) return "niftool timed out after 120s (killed).";

            return string.IsNullOrWhiteSpace(run.Combined)
				? $"niftool exited {run.ExitCode} with no output."
                : run.Combined;
        }
        catch (Exception ex)
        {
			return $"Failed to run niftool: {ex.Message}";
        }
    }
}
