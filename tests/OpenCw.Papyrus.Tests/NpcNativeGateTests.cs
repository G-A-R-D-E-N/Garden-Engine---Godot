using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class NpcNativeGateTests
{
    private static JsonElement Load()
    {
        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "docs", "proof", "npc-native-gate.json"))).RootElement;
    }

    private static string Name(NativeId id) => $"{id.Script}.{id.Function}";

    private static string Classify(string native)
    {
        if (Name(CombatNativeAdapters.IsDeadFamily.Native).Equals(native, StringComparison.OrdinalIgnoreCase)) return "bound_gated";
        var bound = new[] { NativeSet.DebugTrace, NativeSet.DebugNotification, NativeSet.DebugMessageBox }
            .Select(Name)
            .Concat(Enum.GetNames<QuestNative>().Select(name => "Quest." + name));
        if (bound.Contains(native, StringComparer.OrdinalIgnoreCase)) return "bound";
        return UiServices.Natives.Contains(native, StringComparer.OrdinalIgnoreCase) ? "ui_bound" : "unbound";
    }

    [Fact]
    public void EveryRecordedNativeStateMatchesWhatProductionBinds()
    {
        var natives = Load().GetProperty("natives").EnumerateArray().ToArray();

        Assert.Equal(56, natives.Length);
        Assert.All(natives, entry =>
            Assert.Equal(Classify(entry.GetProperty("native").GetString()!), entry.GetProperty("state").GetString()));
    }

    [Fact]
    public void EveryNativeAScriptCallsIsListedWithThatScript()
    {
        var gate = Load();
        var listed = gate.GetProperty("natives").EnumerateArray().ToDictionary(
            entry => entry.GetProperty("native").GetString()!,
            entry => entry.GetProperty("scripts").EnumerateArray().Select(script => script.GetString()!).ToHashSet());

        foreach (var script in gate.GetProperty("scripts").EnumerateArray())
        {
            var name = script.GetProperty("name").GetString()!;
            foreach (var native in script.GetProperty("natives").EnumerateArray().Select(entry => entry.GetString()!))
                Assert.Contains(name, listed[native]);
        }
        Assert.Equal(listed.Values.SelectMany(scripts => scripts).Distinct().Order(),
            gate.GetProperty("scripts").EnumerateArray().Select(script => script.GetProperty("name").GetString()!).Order());
    }

    [Fact]
    public void EveryScriptAttachedToAnNpcIsAnalysed()
    {
        var gate = Load();
        var analysed = gate.GetProperty("scripts").EnumerateArray().Select(script => script.GetProperty("name").GetString()!).ToHashSet();

        Assert.All(gate.GetProperty("npcs").EnumerateArray().SelectMany(npc => npc.GetProperty("scripts").EnumerateArray()),
            script => Assert.Contains(script.GetString()!, analysed));
    }


    [Fact]
    public void ActorIsDeadIsTheOnlyActorNativeBoundAndTheRestAreListedUnbound()
    {
        var natives = Load().GetProperty("natives").EnumerateArray()
            .Select(entry => (Native: entry.GetProperty("native").GetString()!, State: entry.GetProperty("state").GetString()!)).ToArray();

        Assert.Equal(new[] { "Actor.IsDead" }, natives.Where(entry => entry.Native.StartsWith("Actor.", StringComparison.Ordinal)
            && entry.State == "bound_gated").Select(entry => entry.Native));
        Assert.DoesNotContain(natives, entry => entry.Native.StartsWith("Actor.", StringComparison.Ordinal) && entry.State == "bound");
        Assert.Contains(natives, entry => entry.Native == "Actor.GetCrimeFaction" && entry.State == "unbound");
    }

}
