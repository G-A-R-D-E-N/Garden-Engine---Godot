using System.Numerics;
using OpenCommonwealth.Services.Papyrus.Events;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Natives;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;
using Radicle.Interop;
using Vector3 = System.Numerics.Vector3;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public sealed class ReferenceAnimationNativeAdaptersTests : HostFixture
{
    private static readonly PapyrusOwnerId Lever = PapyrusOwnerId.ForPlacedReference("000800:Base.esm", "000A00");

    private IScriptHost Host(HostKind kind, EvidenceGate gate, string body, List<bool> answers)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ObjectReference", null,
                "bool Function PlayAnimation(string asAnimation) native",
                "bool Function PlayAnimationAndWait(string asAnimation, string asEventName) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Self() global native",
                "Function Answer(bool value) global native"),
            new ScriptSpec("Caller", $"Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Self()\n  {body}\nEndFunction\n")));
        host.RegisterNative(new NativeId("Probe", "Self"), _ => PapyrusValue.FromObject(new EngineObjectReference(Lever)));
        host.RegisterNative(new NativeId("Probe", "Answer"), arguments =>
        {
            answers.Add(arguments[0].Flag);
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

    private static EvidenceGate Gate(string status = "APPROXIMATED") => TestHosts.Gate((ReferenceAnimationNativeAdapters.SystemKey, status));

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlayAnimationRecordsTheRequestAndAnswersFalseBecauseNothingPlayed(HostKind kind)
    {
        var answers = new List<bool>();
        var gate = Gate();
        var host = Host(kind, gate, "Probe.Answer(target.PlayAnimation(\"Open\"))", answers);
        var adapters = new ReferenceAnimationNativeAdapters(host, gate);
        var report = adapters.Bind();

        var result = Run(host);

        Assert.Equal(2, report.Bound);
        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(new[] { false }, answers.ToArray());
        Assert.Equal(new[] { new AnimationRequest("000A00", "Open", null) }, adapters.Requests.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlayAnimationAndWaitRecordsBothNamesAndNeverWaitsWhenNoClipIsKnown(HostKind kind)
    {
        var answers = new List<bool>();
        var gate = Gate();
        var host = Host(kind, gate, "Probe.Answer(target.PlayAnimationAndWait(\"Open\", \"Done\"))", answers);
        var adapters = new ReferenceAnimationNativeAdapters(host, gate);
        adapters.Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(new[] { false }, answers.ToArray());
        Assert.Equal(new[] { new AnimationRequest("000A00", "Open", "Done") }, adapters.Requests.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void TheRecordKeepsTheLastRequestsAndCountsEveryOne(HostKind kind)
    {
        var gate = Gate();
        var host = Host(kind, gate,
            "int index = 0\n  While index < 70\n    target.PlayAnimation(\"Open\")\n    index = index + 1\n  EndWhile\n  target.PlayAnimation(\"Last\")",
            new List<bool>());
        var adapters = new ReferenceAnimationNativeAdapters(host, gate);
        adapters.Bind();

        var result = Run(host);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(71, adapters.RequestCount);
        Assert.Equal(ReferenceAnimationNativeAdapters.Capacity, adapters.Requests.Count);
        Assert.Equal("Last", adapters.Requests[^1].Animation);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnUnprovenGateStillBindsBothNatives(HostKind kind)
    {
        var gate = Gate("UNPROVEN");
        var host = Host(kind, gate, "target.PlayAnimation(\"Open\")", new List<bool>());

        var report = new ReferenceAnimationNativeAdapters(host, gate).Bind();

        Assert.Equal(2, report.Bound);
        Assert.All(report.Natives, outcome => Assert.Equal(AdapterState.Bound, outcome.State));
    }

    private sealed class Clips : IAnimationClips
    {
        public Dictionary<(string, string), AnimationClip> Known { get; } = new();

        public AnimationClip? Find(string refKey, string animation) => Known.GetValueOrDefault((refKey, animation));
    }

    private sealed class Sink : IAnimationSink
    {
        public List<string> Log { get; } = new();

        public void Started(string refKey, AnimationClip clip) => Log.Add($"start {clip.Name}");

        public void Ended(string refKey, AnimationClip clip, bool completed) => Log.Add($"end {clip.Name} {completed}");
    }

    private static AnimationClip Clip(string name, float seconds, params (float Time, string Text)[] keys) =>
        new(name, seconds, keys.Select(key => new AnimationKey(key.Time, key.Text)).ToArray());

    private static string[] Names(AnimationStep step) => step.Events.Select(raised => raised.Name).ToArray();

    [Fact]
    public void AClipRaisesItsTextKeysInOrderThenDoneAndMovesTheOpenStateThroughOpeningToOpen()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 2f, (0.5f, "Hit"), (1f, "Latch")) } };
        var sink = new Sink();
        var table = new ReferenceAnimations(clips, sink);

        Assert.True(table.Play("A", "Open"));
        Assert.Equal(ReferenceOpenNativeAdapters.StateOpening, table.OpenState("A"));
        Assert.Equal(new[] { "Hit" }, Names(table.Advance(0.6)));
        Assert.Equal(new[] { "Latch" }, Names(table.Advance(0.5)));
        Assert.Equal(ReferenceOpenNativeAdapters.StateOpening, table.OpenState("A"));
        Assert.Equal(new[] { "done" }, Names(table.Advance(1.0)));
        Assert.Equal(ReferenceOpenNativeAdapters.StateOpen, table.OpenState("A"));
        Assert.Equal(0, table.Playing);
        Assert.Equal(new[] { "start Open", "end Open True" }, sink.Log.ToArray());
        Assert.Empty(table.Advance(5).Events);
    }

    [Fact]
    public void ACloseClipReadsClosingThenClosedAndAPlainClipLeavesTheStateAlone()
    {
        var clips = new Clips { Known = { [("A", "Close")] = Clip("Close", 1f), [("A", "Play01")] = Clip("Play01", 1f) } };
        var table = new ReferenceAnimations(clips);

        Assert.Null(table.OpenState("A"));
        table.Play("A", "Play01");
        Assert.Null(table.OpenState("A"));
        table.Advance(2);
        table.Play("A", "Close");
        Assert.Equal(ReferenceOpenNativeAdapters.StateClosing, table.OpenState("A"));
        table.Advance(2);
        Assert.Equal(ReferenceOpenNativeAdapters.StateClosed, table.OpenState("A"));
    }

    [Fact]
    public void AWaitIsCompletedOnceByTheNamedEventBeforeTheClipEnds()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 2f, (1f, "Latch")) } };
        var table = new ReferenceAnimations(clips);

        Assert.True(table.Play("A", "Open", "latch", 5));
        Assert.Empty(table.Advance(0.9).Wakes);
        Assert.Equal(new[] { new AnimationWake(5, true) }, table.Advance(0.2).Wakes.ToArray());
        Assert.Empty(table.Advance(5).Wakes);
    }

    [Fact]
    public void AWaitForAnEventTheClipNeverRaisesIsStillReleasedAtTheClipEnd()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 1f) } };
        var table = new ReferenceAnimations(clips);

        table.Play("A", "Open", "Never", 9);

        Assert.Empty(table.Advance(0.5).Wakes);
        Assert.Equal(new[] { new AnimationWake(9, true) }, table.Advance(0.6).Wakes.ToArray());
    }

    [Fact]
    public void ANewClipOnTheSameReferenceReleasesTheOldWaitWithFalseAndAnInterruptedOpenSettlesOpen()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 4f), [("A", "Play01")] = Clip("Play01", 1f) } };
        var sink = new Sink();
        var table = new ReferenceAnimations(clips, sink);
        table.Play("A", "Open", "Done", 7);

        table.Play("A", "Play01");

        Assert.Equal(ReferenceOpenNativeAdapters.StateOpen, table.OpenState("A"));
        Assert.Equal(new[] { new AnimationWake(7, false) }, table.Advance(0.1).Wakes.ToArray());
        Assert.Equal(new[] { "start Open", "end Open False", "start Play01" }, sink.Log.ToArray());
        Assert.Empty(table.Advance(0.1).Wakes);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void AClipWithNoUsableLengthOrAnUnknownNameIsNotPlayed(float seconds)
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", seconds) } };
        var table = new ReferenceAnimations(clips);

        Assert.False(table.Play("A", "Open", "Done", 3));
        Assert.False(table.Play("A", "Missing"));
        Assert.Equal(0, table.Playing);
        Assert.Empty(table.Advance(1).Wakes);
    }

    [Fact]
    public void ClearDropsTracksAndStatesWithoutCompletingTheirWaits()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 4f) } };
        var table = new ReferenceAnimations(clips);
        table.Play("A", "Open", "Done", 7);
        table.Play("A", "Open");

        table.Clear();

        Assert.Equal(0, table.Playing);
        Assert.Empty(table.Advance(10).Wakes);
        Assert.Null(table.OpenState("A"));
    }

    private static NifAnimSequenceInfo Sequence(string name, float start, float stop, float frequency = 1f, int textKeys = -1, params string[] controlled) => new()
    {
        Block = 1, Name = name, Weight = 1f, TextKeysRef = textKeys, Cycle = 2, Frequency = frequency, StartTime = start, StopTime = stop,
        ManagerRef = -1, AccumRoot = "",
        Controlled = controlled.Select(node => new NifAnimControlled { NodeName = node, ControllerType = "NiTransformController", InterpolatorId = "", InterpolatorRef = -1, ControllerRef = -1, Priority = 0 }).ToArray(),
    };

    private static NifAnimPoseInfo Pose(string node, Vector3 translation, Vector4 rotation) =>
        new() { NodeName = node, Translation = translation, Rotation = rotation, Scale = 1f };

    private static readonly Vector4 NifIdentity = new(1, 0, 0, 0);

    private static IReadOnlyList<NifAnimPoseInfo> Lift(int block, float time) => [Pose("Lift", new Vector3(0, 0, 5 + 10 * time), NifIdentity)];

    private sealed class SteppedSink : IAnimationSink
    {
        public List<double> Times { get; } = new();

        public void Started(string refKey, AnimationClip clip)
        {
        }

        public void Ended(string refKey, AnimationClip clip, bool completed)
        {
        }

        public void Stepped(string refKey, AnimationClip clip, double seconds) => Times.Add(seconds);
    }

    [Fact]
    public void ARootOnlyClipSamplesTheRootRelativeToItsRestPoseAndClampsAtTheEnds()
    {
        var root = new NifRoot("Lift", new Vector3(0, 0, 5), Quaternion.Identity);
        var clip = NifAnimationClips.From([Sequence("Open", 0f, 2f, 1f, -1, "Lift")], _ => [], "Open", root, Lift)!;

        Assert.Null(clip.PoseBlocked);
        Assert.Equal(Vector3.Zero, clip.Root!.At(0).Translation);
        Assert.Equal(10.0, clip.Root.At(1).Translation.Z, 3);
        Assert.Equal(5.0, clip.Root.At(0.5).Translation.Z, 3);
        Assert.Equal(20.0, clip.Root.At(2).Translation.Z, 3);
        Assert.Equal(20.0, clip.Root.At(9).Translation.Z, 3);
        Assert.Equal(Vector3.Zero, clip.Root.At(-1).Translation);
    }

    [Fact]
    public void ARootRotatingAboutItsRestPivotGivesAQuarterTurnDeltaWithNoTranslationAndAGodotUpAxis()
    {
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var root = new NifRoot("Lift", new Vector3(1, 0, 0), quarter);
        IReadOnlyList<NifAnimPoseInfo> Half(int block, float time) => [Pose("Lift", new Vector3(0, 1, 0), new Vector4(0, 0, 0, 1))];

        var clip = NifAnimationClips.From([Sequence("Open", 0f, 1f, 1f, -1, "Lift")], _ => [], "Open", root, Half)!;
        var delta = clip.Root!.At(1);
        var turned = Vector3.Transform(Vector3.UnitX, delta.Rotation);
        var godot = delta.ToGodot(0.01f);

        Assert.Equal(0.0, turned.X, 3);
        Assert.Equal(1.0, turned.Y, 3);
        Assert.Equal(0.0, delta.Translation.Length(), 3);
        Assert.Equal(Math.Sin(Math.PI / 4), godot.Rotation.Y, 3);
        Assert.Equal(0.0, godot.Rotation.Z, 3);
        Assert.Equal(0.2, new RootDelta(new Vector3(0, 0, 20), Quaternion.Identity).ToGodot(0.01f).Translation.Y, 5);
    }

    [Fact]
    public void AClipThatAnimatesASubBlockIsBlockedWithAReasonAndAClipWithNoRootInfoHasNoPoseAtAll()
    {
        var root = new NifRoot("Lift", Vector3.Zero, Quaternion.Identity);

        var blocked = NifAnimationClips.From([Sequence("Open", 0f, 2f, 1f, -1, "Lift", "Door")], _ => [], "Open", root, Lift)!;
        var bare = NifAnimationClips.From([Sequence("Open", 0f, 2f, 1f, -1, "Lift")], _ => [], "Open")!;
        var none = NifAnimationClips.From([Sequence("Open", 0f, 2f)], _ => [], "Open", root, Lift)!;

        Assert.Null(blocked.Root);
        Assert.Contains("1 node(s)", blocked.PoseBlocked);
        Assert.Null(bare.Root);
        Assert.Null(bare.PoseBlocked);
        Assert.Null(none.Root);
        Assert.Null(none.PoseBlocked);
    }

    [Fact]
    public void ARootThatTheSequenceNeverEvaluatesBlocksTheClipInsteadOfGuessingAPose()
    {
        var root = new NifRoot("Lift", Vector3.Zero, Quaternion.Identity);

        var clip = NifAnimationClips.From([Sequence("Open", 0f, 2f, 1f, -1, "Lift")], _ => [], "Open", root, (block, time) => [])!;

        Assert.Null(clip.Root);
        Assert.NotNull(clip.PoseBlocked);
    }

    [Fact]
    public void TheSinkIsSteppedWithTheClampedClipTimeEveryAdvance()
    {
        var clips = new Clips { Known = { [("A", "Open")] = Clip("Open", 2f) } };
        var sink = new SteppedSink();
        var table = new ReferenceAnimations(clips, sink);
        table.Play("A", "Open");

        table.Advance(0.5);
        table.Advance(5);

        Assert.Equal(new[] { 0.5, 2.0 }, sink.Times.ToArray());
    }

    [Fact]
    public void ANifSequenceBecomesAClipWithKeysInClipSecondsDroppingKeysOutsideItAndSortedByTime()
    {
        var sequences = new[] { Sequence("Idle", 0f, 9f), Sequence("Open", 1f, 3f, 2f, textKeys: 5) };
        IReadOnlyList<NifAnimTextKeyInfo> Keys(int block) => block == 5
            ? [new() { Time = 2.5f, Text = "b" }, new() { Time = 1.5f, Text = "a" }, new() { Time = 4f, Text = "late" }, new() { Time = 0f, Text = "early" }]
            : [];

        var clip = NifAnimationClips.From(sequences, Keys, "open")!;

        Assert.Equal("Open", clip.Name);
        Assert.Equal(1f, clip.Seconds);
        Assert.Equal(new[] { new AnimationKey(0.25f, "a"), new AnimationKey(0.75f, "b") }, clip.Keys.ToArray());
    }

    [Theory]
    [InlineData(2f, 2f, 1f)]
    [InlineData(3f, 1f, 1f)]
    [InlineData(0f, 2f, 0f)]
    public void ANifSequenceWithNoPositiveFiniteLengthOrAMissingNameIsNoClip(float start, float stop, float frequency)
    {
        var sequences = new[] { Sequence("Open", start, stop, frequency) };

        Assert.Null(NifAnimationClips.From(sequences, _ => [], "Open"));
        Assert.Null(NifAnimationClips.From(new[] { Sequence("Open", 0f, 2f) }, _ => [], "Close"));
    }

    [Fact]
    public void ANifSequenceWithNoTextKeyBlockIsAClipWithoutKeysAndNeverReadsTheBlock()
    {
        var clip = NifAnimationClips.From([Sequence("Open", 0f, 2f)], _ => throw new InvalidOperationException("must not read"), "Open")!;

        Assert.Equal(2f, clip.Seconds);
        Assert.Empty(clip.Keys);
    }

    private IScriptHost ClipHost(HostKind kind, EvidenceGate gate, List<bool> answers, List<string> heard)
    {
        var host = Track(TestHosts.Create(kind, gate,
            TestHosts.Stub("ScriptObject", null,
                "bool Function RegisterForAnimationEvent(ObjectReference akSender, string asEventName) native",
                "Function UnregisterForAnimationEvent(ObjectReference akSender, string asEventName) native"),
            TestHosts.Stub("ObjectReference", "ScriptObject",
                "bool Function PlayAnimation(string asAnimation) native",
                "bool Function PlayAnimationAndWait(string asAnimation, string asEventName) native"),
            TestHosts.Stub("Probe", null,
                "ObjectReference Function Self() global native",
                "Function Answer(bool value) global native",
                "Function Heard(string value) global native"),
            new ScriptSpec("Caller", "Scriptname Caller\nFunction Run()\n  ObjectReference target = Probe.Self()\n  Probe.Answer(target.PlayAnimationAndWait(\"Open\", \"Done\"))\nEndFunction\n"),
            new ScriptSpec("InitCaller", "Scriptname InitCaller\nEvent OnInit()\n  ObjectReference target = Probe.Self()\n  Probe.Answer(target.PlayAnimationAndWait(\"Open\", \"Done\"))\nEndEvent\n"),
            new ScriptSpec("Listener", "Scriptname Listener extends ObjectReference\nFunction Listen(ObjectReference akSender)\n  RegisterForAnimationEvent(akSender, \"Latch\")\nEndFunction\nEvent OnAnimationEvent(ObjectReference akSource, string asEventName)\n  Probe.Heard(asEventName)\nEndEvent\n"),
            new ScriptSpec("SelfListener", "Scriptname SelfListener extends ObjectReference\nFunction Listen()\n  RegisterForAnimationEvent(Self, \"Latch\")\nEndFunction\nEvent OnAnimationEvent(ObjectReference akSource, string asEventName)\n  Probe.Heard(asEventName)\nEndEvent\n"),
            new ScriptSpec("Deaf", "Scriptname Deaf extends ObjectReference\nEvent OnAnimationEvent(ObjectReference akSource, string asEventName)\n  Probe.Heard(asEventName)\nEndEvent\n")));
        host.RegisterNative(new NativeId("Probe", "Self"), _ => PapyrusValue.FromObject(new EngineObjectReference(Lever)));
        host.RegisterNative(new NativeId("Probe", "Answer"), arguments =>
        {
            answers.Add(arguments[0].Flag);
            return PapyrusValue.None;
        });
        host.RegisterNative(new NativeId("Probe", "Heard"), arguments =>
        {
            heard.Add(arguments[0].Text ?? "");
            return PapyrusValue.None;
        });
        return host;
    }

    private static Clips OpenClip() => new() { Known = { [("000A00", "Open")] = Clip("Open", 1f, (0.5f, "Latch")) } };

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void PlayAnimationAndWaitSuspendsTheScriptUntilTheClipEventAndThenAnswersTrue(HostKind kind)
    {
        var answers = new List<bool>();
        var gate = Gate();
        var host = ClipHost(kind, gate, answers, new List<string>());
        var table = new ReferenceAnimations(OpenClip());
        new ReferenceAnimationNativeAdapters(host, gate, table).Bind();
        var instance = host.Attach("Caller");
        Assert.True(host.Initialize(instance).Completed);

        var result = host.RunEvent(instance, "Run");

        Assert.Equal(VmStopReason.Waiting, result.Reason);
        Assert.Equal(1, host.WaitingCount);
        Assert.Empty(answers);
        table.Deliver(host, 0.4);
        host.RunStep(32);
        Assert.Empty(answers);
        table.Deliver(host, 0.7);
        host.RunStep(32);
        Assert.Equal(new[] { true }, answers.ToArray());
        Assert.Equal(0, host.WaitingCount);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnOnInitCallerGetsTheClipButNeverWaitsSoTheScriptIsNotFailed(HostKind kind)
    {
        var answers = new List<bool>();
        var gate = Gate();
        var host = ClipHost(kind, gate, answers, new List<string>());
        var table = new ReferenceAnimations(OpenClip());
        new ReferenceAnimationNativeAdapters(host, gate, table).Bind();
        var instance = host.Attach("InitCaller");

        var result = host.Initialize(instance);

        Assert.True(result.Completed, TestHosts.Describe(host, result));
        Assert.Equal(0, host.WaitingCount);
        Assert.Equal(new[] { true }, answers.ToArray());
        Assert.Equal(1, table.Playing);
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void OnAnimationEventReachesOnlyTheScriptThatRegisteredForThatSenderAndEvent(HostKind kind)
    {
        var heard = new List<string>();
        var gate = Gate();
        var host = ClipHost(kind, gate, new List<bool>(), heard);
        var table = new ReferenceAnimations(OpenClip());
        var report = new ReferenceAnimationEventNativeAdapters(host, gate, table).Bind();
        var listener = host.Attach("Listener");
        var deaf = host.Attach("Deaf");
        Assert.True(host.Initialize(listener).Completed);
        Assert.True(host.Initialize(deaf).Completed);
        var sender = PapyrusValue.FromObject(new EngineObjectReference(Lever));

        var registered = host.RunEvent(listener, "Listen", sender);
        table.Play("000A00", "Open");

        Assert.Equal(2, report.Bound);
        Assert.True(registered.Completed, TestHosts.Describe(host, registered));
        Assert.Equal(1, table.Registered);
        Assert.Equal(1, table.Deliver(host, 0.6));
        Assert.Equal(new[] { "Latch" }, heard.ToArray());
        Assert.Equal(0, table.Deliver(host, 1.0));
        Assert.Equal(new[] { "Latch" }, heard.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void ARegistrationWithSelfAsTheSenderIsAcceptedAndDeliveredToThatScript(HostKind kind)
    {
        var heard = new List<string>();
        var gate = Gate();
        var host = ClipHost(kind, gate, new List<bool>(), heard);
        var table = new ReferenceAnimations(OpenClip());
        new ReferenceAnimationEventNativeAdapters(host, gate, table).Bind();
        var listener = host.Attach("SelfListener", owner: Lever);
        Assert.True(host.Initialize(listener).Completed);

        var registered = host.RunEvent(listener, "Listen");
        table.Play("000A00", "Open");

        Assert.True(registered.Completed, TestHosts.Describe(host, registered));
        Assert.Equal(1, table.Registered);
        Assert.Equal(1, table.Deliver(host, 0.6));
        Assert.Equal(new[] { "Latch" }, heard.ToArray());
    }

    [Theory]
    [MemberData(nameof(TestHosts.HostKinds), MemberType = typeof(TestHosts))]
    public void AnEventPostedByALiveActorClipReachesTheRegisteredScriptOnTheNextDeliver(HostKind kind)
    {
        var heard = new List<string>();
        var gate = Gate();
        var host = ClipHost(kind, gate, new List<bool>(), heard);
        var table = new ReferenceAnimations(new Clips());
        new ReferenceAnimationEventNativeAdapters(host, gate, table).Bind();
        var listener = host.Attach("SelfListener", owner: Lever);
        Assert.True(host.Initialize(listener).Completed);
        Assert.True(host.RunEvent(listener, "Listen").Completed);

        Assert.False(table.Post("000A00", "", "Other"));
        Assert.False(table.Post("000B00", "", "Latch"));
        Assert.True(table.Post("000A00", "", "latch"));
        Assert.Empty(heard);
        Assert.Equal(1, table.Deliver(host, 0.0));
        Assert.Equal(new[] { "latch" }, heard.ToArray());
        Assert.Equal(0, table.Deliver(host, 0.0));
    }

    [Fact]
    public void APostedEventWakesAWaitOnThatNameAndTheQueueIsBounded()
    {
        var table = new ReferenceAnimations(OpenClip());
        long token = ReferenceAnimations.NextToken();
        Assert.True(table.Play("000A00", "Open", "Latch", token));
        Assert.True(table.Post("000A00", "", "Latch"));
        var step = table.Advance(0.0);
        Assert.Contains(step.Wakes, wake => wake.Token == token && wake.Completed);
        for (int i = 0; i < ReferenceAnimations.PostCapacity; i++) Assert.True(table.Post("000A00", "", "Latch"));
        Assert.False(table.Post("000A00", "", "Latch"));
        Assert.Equal(1, table.Dropped);
        table.Clear();
        Assert.Equal(0, table.Dropped);
        Assert.Empty(table.Advance(0.0).Events);
    }
}
