using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;
using Interaction = OpenCommonwealth.Services.Interaction;

namespace OpenCw.Papyrus.Tests;

public sealed class ActivationEndToEndTests : HostFixture
{
    private static readonly PapyrusOwnerId Door = PapyrusOwnerId.ForPlacedReference("Base:Probe.esp", "Door:Probe.esp");
    private static readonly PapyrusOwnerId Activator = PapyrusOwnerId.ForPlacedReference("Actor:Probe.esp", "Player:Probe.esp");
    private static readonly IReadOnlySet<string> NoGroups = new HashSet<string>();
    private const string Metadata = "[{\"name\":\"DoorProbe\",\"properties\":[]}]";

    private static readonly string[] Keys =
    {
        "vm.session.cell_lifecycle",
        "vm.session.activation_routing",
        "vmad.reference_compiled_attachments",
        "vmad.reference_source",
        "owner.engine_identity",
        "event.OnActivate",
        ActivationNativeAdapters.SystemKey,
    };

    private sealed class Wiring
    {
        public Interaction.ActivationBlocks Blocks { get; } = new();

        public List<string> Order { get; } = new();

        public Interaction.ActivationDispatcher Dispatcher { get; set; } = null!;
    }

    private Wiring Wire(HostKind kind, string body)
    {
        var gate = TestHosts.Gate(Keys.Select(key => (key, "ENGINE_POLICY")).ToArray());
        var host = Track(TestHosts.Create(kind, gate,
            new ScriptSpec("Form", "Scriptname Form\n"),
            new ScriptSpec("ObjectReference",
                "Scriptname ObjectReference\n" +
                "Function BlockActivation(bool abBlocked = true, bool abHideActivateText = false) native\n" +
                "bool Function IsActivationBlocked() native\n"),
            new ScriptSpec("DoorProbe",
                "Scriptname DoorProbe extends ObjectReference\n" +
                "Function OnActivate(ObjectReference akActionRef)\n" +
                $"  {body}\n" +
                "EndFunction\n")));
        var session = new GameSession(host, gate);
        var wiring = new Wiring();
        Assert.Equal(2, new ActivationNativeAdapters(session.Host, gate, wiring.Blocks.Set, wiring.Blocks.IsBlocked).Bind().Bound);
        Assert.Null(session.AttachReference("Cell", Door, Metadata).Refusal);

        var dispatcher = new Interaction.ActivationDispatcher(
            _ =>
            {
                wiring.Order.Add("script");
                return session.Activate(Door, Activator).Delivered > 0;
            },
            wiring.Blocks.IsBlocked);
        dispatcher.Register(new Interaction.DelegateActivationHandler(
            _ => true,
            _ =>
            {
                wiring.Order.Add("handler");
                return new Interaction.ActivationResult(Interaction.ActivationStatus.Handled);
            }));
        wiring.Dispatcher = dispatcher;
        return wiring;
    }

    private static Interaction.ActivationTarget DoorTarget() =>
        new(Door.Discriminator, "Base:Probe.esp", "Door", NoGroups);

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnOnActivateScriptThatBlocksStopsTheDefaultHandlerButRunsFirst(HostKind kind)
    {
        var wiring = Wire(kind, "BlockActivation(True, True)");

        var result = wiring.Dispatcher.Dispatch(DoorTarget());

        Assert.Equal(Interaction.ActivationStatus.Blocked, result.Status);
        Assert.Equal(new[] { "script" }, wiring.Order.ToArray());
        Assert.Equal(1, wiring.Dispatcher.ScriptEventsDelivered);
        Assert.True(wiring.Blocks.IsBlocked(Door.Discriminator));
        Assert.True(wiring.Blocks.HidesText(Door.Discriminator));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void BlockingWithoutHidingTheTextLeavesThePromptVisible(HostKind kind)
    {
        var wiring = Wire(kind, "BlockActivation()");

        var result = wiring.Dispatcher.Dispatch(DoorTarget());

        Assert.Equal(Interaction.ActivationStatus.Blocked, result.Status);
        Assert.True(wiring.Blocks.IsBlocked(Door.Discriminator));
        Assert.False(wiring.Blocks.HidesText(Door.Discriminator));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnOnActivateScriptThatDoesNotBlockRunsBeforeTheFirstHandler(HostKind kind)
    {
        var wiring = Wire(kind, "Int unused = 0");

        var result = wiring.Dispatcher.Dispatch(DoorTarget());

        Assert.Equal(Interaction.ActivationStatus.Handled, result.Status);
        Assert.Equal(new[] { "script", "handler" }, wiring.Order.ToArray());
        Assert.Equal(1, wiring.Dispatcher.ScriptEventsDelivered);
        Assert.False(wiring.Blocks.IsBlocked(Door.Discriminator));
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheBlockIsReadAfterTheScriptEventSoTheScriptCanLiftAnEarlierBlock(HostKind kind)
    {
        var wiring = Wire(kind, "BlockActivation(False)");
        wiring.Blocks.Set(Door.Discriminator, true, false);

        var result = wiring.Dispatcher.Dispatch(DoorTarget());

        Assert.Equal(Interaction.ActivationStatus.Handled, result.Status);
        Assert.Equal(new[] { "script", "handler" }, wiring.Order.ToArray());
        Assert.False(wiring.Blocks.IsBlocked(Door.Discriminator));
    }
}
