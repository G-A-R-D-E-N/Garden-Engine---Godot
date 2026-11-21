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
        bool Is(params GameplayNativeFamily[] families) => families.Any(family => Name(family.Native).Equals(native, StringComparison.OrdinalIgnoreCase));
        if (Is(CombatNativeAdapters.IsDeadFamily)) return "bound_gated";
        if (Is(PerkNativeAdapters.AddFamily, FactionNativeAdapters.InFamily)) return "player_only";
        if (Is(CoreNativeAdapters.AbsFamily, CoreNativeAdapters.RandomFamily, CoreNativeAdapters.TimeFamily, CoreNativeAdapters.PlayerFamily,
                CoreNativeAdapters.WarningFamily, CoreNativeAdapters.ErrorFamily, CoreNativeAdapters.StackFamily,
                CrimeFactionNativeAdapters.GetFamily, InventoryNativeAdapters.AddFamily, ReferenceLinkNativeAdapters.GetFamily,
                ReferenceTimerNativeAdapters.StartFamily, ReferenceTimerNativeAdapters.CancelFamily)) return "bound";
        if (new[] { NativeSet.DebugTrace }.Select(Name).Contains(native, StringComparer.OrdinalIgnoreCase)) return "bound";
        if (UnsupportedNativeAdapters.Table.Any(entry => $"{entry.Script}.{entry.Function}".Equals(native, StringComparison.OrdinalIgnoreCase))) return "unsupported";
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
    public void ActorIsDeadIsTheOnlyGatedNativeAndGetCrimeFactionIsBound()
    {
        var natives = Load().GetProperty("natives").EnumerateArray()
            .Select(entry => (Native: entry.GetProperty("native").GetString()!, State: entry.GetProperty("state").GetString()!)).ToArray();

        Assert.Equal(new[] { "Actor.IsDead" }, natives.Where(entry => entry.State == "bound_gated").Select(entry => entry.Native));
        Assert.Contains(natives, entry => entry.Native == "Actor.GetCrimeFaction" && entry.State == "bound");
        Assert.Contains(natives, entry => entry.Native == "Actor.AddSpell" && entry.State == "unsupported");
    }
}
