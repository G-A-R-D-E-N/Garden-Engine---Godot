using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Ui.DevConsole.DebugChecks;

public sealed class RdfProcess : IRdfProcess
{
    private readonly string _script;

    public RdfProcess(string script)
    {
        _script = script;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> args, Action<string> onStdout, Action<string> onStderr, CancellationToken cancel)
    {
        if (!File.Exists(_script))
            throw new RdfException("The real-data-fast launcher was not found at " + _script + ". Install it or set " + RdfClient.ScriptVariable + " to its rdf.ps1.");
        var info = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var part in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _script })
            info.ArgumentList.Add(part);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onStdout(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onStderr(e.Data); };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new RdfException("Could not start powershell: " + ex.Message);
        }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var kill = cancel.Register(() => Kill(process));
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Kill(process);
        }
        return process.ExitCode;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
        catch (AggregateException)
        {
        }
    }
}
