using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class InventoryNativeAdaptersTests : HostFixture
{
    private const string Chest = "000A00:Base.esm";
    private const string Crate = "000A01:Base.esm";
    private const string Caps = "00000F:Base.esm";
    private const string Ammo = "000B02:Base.esm";

    private sealed class Inventories : IReferenceInventoryAccess
    {
        public Dictionary<string, Dictionary<string, int>> Held { get; init; } = new();
        public List<string> Log { get; } = new();

        public bool TryAdd(string refKey, string itemKey, int count)
        {
            Log.Add($"add {refKey} {itemKey} {count}");
            if (!Held.TryGetValue(refKey, out var items)) return false;
            items[itemKey] = items.GetValueOrDefault(itemKey) + count;
            return true;
        }

        public int Count(string refKey, string? itemKey) =>
            !Held.TryGetValue(refKey, out var items) ? 0 : itemKey is null ? items.Values.Sum() : items.GetValueOrDefault(itemKey);

        public int Move(string refKey, string? toRefKey, string? itemKey, int count)
        {
            Log.Add($"move {refKey} -> {toRefKey ?? "none"} {itemKey ?? "all"} {count}");
            if (!Held.TryGetValue(refKey, out var source) || (toRefKey is not null && !Held.ContainsKey(toRefKey))) return 0;
            var moved = 0;
            foreach (var (item, held) in source.ToArray().Where(pair => itemKey is null || pair.Key == itemKey))
            {
                var take = itemKey is null ? held : Math.Min(count, held);
                moved += take;
                if (toRefKey is not null) Held[toRefKey][item] = Held[toRefKey].GetValueOrDefault(item) + take;
                if (take == held) source.Remove(item);
                else source[item] = held - take;
            }
            return moved;
        }
    }

    private static PapyrusValue Bare(string key) => PapyrusValue.FromObject(new EngineObjectReference(key));

    private static PapyrusValue Unreadable => PapyrusValue.FromObject(new EngineObjectReference("no-colon"));

    private IScriptHost Host(HostKind kind, EvidenceGate gate, PapyrusValue receiver, PapyrusValue item, PapyrusValue other, string body, List<long> reported)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("Form"),
            TestHosts.Stub("ObjectReference", null,
                "Function AddItem(Form akItemToAdd, int aiCount = 1, bool abSilent = false) native",
                "int Function GetItemCount(Form akItem = None) native",
                "Function RemoveAllItems(ObjectReference akTransferTo = None, bool abKeepOwnership = false) native",
                "Function RemoveItem(Form akItemToRemove, int aiCount = 1, bool abSilent = false, ObjectReference akOtherContainer = None) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Target() global native",
                "ObjectReference Function Other() global native",
                "Form Function Item() global native",
                "Function Report(int value) global native"),
            new ScriptSpec("Caller",
                "Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Target()\n  ObjectReference other = Probe.Other()\n  Form item = Probe.Item()\n" +
                $"  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Target"), _ => receiver);
        host.RegisterNative(new NativeId("Probe", "Other"), _ => other);
        host.RegisterNative(new NativeId("Probe", "Item"), _ => item);
        host.RegisterNative(new NativeId("Probe", "Report"), arguments =>
        {
            reported.Add(arguments[0].Integer);
            return PapyrusValue.None;
        });
        return host;
    }

    private static VmResult Run(IScriptHost host)
    {
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);
        return host.RunEvent(instance, "Run");
    }

    private (List<long> Reported, InventoryNativeAdapters Adapters, AdapterReport Report) Play(
        HostKind kind, Inventories access, PapyrusValue receiver, PapyrusValue item, PapyrusValue other, string body)
    {
        var reported = new List<long>();
        var gate = TestHosts.Gate((InventoryNativeAdapters.SystemKey, "APPROXIMATED"));
        var host = Host(kind, gate, receiver, item, other, body, reported);
        var adapters = new InventoryNativeAdapters(host, gate, access);
        var report = adapters.Bind();
        var result = Run(host);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        return (reported, adapters, report);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AddItemThenGetItemCountReadTheSameInventoryWithTheDefaultCountOfOne(HostKind kind)
    {
        var access = new Inventories { Held = { [Chest] = new() { [Caps] = 2 } } };

        var run = Play(kind, access, Bare(Chest), Bare(Ammo), Bare(Crate),
            "target.AddItem(item, 3)\n  target.AddItem(item)\n  Probe.Report(target.GetItemCount(item))\n  Probe.Report(target.GetItemCount())");

        Assert.Equal(4, run.Report.Bound);
        Assert.Equal(new long[] { 4, 6 }, run.Reported.ToArray());
        Assert.Equal(0, run.Adapters.Unapplied);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RemoveItemTakesAPartialCountAndAnOverCountTakesOnlyWhatIsHeld(HostKind kind)
    {
        var access = new Inventories { Held = { [Chest] = new() { [Ammo] = 5, [Caps] = 9 } } };

        var run = Play(kind, access, Bare(Chest), Bare(Ammo), PapyrusValue.None,
            "target.RemoveItem(item, 2)\n  Probe.Report(target.GetItemCount(item))\n  target.RemoveItem(item, 10)\n  Probe.Report(target.GetItemCount(item))\n  Probe.Report(target.GetItemCount())");

        Assert.Equal(new long[] { 3, 0, 9 }, run.Reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RemoveItemAndRemoveAllItemsMoveIntoTheNamedContainerAndAnUnreadableDestinationMovesNothing(HostKind kind)
    {
        var moved = Play(kind, new Inventories { Held = { [Chest] = new() { [Ammo] = 5, [Caps] = 9 }, [Crate] = new() } }, Bare(Chest), Bare(Ammo), Bare(Crate),
            "target.RemoveItem(item, 2, False, other)\n  Probe.Report(other.GetItemCount(item))\n  target.RemoveAllItems(other)\n  Probe.Report(other.GetItemCount())\n  Probe.Report(target.GetItemCount())");
        var refused = Play(kind, new Inventories { Held = { [Chest] = new() { [Ammo] = 5 } } }, Bare(Chest), Bare(Ammo), Unreadable,
            "target.RemoveItem(item, 2, False, other)\n  target.RemoveAllItems(other)\n  Probe.Report(target.GetItemCount())");

        Assert.Equal(new long[] { 2, 14, 0 }, moved.Reported.ToArray());
        Assert.Equal(new long[] { 5 }, refused.Reported.ToArray());
        Assert.Equal(2, refused.Adapters.Unapplied);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void RemoveAllItemsWithNoDestinationEmptiesTheInventory(HostKind kind)
    {
        var access = new Inventories { Held = { [Chest] = new() { [Ammo] = 5, [Caps] = 9 } } };

        var run = Play(kind, access, Bare(Chest), Bare(Ammo), PapyrusValue.None, "target.RemoveAllItems()\n  Probe.Report(target.GetItemCount())");

        Assert.Equal(new long[] { 0 }, run.Reported.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnreadableItemFormOrNoneChangesNothingAndCountsZero(HostKind kind)
    {
        var access = new Inventories { Held = { [Chest] = new() { [Ammo] = 5 } } };

        var run = Play(kind, access, Bare(Chest), Unreadable, PapyrusValue.None,
            "target.AddItem(item, 3)\n  target.RemoveItem(item, 1)\n  target.AddItem(None)\n  Probe.Report(target.GetItemCount(item))\n  Probe.Report(target.GetItemCount())");

        Assert.Equal(new long[] { 0, 5 }, run.Reported.ToArray());
        Assert.Empty(access.Log);
        Assert.Equal(3, run.Adapters.Unapplied);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AReceiverWithNoInventoryOrNoReferenceKeyIsLeftAloneAndCountsZero(HostKind kind)
    {
        var access = new Inventories { Held = { [Chest] = new() { [Ammo] = 5 } } };

        var unopened = Play(kind, access, Bare(Crate), Bare(Ammo), PapyrusValue.None,
            "target.AddItem(item, 3)\n  target.RemoveItem(item, 1)\n  Probe.Report(target.GetItemCount(item))");
        var anonymous = Play(kind, access, Unreadable, Bare(Ammo), PapyrusValue.None,
            "target.AddItem(item, 3)\n  Probe.Report(target.GetItemCount(item))");

        Assert.Equal(new long[] { 0 }, unopened.Reported.ToArray());
        Assert.Equal(new long[] { 0 }, anonymous.Reported.ToArray());
        Assert.Equal(5, access.Held[Chest][Ammo]);
        Assert.Equal(2, unopened.Adapters.Unapplied);
        Assert.Equal(1, anonymous.Adapters.Unapplied);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsAndTheRecordedRegistryApproximatesIt(HostKind kind)
    {
        var unproven = TestHosts.Gate((InventoryNativeAdapters.SystemKey, "UNPROVEN"));
        var report = new InventoryNativeAdapters(Host(kind, unproven, Bare(Chest), Bare(Ammo), PapyrusValue.None, "", []), unproven, new Inventories()).Bind();
        Assert.Equal(4, report.Bound);

        var root = TestRepository.FindRoot();
        Assert.NotNull(root);
        var registry = EvidenceRegistry.Load(File.ReadAllText(Path.Combine(root!, "docs", "proof", "papyrus-evidence-registry.json")));
        Assert.Equal(EvidenceStatus.Approximated, registry.StatusFor(InventoryNativeAdapters.SystemKey));
        Assert.True(new EvidenceGate(registry).Gate(InventoryNativeAdapters.SystemKey).Allowed);
    }
}
