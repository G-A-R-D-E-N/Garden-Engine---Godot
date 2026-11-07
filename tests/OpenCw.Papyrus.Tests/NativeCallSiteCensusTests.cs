using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Papyrus.Census;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class NativeCallSiteCensusTests
{
    private static ScriptLibrary Library(params PapyrusScript[] scripts)
    {
        var library = new ScriptLibrary();
        foreach (var script in scripts)
        {
            library.Add(script);
        }
        return library;
    }

    private static PapyrusScript Script(string name, params string[] functions) =>
        new(name, functions.Select(function => new PapyrusFunction(function, Array.Empty<PapyrusInstruction>())).ToList(),
            Array.Empty<string>());

    private static NativeCallSite Site(string script, string function, int instruction, string target,
        NativeTargetKind kind = NativeTargetKind.Static) =>
        new(script, function, instruction, kind, target);

    private static IEnumerable<CensusScript[]> Permutations(IReadOnlyList<CensusScript> scripts)
    {
        if (scripts.Count == 0)
        {
            yield return Array.Empty<CensusScript>();
            yield break;
        }
        for (var index = 0; index < scripts.Count; ++index)
        {
            var rest = scripts.Where((_, position) => position != index).ToList();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { scripts[index] }.Concat(tail).ToArray();
            }
        }
    }

    [Fact]
    public void SyntheticResolutionMatchesTheLibraryRules()
    {
        var library = Library(Script("QuestScript", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("questscript", ScriptSetState.Loaded,
                new[] { Site("questscript", "oninit", 4, "Utility") }),
        }, library);

        Assert.Equal(1, report.Loaded);
        Assert.Equal(1, report.CallSites);
        Assert.Empty(report.Unresolved);
        Assert.Equal("Utility", Assert.Single(report.Natives).Native);
    }

    [Theory]
    [InlineData("Combat", "Work")]
    [InlineData(null, "Value.get")]
    public void DeclaredCallersOutsideTheEmptyStateAreCounted(string? state, string function)
    {
        var script = new PapyrusScript("QuestScript",
            new[] { new PapyrusFunction(function, Array.Empty<PapyrusInstruction>(), State: state) }, Array.Empty<string>());
        Assert.Null(script.FindFunction(function));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("QuestScript", ScriptSetState.Loaded,
                new[] { Site("QuestScript", function.ToLowerInvariant(), 0, "Utility") }),
        }, Library(script));

        Assert.Equal(1, report.CallSites);
        Assert.Empty(report.Unresolved);
        Assert.Equal(1, Assert.Single(report.Natives).Total);
    }

    [Fact]
    public void UnresolvedSitesAreListedSeparately()
    {
        var library = Library(Script("QuestScript", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("Missing", ScriptSetState.Loaded, new[] { Site("Missing", "OnInit", 0, "Utility") }),
            new CensusScript("QuestScript", ScriptSetState.Loaded, new[]
            {
                Site("QuestScript", "OnInit", 1, "   "),
                Site("QuestScript", "NotAFunction", 2, "Debug"),
            }),
        }, library);

        Assert.Equal(1, report.Loaded);
        Assert.Equal(3, report.Unresolved.Count);
        Assert.Empty(report.Natives);
        Assert.Equal(2, report.CallSites);
        Assert.Contains(report.Unresolved, entry => entry.Contains("Missing is not in the library", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFourCategoriesAreSeparate()
    {
        var library = Library(Script("Loaded", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("Loaded", ScriptSetState.Loaded, new[] { Site("Loaded", "OnInit", 0, "Utility") }),
            new CensusScript("Broken", ScriptSetState.Unreadable, Array.Empty<NativeCallSite>()),
            new CensusScript("Absent", ScriptSetState.NotLoadable, Array.Empty<NativeCallSite>()),
            new CensusScript("Twice", ScriptSetState.Ambiguous, Array.Empty<NativeCallSite>()),
        }, library);

        Assert.Equal(4, report.Scripts);
        Assert.Equal(1, report.Loaded);
        Assert.Equal(1, report.Unreadable);
        Assert.Equal(1, report.NotLoadable);
        Assert.Equal(1, report.Ambiguous);
        Assert.Equal(new[] { "Broken" }, report.UnreadableScripts);
        Assert.Equal(new[] { "Absent" }, report.NotLoadableScripts);
        Assert.Equal(new[] { "Twice" }, report.AmbiguousScripts);

        var text = report.ToReport();
        Assert.Contains("unreadable=1", text, StringComparison.Ordinal);
        Assert.Contains("not-loadable=1", text, StringComparison.Ordinal);
        Assert.Contains("ambiguous=1", text, StringComparison.Ordinal);
        Assert.Contains("unresolved=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsAggregatePerNativeAndOrderByName()
    {
        var library = Library(Script("A", "OnInit"), Script("B", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("B", ScriptSetState.Loaded, new[]
            {
                Site("B", "OnInit", 0, "Utility"),
                Site("B", "OnInit", 1, "Utility", NativeTargetKind.ScriptInstance),
            }),
            new CensusScript("A", ScriptSetState.Loaded, new[]
            {
                Site("A", "OnInit", 0, "Utility"),
                Site("A", "OnInit", 1, "Debug"),
            }),
        }, library);

        Assert.Equal(new[] { "Debug", "Utility" }, report.Natives.Select(native => native.Native).ToArray());
        var utility = report.Natives[1];
        Assert.Equal(2, utility.Static);
        Assert.Equal(1, utility.Method);
        Assert.Equal(3, utility.Total);
        Assert.Equal(4, report.CallSites);
    }

    [Fact]
    public void TheReportIsDeterministicUnderAnyInputOrder()
    {
        var library = Library(Script("A", "OnInit"), Script("B", "OnInit"), Script("C", "OnInit"));
        var scripts = new List<CensusScript>
        {
            new("C", ScriptSetState.Loaded, new[] { Site("C", "OnInit", 0, "Utility") }),
            new("A", ScriptSetState.Loaded, new[] { Site("A", "OnInit", 0, "Debug"), Site("A", "OnInit", 1, "Utility") }),
            new("B", ScriptSetState.NotLoadable, Array.Empty<NativeCallSite>()),
            new("D", ScriptSetState.Unreadable, Array.Empty<NativeCallSite>()),
        };

        var expected = NativeCallSiteCensus.Run(scripts, library).ToReport();
        foreach (var permutation in Permutations(scripts))
        {
            Assert.Equal(expected, NativeCallSiteCensus.Run(permutation, library).ToReport());
        }
    }

    [Fact]
    public void EmptyInputIsAnEmptyReport()
    {
        var report = NativeCallSiteCensus.Run(Array.Empty<CensusScript>(), Library());

        Assert.Equal(0, report.Scripts);
        Assert.Equal(0, report.CallSites);
        Assert.Empty(report.Natives);
        Assert.Contains("natives=0", report.ToReport(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodSiteWithAKnownReceiverTypeIsAttributedToThatType()
    {
        var library = Library(Script("QuestScript", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("QuestScript", ScriptSetState.Loaded, new[]
            {
                new NativeCallSite("QuestScript", "OnInit", 3, NativeTargetKind.ScriptInstance, "Actor", "SetValue"),
            }),
        }, library);

        Assert.Empty(report.Unresolved);
        var native = Assert.Single(report.Natives);
        Assert.Equal("Actor", native.Native);
        Assert.Equal(0, native.Static);
        Assert.Equal(1, native.Method);
    }

    [Fact]
    public void AMethodSiteWithAnUnknownReceiverTypeIsCountedNotGuessed()
    {
        var library = Library(Script("QuestScript", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("QuestScript", ScriptSetState.Loaded, new[]
            {
                new NativeCallSite("QuestScript", "OnInit", 3, NativeTargetKind.ScriptInstance, "None", "SetValue"),
                new NativeCallSite("QuestScript", "OnInit", 4, NativeTargetKind.ScriptInstance, "none", "GetValue"),
            }),
        }, library);

        Assert.Equal(2, report.CallSites);
        Assert.Empty(report.Natives);
        Assert.Equal(2, report.Unresolved.Count);
        Assert.All(report.Unresolved, entry => Assert.Contains("not attributed", entry, StringComparison.Ordinal));
    }

    [Fact]
    public void AMethodSiteWithNoFunctionNameIsCountedNotGuessed()
    {
        var library = Library(Script("QuestScript", "OnInit"));
        var report = NativeCallSiteCensus.Run(new[]
        {
            new CensusScript("QuestScript", ScriptSetState.Loaded, new[]
            {
                new NativeCallSite("QuestScript", "OnInit", 3, NativeTargetKind.ScriptInstance, "Actor", ""),
            }),
        }, library);

        Assert.Empty(report.Natives);
        Assert.Contains(report.Unresolved, entry => entry.Contains("names no function", StringComparison.Ordinal));
    }
}
