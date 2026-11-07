using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Census;

/// <summary>How the effective script set treated one script.</summary>
public enum ScriptSetState
{
    /// <summary>The payload was decoded and is in the library.</summary>
    Loaded,

    /// <summary>The payload exists but could not be decoded.</summary>
    Unreadable,

    /// <summary>No payload was found for the name.</summary>
    NotLoadable,

    /// <summary>More than one candidate claims the name, so the winner is not decided.</summary>
    Ambiguous,
}

/// <summary>
/// One native call site found in a decoded script.
/// </summary>
/// <remarks>
/// <c>Target</c> is the script that owns the call: for <c>callstatic</c> the script operand, for
/// <c>callparent</c> the enclosing object's parent, and for <c>callmethod</c> the receiver's declared
/// type. A method site also carries the called function's name, because the receiver's declared type
/// is what attributes the call while the name is what the runtime dispatches on.
/// </remarks>
public sealed record NativeCallSite(
    string Script,
    string Function,
    int Instruction,
    NativeTargetKind Kind,
    string Target,
    string? CalledFunction = null);

/// <summary>One script of the effective set, with the call sites its decode produced.</summary>
public sealed record CensusScript(string Name, ScriptSetState State, IReadOnlyList<NativeCallSite> Sites);

public sealed record CollectedCallSites(CensusScript Script, int ScriptCalls, IReadOnlyList<string> Unresolved);

public sealed record NativeCallCount(string Native, int Static, int Method)
{
    public int Total => Static + Method;
}

/// <summary>
/// The static native call-site census over the effective script set.
/// </summary>
/// <remarks>
/// The census resolves script and function names with the same <see cref="ScriptLibrary"/> rules the
/// runtime uses, so a call site the census counts is one the VM would resolve and one it refuses is
/// one the VM would refuse too. Scripts are reported in four separate categories, because a payload
/// that could not be decoded, a name with no payload, a name with several candidates and a name the
/// library holds are different facts and must not be collapsed into one "missing" count.
///
/// `CallSites` counts the sites in scripts the library resolved, which are the sites the runtime
/// would reach. A site inside a script that does not resolve is reported in `Unresolved` instead, so
/// the two numbers are never mixed.
///
/// <para>A method site is attributed through <see cref="MethodCallContract"/>: it is counted under the
/// receiver's declared type when the payload carries one, and reported in `Unresolved` when it does
/// not, so a call is never attributed to a script the data does not name.</para>
/// </remarks>
public sealed record CensusReport(
    int Scripts,
    int Loaded,
    int Unreadable,
    int NotLoadable,
    int Ambiguous,
    int CallSites,
    IReadOnlyList<NativeCallCount> Natives,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<string> UnreadableScripts,
    IReadOnlyList<string> NotLoadableScripts,
    IReadOnlyList<string> AmbiguousScripts)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"scripts={Scripts} loaded={Loaded} unreadable={Unreadable} not-loadable={NotLoadable} ambiguous={Ambiguous} call-sites={CallSites}\n");
        text.Append(CultureInfo.InvariantCulture, $"natives={Natives.Count}\n");
        foreach (var native in Natives)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  native {native.Native} static={native.Static} method={native.Method} total={native.Total}\n");
        }
        AppendList(text, "unresolved", Unresolved);
        AppendList(text, "unreadable", UnreadableScripts);
        AppendList(text, "not-loadable", NotLoadableScripts);
        AppendList(text, "ambiguous", AmbiguousScripts);
        return text.ToString();
    }

    private static void AppendList(StringBuilder text, string label, IReadOnlyList<string> entries)
    {
        text.Append(CultureInfo.InvariantCulture, $"{label}={entries.Count}\n");
        foreach (var entry in entries)
        {
            text.Append(CultureInfo.InvariantCulture, $"  {label} {entry}\n");
        }
    }
}

public static class NativeCallSiteCensus
{
    public static CollectedCallSites Collect(PapyrusScript script, ScriptLibrary library)
    {
        var sites = new List<NativeCallSite>();
        var unresolved = new List<string>();
        var scriptCalls = 0;
        foreach (var function in script.Functions.OrderBy(function => function.State, StringComparer.Ordinal)
                     .ThenBy(function => function.Name, StringComparer.Ordinal))
        {
            for (var index = 0; index < function.Instructions.Count; index++)
            {
                var instruction = function.Instructions[index];
                if (instruction.Opcode is not (PapyrusOpcode.CallStatic or PapyrusOpcode.CallMethod or PapyrusOpcode.CallParent)) continue;
                var target = instruction.Opcode == PapyrusOpcode.CallParent ? script.Parent : instruction.Script;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                PapyrusFunction? declared = null;
                PapyrusFunction? nativeDeclaration = null;
                var current = target;
                while (!string.IsNullOrWhiteSpace(current) && library.TryGet(current, out var owner))
                {
                    if (!visited.Add(current))
                    {
                        declared = nativeDeclaration = null;
                        break;
                    }
                    declared = owner.FindFunction(instruction.Name ?? "");
                    if (instruction.Opcode != PapyrusOpcode.CallMethod || declared is { IsNative: false }) break;
                    nativeDeclaration ??= declared;
                    current = owner.Parent;
                }
                declared ??= nativeDeclaration;
                if (declared is null)
                {
                    unresolved.Add($"{script.Name}[{function.State ?? "<property>"}].{function.Name}+{index}: " +
                                   $"no resolved declaration for {target ?? "<unknown>"}.{instruction.Name ?? "<unknown>"}");
                    continue;
                }
                if (!declared.IsNative)
                {
                    scriptCalls++;
                    continue;
                }
                var isStatic = instruction.Opcode == PapyrusOpcode.CallStatic;
                sites.Add(new NativeCallSite(script.Name, function.Name, index,
                    isStatic ? NativeTargetKind.Static : NativeTargetKind.ScriptInstance,
                    target!, instruction.Name));
            }
        }
        return new CollectedCallSites(new CensusScript(script.Name, ScriptSetState.Loaded, sites), scriptCalls, unresolved);
    }

    /// <summary>
    /// Counts the call sites of an effective script set. The library decides whether a script and a
    /// function resolve, exactly as the runtime would.
    /// </summary>
    public static CensusReport Run(IEnumerable<CensusScript> scripts, ScriptLibrary library)
    {
        var ordered = scripts.OrderBy(script => script.Name, StringComparer.Ordinal).ToList();
        var natives = new SortedDictionary<string, (int Static, int Method)>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var unreadable = new List<string>();
        var notLoadable = new List<string>();
        var ambiguous = new List<string>();
        var loaded = 0;
        var callSites = 0;

        foreach (var script in ordered)
        {
            switch (script.State)
            {
                case ScriptSetState.Unreadable:
                    unreadable.Add(script.Name);
                    continue;
                case ScriptSetState.NotLoadable:
                    notLoadable.Add(script.Name);
                    continue;
                case ScriptSetState.Ambiguous:
                    ambiguous.Add(script.Name);
                    continue;
            }

            if (!library.TryGet(script.Name, out var resolved))
            {
                unresolved.Add($"{script.Name} is not in the library");
                continue;
            }
            loaded++;

            foreach (var site in script.Sites.OrderBy(site => site.Instruction).ThenBy(site => site.Target, StringComparer.Ordinal))
            {
                callSites++;
                if (string.IsNullOrWhiteSpace(site.Target))
                {
                    unresolved.Add($"{script.Name}.{site.Function}+{site.Instruction} has an empty target");
                    continue;
                }
                if (!resolved.Functions.Any(function => function.Name.Equals(site.Function, StringComparison.OrdinalIgnoreCase)))
                {
                    unresolved.Add($"{script.Name}.{site.Function}+{site.Instruction} is in a function the script does not define");
                    continue;
                }

                if (site.Kind != NativeTargetKind.Static && site.CalledFunction is not null)
                {
                    var method = MethodCallContract.Resolve(site.CalledFunction, site.Target);
                    if (!method.Resolved)
                    {
                        unresolved.Add($"{script.Name}.{site.Function}+{site.Instruction} {method.Reason}");
                        continue;
                    }
                    var methodCount = natives.TryGetValue(method.ReceiverType!, out var known)
                        ? known
                        : (Static: 0, Method: 0);
                    natives[method.ReceiverType!] = (methodCount.Static, methodCount.Method + 1);
                    continue;
                }

                var current = natives.TryGetValue(site.Target, out var count) ? count : (Static: 0, Method: 0);
                natives[site.Target] = site.Kind == NativeTargetKind.Static
                    ? (current.Static + 1, current.Method)
                    : (current.Static, current.Method + 1);
            }
        }

        return new CensusReport(
            ordered.Count,
            loaded,
            unreadable.Count,
            notLoadable.Count,
            ambiguous.Count,
            callSites,
            natives.Select(entry => new NativeCallCount(entry.Key, entry.Value.Static, entry.Value.Method)).ToList(),
            unresolved,
            unreadable,
            notLoadable,
            ambiguous);
    }
}
