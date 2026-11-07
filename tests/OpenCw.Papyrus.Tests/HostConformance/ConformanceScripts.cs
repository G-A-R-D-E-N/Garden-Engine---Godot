using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Radicle.Interop;

namespace OpenCw.Papyrus.Tests;

internal static class ConformanceScripts
{
    internal const string Static =
        "Scriptname Probe\n" +
        "Function Sink(Int number, Float fraction, Bool flag, String label) global native\n" +
        "Function Run()\n" +
        "  Sink(7, 2.5, True, \"hi\")\n" +
        "EndFunction\n";

    internal const string Instance =
        "Scriptname Probe\n" +
        "Function Poke() native\n" +
        "Function Run()\n" +
        "  Poke()\n" +
        "EndFunction\n";

    internal const string Boot =
        "Scriptname Boot\n" +
        "Function OnInit()\n" +
        "EndFunction\n" +
        "Function Run()\n" +
        "EndFunction\n";

    internal const string Fragile =
        "Scriptname Fragile\n" +
        "Function Boom() global native\n" +
        "Function OnInit()\n" +
        "  Boom()\n" +
        "EndFunction\n" +
        "Function Run()\n" +
        "EndFunction\n";

    internal const string Latent =
        "Scriptname Probe\n" +
        "Int Function Park() global native\n" +
        "Function Sink(Int number) global native\n" +
        "Function Run()\n" +
        "  Int got = Park()\n" +
        "  Sink(got)\n" +
        "EndFunction\n";

    internal const string Spin =
        "Scriptname Spin\n" +
        "Function Run()\n" +
        "  Int n = 0\n" +
        "  While n == 0\n" +
        "    n = 0\n" +
        "  EndWhile\n" +
        "EndFunction\n";

    internal const string Quick =
        "Scriptname Quick\n" +
        "Function Run()\n" +
        "EndFunction\n";

    internal const string Property =
        "Scriptname Probe\n" +
        "Int Property Count Auto\n" +
        "Function Sink(Int number) global native\n" +
        "Function Read(Probe target)\n" +
        "  Sink(target.Count)\n" +
        "EndFunction\n";

    internal const string Override =
        "Scriptname Probe\n" +
        "Function Sink(Int number) global native\n" +
        "Function Ping()\n" +
        "  Sink(1)\n" +
        "EndFunction\n";

    internal const string Empty =
        "Scriptname Probe\n" +
        "Function Run()\n" +
        "EndFunction\n";

    internal static IReadOnlyDictionary<string, byte[]> Compile(IEnumerable<(string Name, string Source)> scripts)
    {
        var batch = scripts.ToArray();
        var root = Directory.CreateTempSubdirectory("opencw-papyrus-").FullName;
        try
        {
            foreach (var (name, source) in batch)
            {
                File.WriteAllBytes(Path.Combine(root, name + ".psc"), Encoding.UTF8.GetBytes(source));
            }
            var pex = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (name, source) in batch)
            {
                var compile = PapyrusCompiler.CompileSource(Encoding.UTF8.GetBytes(source), name + ".psc", imports: root);
                using var compiled = compile.Pex
                    ?? throw new InvalidOperationException($"{name}.psc did not compile: " +
                        string.Join("; ", compile.Diagnostics.Select(d => $"{d.Severity} {d.Code} {d.Line}:{d.Column} {d.Message}")));
                pex[name] = compiled.ToBytes();
            }
            return pex;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
