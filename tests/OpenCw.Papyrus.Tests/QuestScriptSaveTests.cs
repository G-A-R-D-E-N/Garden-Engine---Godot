using System.Text;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Quest;
using OpenCommonwealth.Services.Papyrus.Save;
using OpenCommonwealth.Services.Papyrus.Vm;
using Xunit;
using GeSaveContainer = OpenCommonwealth.Services.GeSaveContainer;
using GeSaveStatus = OpenCommonwealth.Services.GeSaveStatus;

namespace OpenCw.Papyrus.Tests;

public sealed class QuestScriptSaveTests : HostFixture
{
    private const string Quest = "000800:QuestProbe.esp";
    private const string Direct = "000801:QuestProbe.esp";
    private const string Second = "000802:QuestProbe.esp";
    private const string Decoy = "000700:QuestProbe.esp";

    private static readonly ScriptSpec StateScript = TestHosts.Stub("StateScript", null,
        "Struct Pair\n  Int A\n  String B\nEndStruct",
        "Function GoToState(String asNewState) native",
        "Int Property Count Auto",
        "Int[] Property Items Auto",
        "Pair Property Snap Auto",
        "StateScript Property Friend Auto",
        "Form Property Target Auto",
        "Form Function Pick() global native",
        "String Function Describe(Form subject) global native",
        "Function Fill()\n  Items = new Int[3]\n  Items[0] = 4\n  Items[1] = 5\n  Items[2] = 6\n  Snap = new Pair\n  Snap.A = 9\n  Snap.B = \"nine\"\n  Count = 3\nEndFunction",
        "Function GoBusy()\n  GoToState(\"Busy\")\nEndFunction",
        "State Busy\n  Function OnX()\n  EndFunction\nEndState",
        "Function Grab()\n  Target = Pick()\nEndFunction",
        "Int Function ReadA()\n  Return Snap.A\nEndFunction",
        "String Function ReadB()\n  Return Snap.B\nEndFunction",
        "Int Function ReadItem(Int index)\n  Return Items[index]\nEndFunction",
        "String Function Who()\n  Return Describe(Target)\nEndFunction");

    private static readonly ScriptSpec[] Scripts =
    {
        TestHosts.Stub("ScriptObject"),
        TestHosts.Stub("Form"),
        StateScript,
        TestHosts.Stub("QuestScript", null,
            "Int Property Count Auto",
            "Float Property SignedZero Auto",
            "Bool Property Flag Auto",
            "String Property Text Auto",
            "Form Property Link Auto",
            "Int Property Level = 7 Auto",
            "Bool Property Armed Auto",
            "Int Property Total Auto",
            "String Property Label Auto",
            "Function OnInit()\nEndFunction",
            "Function Bump()\n  Count = Count + 5\n  SignedZero = 2.5\n  Flag = true\n  Text = \"bumped\"\n  Level = Level + 1\n  Armed = true\n  Total = Total + 3\n  Label = \"set\"\nEndFunction"),
    };

    private static QuestStartupInput Holder(string quest)
    {
        var input = QuestStartupBindingTests.Input();
        return input with { Quest = quest, Scripts = new[] { "StateScript" }, Metadata = input.Metadata! with { ScriptProperties = Array.Empty<QuestStartupProperty>() } };
    }

    private static QuestStartupInput Scalar(string quest = Quest)
    {
        var input = QuestStartupBindingTests.Input();
        return input with
        {
            Quest = quest,
            Metadata = input.Metadata! with { ScriptProperties = input.Metadata.ScriptProperties.Take(4).ToArray() },
        };
    }

    private GameSession Started(params QuestStartupInput[] startup) => StartedWith(Scripts, startup);

    private GameSession StartedWith(ScriptSpec[] scripts, params QuestStartupInput[] startup)
    {
        var gate = QuestStartupBindingTests.Gate();
        var session = new GameSession(Track(TestHosts.Create(HostKind.Radicle, gate, scripts)), gate, startup);
        var result = session.StartNewGame();
        Assert.True(result.Applied, string.Join("; ", result.Reasons));
        return session;
    }

    private static ScriptInstance Only(GameSession session, string quest = Quest) => Assert.Single(session.Quests.InstancesFor(quest));

    private static Dictionary<string, object> Values(GameSession session, string quest = Quest) =>
        ((RadicleScriptHost)session.Host).ReadScalarProperties(Only(session, quest)).ToDictionary(property => property.Name, property => property.Value);

    private static void Bump(GameSession session, string quest = Quest) => Assert.True(session.Host.RunEvent(Only(session, quest), "Bump").Completed);

    private static QuestScriptRestoreReport RoundTrip(GameSession from, GameSession to) =>
        QuestScriptSave.Restore(QuestScriptSave.Read(QuestScriptSave.Sections(from.Quests, from.Host)), to.Quests, to.Host);

    private static string Doc(string quests, string version = "1") => "{\"version\":" + version + ",\"quests\":[" + quests + "]}";

    private const string GoodQuest = "{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\",\"value\":1}]}]}";

    [Fact]
    public void ThePropertiesAScriptChangedAreReadBackAndRestoredIntoAFreshSession()
    {
        var before = Started(Scalar());
        Assert.Equal(17, Values(before)["Count"]);
        Bump(before);
        var played = Values(before);
        Assert.Equal(22, played["Count"]);

        var after = Started(Scalar());
        Assert.Equal(17, Values(after)["Count"]);
        var report = RoundTrip(before, after);

        Assert.Equal(played.Count, report.Written);
        Assert.Equal(0, report.Reset + report.DroppedQuests + report.DroppedScripts + report.DroppedProperties + report.NotInSave);
        Assert.Equal(played, Values(after));
        Bump(after);
        Assert.Equal(27, Values(after)["Count"]);
    }

    [Fact]
    public void ObjectPropertiesAreNotCapturedAndNeverAppearInTheSection()
    {
        var session = Started(Scalar());
        var names = QuestScriptSave.Capture(session.Quests, session.Host).Quests.Single().Scripts.Single().Properties.Select(property => property.Name);
        Assert.Equal(new[] { "Count", "Flag", "SignedZero", "Text" }, names.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Link", Encoding.UTF8.GetString(QuestScriptSave.Sections(session.Quests, session.Host)[QuestScriptSave.SectionName]));
    }

    [Fact]
    public void ADirectPathQuestIsRecordedAsStartedWithNoPropertiesCaptured()
    {
        var session = Started(Scalar(), QuestStartupBindingTests.Input() with { Quest = Direct });
        Assert.False(session.Quests.IsLifecycle(Direct));
        var snapshot = QuestScriptSave.Capture(session.Quests, session.Host);
        Assert.Equal(new[] { Quest, Direct }, snapshot.Quests.Select(entry => entry.Quest));
        Assert.Empty(snapshot.Quests.Single(entry => entry.Quest == Direct).Scripts);
    }

    [Fact]
    public void AQuestTheSessionNeverStartedIsDroppedAndLeavesTheRestAlone()
    {
        var saved = Started(Scalar(), Scalar("000802:QuestProbe.esp"));
        Bump(saved);
        Bump(saved, "000802:QuestProbe.esp");
        var loaded = Started(Scalar());

        var report = RoundTrip(saved, loaded);

        Assert.Equal(1, report.DroppedQuests);
        Assert.Equal(Values(saved).Count, report.Written);
        Assert.Equal(22, Values(loaded)["Count"]);
    }

    [Fact]
    public void AStartedQuestMissingFromTheSaveKeepsItsStartupValuesAndIsCounted()
    {
        var saved = Started(Scalar());
        var loaded = Started(Scalar(), Scalar("000802:QuestProbe.esp"));
        Bump(loaded, "000802:QuestProbe.esp");

        var report = RoundTrip(saved, loaded);

        Assert.Equal(1, report.NotInSave);
        Assert.Equal(22, Values(loaded, "000802:QuestProbe.esp")["Count"]);
    }

    [Fact]
    public void AScriptOrPropertyTheLoadedScriptsNoLongerDeclareIsDroppedWithoutBlockingTheRest()
    {
        var session = Started(Scalar());
        Bump(session);
        var snapshot = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[]
            {
                new QuestScriptInstance("QuestScript", new[]
                {
                    new QuestScriptProperty("Count", "Int", 99),
                    new QuestScriptProperty("Removed", "Int", 1),
                    new QuestScriptProperty("Text", "Int", 7),
                    new QuestScriptProperty("Link", "String", "x"),
                }),
                new QuestScriptInstance("GoneScript", new[] { new QuestScriptProperty("Count", "Int", 5) }),
            }),
        });

        var report = QuestScriptSave.Restore(snapshot, session.Quests, session.Host);

        Assert.Equal(1, report.Written);
        Assert.Equal(6, report.Reset);
        Assert.Equal(1, report.DroppedScripts);
        Assert.Equal(4, report.DroppedProperties);
        var values = Values(session);
        Assert.Equal(99, values["Count"]);
        Assert.Equal("bumped", values["Text"]);
        Assert.Equal(false, values["Flag"]);
        Assert.Equal(7, values["Level"]);
    }

    [Fact]
    public void ANegativeZeroFloatKeepsItsSignThroughTheSection()
    {
        var before = Started(Scalar());
        var after = Started(Scalar());
        Bump(after);
        Assert.Equal(2.5f, Values(after)["SignedZero"]);

        RoundTrip(before, after);

        Assert.Equal(BitConverter.SingleToInt32Bits(-0.0f), BitConverter.SingleToInt32Bits((float)Values(after)["SignedZero"]));
    }

    [Fact]
    public void APropertyTheSaveNeverMentionedIsResetToItsInitialValueOnAQuickload()
    {
        var session = Started(Scalar());
        var snapshot = QuestScriptSave.Read(QuestScriptSave.Sections(session.Quests, session.Host));
        Bump(session);
        Assert.Equal(8, Values(session)["Level"]);
        Assert.Equal(true, Values(session)["Armed"]);

        var report = QuestScriptSave.Restore(snapshot, session.Quests, session.Host);

        var values = Values(session);
        Assert.Equal(7, values["Level"]);
        Assert.Equal(false, values["Armed"]);
        Assert.Equal(0, values["Total"]);
        Assert.Equal("", values["Label"]);
        Assert.Equal(17, values["Count"]);
        Assert.Equal(4, report.Written);
        Assert.Equal(4, report.Reset);
    }

    [Fact]
    public void AFailedWritePutsTheEarlierWritesBackAndRethrows()
    {
        var session = Started(Scalar());
        Bump(session);
        var snapshot = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[]
            {
                new QuestScriptInstance("QuestScript", new[]
                {
                    new QuestScriptProperty("Count", "Int", 99),
                    new QuestScriptProperty("Flag", "Bool", new object()),
                }),
            }),
        });
        var before = Values(session);

        Assert.Throws<ArgumentException>(() => QuestScriptSave.Restore(snapshot, session.Quests, session.Host));

        Assert.Equal(before, Values(session));
    }

    [Fact]
    public void TheRestoreLineIsSilentOnlyWhenEveryCountIsZero()
    {
        Assert.Null(new QuestScriptRestoreReport(0, 0, 0, 0, 0, 0).ToLine());
        Assert.Contains("2 started quest(s) not in the save", new QuestScriptRestoreReport(0, 0, 0, 0, 0, 2).ToLine());
        Assert.Contains("reset 3", new QuestScriptRestoreReport(0, 3, 0, 0, 0, 0).ToLine());
    }

    [Fact]
    public void ASessionWithoutAStartedQuestWritesNoSectionAndAMissingSectionReadsAsEmpty()
    {
        Assert.Empty(QuestScriptSave.Sections(null, null));
        Assert.Empty(QuestScriptSave.Sections(new QuestRuntime(QuestStartupBindingTests.Gate()), null));
        Assert.True(QuestScriptSave.Read(new Dictionary<string, byte[]>()).IsEmpty);
    }

    [Fact]
    public void TheSectionTextIsPinned()
    {
        var session = Started(Scalar());
        var text = Encoding.UTF8.GetString(QuestScriptSave.Sections(session.Quests, session.Host)[QuestScriptSave.SectionName]);
        const string expected = "{\n  \"version\": 1,\n  \"quests\": [\n    {\n      \"quest\": \"000800:QuestProbe.esp\",\n      \"scripts\": [\n        {\n          \"script\": \"QuestScript\",\n          \"properties\": [\n" +
            "            {\n              \"name\": \"Count\",\n              \"type\": \"Int\",\n              \"value\": 17\n            },\n" +
            "            {\n              \"name\": \"Flag\",\n              \"type\": \"Bool\",\n              \"value\": false\n            },\n" +
            "            {\n              \"name\": \"SignedZero\",\n              \"type\": \"Float\",\n              \"value\": -0\n            },\n" +
            "            {\n              \"name\": \"Text\",\n              \"type\": \"String\",\n              \"value\": \"a text\"\n            }\n" +
            "          ]\n        }\n      ]\n    }\n  ]\n}";
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"quests\":[]}")]
    [InlineData("{\"version\":\"1\",\"quests\":[]}")]
    [InlineData("{\"version\":2,\"quests\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"version\":1,\"quests\":[]}")]
    public void AMalformedEnvelopeIsRefused(string json) => Assert.Throws<FormatException>(() => QuestScriptSave.Deserialize(json));

    [Theory]
    [InlineData("1")]
    [InlineData("{\"scripts\":[]}")]
    [InlineData("{\"quest\":\"\",\"scripts\":[]}")]
    [InlineData("{\"quest\":\"q\"}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[1]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"properties\":[]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\"}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[]},{\"script\":\"s\",\"properties\":[]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[1]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\",\"value\":1},{\"name\":\"n\",\"type\":\"Int\",\"value\":1}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Object\",\"value\":1}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"int\",\"value\":1}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\",\"value\":1.5}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\",\"value\":3000000000}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\",\"value\":\"1\"}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Int\"}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Float\",\"value\":1e999}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"Bool\",\"value\":1}]}]}")]
    [InlineData("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[{\"name\":\"N\",\"type\":\"String\",\"value\":null}]}]}")]
    public void AMalformedQuestEntryIsRefused(string entry) => Assert.Throws<FormatException>(() => QuestScriptSave.Deserialize(Doc(entry)));

    [Fact]
    public void ADuplicateQuestIsRefused() => Assert.Throws<FormatException>(() => QuestScriptSave.Deserialize(Doc(GoodQuest + "," + GoodQuest)));

    [Fact]
    public void AWellFormedEntryDecodesEveryType()
    {
        var snapshot = QuestScriptSave.Deserialize(Doc("{\"quest\":\"q\",\"scripts\":[{\"script\":\"S\",\"properties\":[" +
            "{\"name\":\"I\",\"type\":\"Int\",\"value\":-5},{\"name\":\"F\",\"type\":\"Float\",\"value\":0.5},{\"name\":\"B\",\"type\":\"Bool\",\"value\":true},{\"name\":\"T\",\"type\":\"String\",\"value\":\"\"}]}]}"));
        var properties = snapshot.Quests.Single().Scripts.Single().Properties;
        Assert.Equal(new object[] { -5, 0.5f, true, "" }, properties.Select(property => property.Value));
    }

    [Fact]
    public void ANonFiniteFloatCannotBeWritten() =>
        Assert.Throws<FormatException>(() => QuestScriptSave.Serialize(new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry("q", new[] { new QuestScriptInstance("S", new[] { new QuestScriptProperty("N", "Float", float.NaN) }) }),
        })));

    [Fact]
    public void AValueOfTheWrongClrTypeCannotBeWritten() =>
        Assert.Throws<FormatException>(() => QuestScriptSave.Serialize(new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry("q", new[] { new QuestScriptInstance("S", new[] { new QuestScriptProperty("N", "Int", "text") }) }),
        })));

    private GameSession Holders(params string[] quests) => Started(quests.Select(Holder).ToArray());

    private static RadicleScriptHost Radicle(GameSession session) => (RadicleScriptHost)session.Host;

    private static ulong IdOf(GameSession session, string quest) => Only(session, quest).HostId;

    private static object? Prop(GameSession session, string quest, string name) =>
        Radicle(session).Runtime.TryGetProperty(IdOf(session, quest), name, out var value) ? value : null;

    private static PapyrusValue Call(GameSession session, string quest, string function, params PapyrusValue[] arguments)
    {
        var result = session.Host.RunEvent(Only(session, quest), function, arguments);
        Assert.True(result.Completed, result.Message ?? result.Reason.ToString());
        return result.Value;
    }

    private static void Link(GameSession session, string quest, string other) =>
        Radicle(session).Runtime.SetVariable(IdOf(session, quest), "::Friend_var", new Radicle.Interop.PapyrusObjectRef(IdOf(session, other), "StateScript", ""));

    private static Dictionary<string, byte[]> Saved(GameSession session)
    {
        var sections = QuestScriptSave.Sections(session.Quests, session.Host).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (name, payload) in PapyrusStateSave.Sections(session.Host)) sections[name] = payload;
        return sections;
    }

    private static PapyrusStateRestoreReport Load(IReadOnlyDictionary<string, byte[]> sections, GameSession to)
    {
        var report = PapyrusStateSave.Restore(PapyrusStateSave.Read(sections)!, to.Host);
        QuestScriptSave.Restore(QuestScriptSave.Read(sections), to.Quests, to.Host);
        return report;
    }

    private static void RegisterEngineNatives(GameSession session, Func<string> pick)
    {
        session.Host.RegisterNative(new NativeId("StateScript", "Pick"), _ => PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(pick()))));
        session.Host.RegisterNative(new NativeId("StateScript", "Describe"), arguments => PapyrusValue.FromString(((EngineObjectReference)arguments[0].Reference!).Identity));
    }

    [Fact]
    public void AnExplicitEmptyQuestSnapshotLoadedOverAProgressedSessionClearsTheProgress()
    {
        var progressed = Holders(Quest);
        progressed.QuestStages.Apply(Quest, QuestNative.Start);
        Assert.True(progressed.QuestStages.Apply(Quest, QuestNative.SetStage, 20));
        var empty = new Dictionary<string, byte[]> { [QuestStateSave.SectionName] = Encoding.UTF8.GetBytes(QuestStateSave.Serialize(QuestStateSnapshot.Empty)) };

        var outcome = GameScriptSave.Restore(empty, progressed);

        Assert.True(outcome.Ok, outcome.Failure);
        Assert.Equal(0, progressed.QuestStages.StageOf(Quest));
        Assert.False(progressed.QuestStages.StageDone(Quest, 20));
    }

    [Fact]
    public void CapturingALiveSessionWithNoProgressStillWritesTheQuestSection()
    {
        var session = Holders(Quest);
        Assert.Contains(QuestStateSave.SectionName, GameScriptSave.Capture(session, null).Keys);
    }

    [Fact]
    public void ObjectArrayAndStructPropertiesRoundTripIntoAFreshSessionWithDifferentHandles()
    {
        var before = Holders(Decoy, Quest, Second);
        Call(before, Quest, "Fill");
        Link(before, Quest, Second);
        var sections = Saved(before);
        Assert.Contains(PapyrusStateSave.SectionName, sections.Keys);

        var after = Holders(Quest, Second);
        Assert.NotEqual(IdOf(before, Quest), IdOf(after, Quest));
        Assert.NotEqual(IdOf(before, Second), IdOf(after, Second));
        Assert.Null(Prop(after, Quest, "Items"));
        var report = Load(sections, after);

        Assert.Null(report.Refused);
        Assert.Equal((2, 1, 0), (report.Instances, report.SkippedInstances, report.UnboundReferences));
        Assert.Equal(3, Prop(after, Quest, "Count"));
        Assert.Equal(new object?[] { 4, 5, 6 }, ((IEnumerable<object?>)Prop(after, Quest, "Items")!).ToArray());
        Assert.Equal(IdOf(after, Second), ((Radicle.Interop.PapyrusObjectRef)Prop(after, Quest, "Friend")!).Handle);
        Assert.Equal(9, Call(after, Quest, "ReadA").Integer);
        Assert.Equal("nine", Call(after, Quest, "ReadB").Text);
        Assert.Equal(6, Call(after, Quest, "ReadItem", PapyrusValue.FromInt(2)).Integer);
    }

    [Fact]
    public void AnInstanceTheLoadedSessionLacksIsSkippedAndTheRestStillRestores()
    {
        var before = Holders(Quest, Second);
        Call(before, Quest, "Fill");
        Link(before, Quest, Second);
        var sections = Saved(before);

        var after = Holders(Quest);
        var report = Load(sections, after);

        Assert.Null(report.Refused);
        Assert.Equal(1, report.Instances);
        Assert.Equal(1, report.SkippedInstances);
        Assert.Equal(1, report.UnboundReferences);
        Assert.Equal(3, Prop(after, Quest, "Count"));
        Assert.Equal(9, Call(after, Quest, "ReadA").Integer);
        Assert.Equal(new object?[] { 4, 5, 6 }, ((IEnumerable<object?>)Prop(after, Quest, "Items")!).ToArray());
    }

    [Fact]
    public void AnEngineObjectReferenceFollowsItsIdentityToTheLiveSessionsHandle()
    {
        var before = Holders(Quest);
        RegisterEngineNatives(before, () => "000901:Probe.esp");
        Call(before, Quest, "Grab");
        var handle = Assert.Single(Radicle(before).CaptureState().Engine);
        Assert.Equal(0x8000000000000001UL, handle.Handle);
        Assert.Equal(PapyrusOwnerId.ForForm("000901:Probe.esp"), handle.Owner);
        var sections = Saved(before);

        var after = Holders(Quest);
        RegisterEngineNatives(after, () => "000999:Probe.esp");
        Call(after, Quest, "Grab");
        Assert.Equal("form:000999:Probe.esp#", Call(after, Quest, "Who").Text);

        var report = Load(sections, after);

        Assert.Null(report.Refused);
        Assert.Equal(0, report.UnboundReferences);
        Assert.Equal("form:000901:Probe.esp#", Call(after, Quest, "Who").Text);
    }

    [Fact]
    public void ARadicleRefusalIsReportedWithNoArrayWrittenAndTheScalarSectionStillApplies()
    {
        var before = Started(Scalar(), Holder(Second));
        Bump(before);
        Call(before, Second, "Fill");
        var sections = Saved(before);

        var changed = TestHosts.Stub("StateScript", null, "Int Property Count Auto", "Int Property Items Auto");
        var after = StartedWith(new[] { Scripts[0], Scripts[1], changed, Scripts[3] }, Scalar(), Holder(Second));
        var report = Load(sections, after);

        Assert.StartsWith("papyrus state: refused, instance state left as started: ", report.ToLine());
        Assert.Equal(0, report.Instances);
        Assert.Equal(3, Prop(after, Second, "Count"));
        Assert.Equal(0, Prop(after, Second, "Items"));
        Assert.Equal(22, Values(after)["Count"]);
    }

    [Fact]
    public void ASessionWithNothingAttachedWritesNoStateSectionAndASaveWithoutOneReadsAsNone()
    {
        var gate = QuestStartupBindingTests.Gate();
        var empty = Track(TestHosts.Create(HostKind.Radicle, gate, Scripts));
        Assert.Empty(PapyrusStateSave.Sections(empty));
        Assert.Empty(PapyrusStateSave.Sections(null));
        var scalarSession = Started(Scalar());
        Assert.Null(PapyrusStateSave.Read(QuestScriptSave.Sections(scalarSession.Quests, scalarSession.Host)));
        Assert.Null(PapyrusStateSave.Read(new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void TheStateSectionKeepsItsEngineHandlesAndRefusesATamperedBlob()
    {
        var before = Holders(Quest);
        RegisterEngineNatives(before, () => "000901:Probe.esp");
        Call(before, Quest, "Grab");
        var section = Encoding.UTF8.GetString(Saved(before)[PapyrusStateSave.SectionName]);
        var read = PapyrusStateSave.Deserialize(section);
        Assert.Single(read.Engine);
        Assert.Equal(PapyrusStateSave.Serialize(read), section);
        Assert.Equal(64, read.Digest.Length);
        Assert.Throws<FormatException>(() => PapyrusStateSave.Deserialize(section.Replace("StateScript", "StateScripT")));
    }

    private const string XDigest = "2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881";

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"blob\":\"x\",\"digest\":\"2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\",\"engine\":[]}")]
    [InlineData("{\"version\":2,\"blob\":\"x\",\"digest\":\"2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\",\"engine\":[]}")]
    [InlineData("{\"version\":1,\"digest\":\"2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\",\"engine\":[]}")]
    [InlineData("{\"version\":1,\"blob\":\"x\",\"engine\":[]}")]
    [InlineData("{\"version\":1,\"blob\":\"x\",\"digest\":\"y\",\"engine\":[]}")]
    [InlineData("{\"version\":1,\"blob\":\"x\",\"digest\":\"2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\"}")]
    [InlineData("{\"version\":1,\"version\":1,\"blob\":\"x\",\"digest\":\"2d711642b726b04401627ca9fbac32f5c8530fb1903cc4db02258717921a4881\",\"engine\":[]}")]
    public void AMalformedStateEnvelopeIsRefused(string json) => Assert.Throws<FormatException>(() => PapyrusStateSave.Deserialize(json));

    [Theory]
    [InlineData("1")]
    [InlineData("{\"identity\":\"i\"}")]
    [InlineData("{\"handle\":\"x\",\"identity\":\"i\"}")]
    [InlineData("{\"handle\":\"1\"}")]
    [InlineData("{\"handle\":\"1\",\"identity\":\"i\"},{\"handle\":\"1\",\"identity\":\"j\"}")]
    [InlineData("{\"handle\":\"1\",\"identity\":\"i\",\"owner\":1}")]
    [InlineData("{\"handle\":\"1\",\"identity\":\"i\",\"owner\":{\"kind\":\"Nope\",\"base\":\"b\",\"disc\":\"\"}}")]
    [InlineData("{\"handle\":\"1\",\"identity\":\"form:b#\",\"owner\":{\"kind\":\"Form\",\"base\":\"c\",\"disc\":\"\"}}")]
    public void AMalformedEngineEntryIsRefused(string entry) =>
        Assert.Throws<FormatException>(() => PapyrusStateSave.Deserialize("{\"version\":1,\"blob\":\"x\",\"digest\":\"" + XDigest + "\",\"engine\":[" + entry + "]}"));

    [Fact]
    public void AWellFormedEngineEntryDecodesWithAndWithoutAnOwner()
    {
        var read = PapyrusStateSave.Deserialize("{\"version\":1,\"blob\":\"x\",\"digest\":\"" + XDigest + "\",\"engine\":[" +
            "{\"handle\":\"9223372036854775809\",\"identity\":\"form:b#\",\"owner\":{\"kind\":\"Form\",\"base\":\"b\",\"disc\":\"\"}}," +
            "{\"handle\":\"9223372036854775810\",\"identity\":\"loose\"}]}");
        Assert.Equal(new[] { 0x8000000000000001UL, 0x8000000000000002UL }, read.Engine.Select(handle => handle.Handle));
        Assert.Equal(PapyrusOwnerId.ForForm("b"), read.Engine[0].Owner);
        Assert.Null(read.Engine[1].Owner);
    }

    [Fact]
    public void TwoLiveInstancesWithOneIdentityRefuseTheCaptureSoNoStateSectionIsWritten()
    {
        var session = Holders(Quest);
        var owner = PapyrusOwnerId.ForForm("000950:QuestProbe.esp");
        session.Host.Attach("StateScript", null, owner);
        session.Host.Attach("StateScript", null, owner);

        Assert.ThrowsAny<InvalidOperationException>(() => PapyrusStateSave.Sections(session.Host));
    }

    private static void SetCount(GameSession session, ScriptInstance instance, int value) =>
        Radicle(session).Runtime.SetVariable(instance.HostId, "::Count_var", value);

    private static object? CountOf(GameSession session, ScriptInstance instance) =>
        Radicle(session).Runtime.TryGetProperty(instance.HostId, "Count", out var value) ? value : null;

    [Fact]
    public void AnAliasScriptKeepsItsStateWhenALaterSessionAttachesTheAliasesInAnotherOrder()
    {
        var one = PapyrusOwnerId.ForQuestAlias(Quest, 1);
        var two = PapyrusOwnerId.ForQuestAlias(Quest, 2);
        var before = Holders(Quest);
        var first = before.Host.Attach("StateScript", null, stateIdentity: one);
        var second = before.Host.Attach("StateScript", null, stateIdentity: two);
        Assert.Null(first.Owner);
        SetCount(before, first, 11);
        SetCount(before, second, 22);
        var sections = Saved(before);

        var after = Holders(Quest);
        var swappedTwo = after.Host.Attach("StateScript", null, stateIdentity: two);
        var swappedOne = after.Host.Attach("StateScript", null, stateIdentity: one);
        var report = Load(sections, after);

        Assert.Null(report.Refused);
        Assert.Equal((3, 0), (report.Instances, report.SkippedInstances));
        Assert.Equal(22, CountOf(after, swappedTwo));
        Assert.Equal(11, CountOf(after, swappedOne));
    }

    [Fact]
    public void AFunctionOfTheRestoredStateIsDeclaredEvenWhenItWasQueriedBeforeTheRestore()
    {
        var before = Holders(Quest);
        Call(before, Quest, "GoBusy");
        var sections = Saved(before);

        var after = Holders(Quest);
        Assert.False(Only(after).DefinesFunction("OnX"));
        Load(sections, after);

        Assert.True(Only(after).DefinesFunction("OnX"));
    }

    [Fact]
    public void AFailedScalarRestorePutsTheInstanceStateBackAndRethrows()
    {
        var before = Holders(Second);
        Call(before, Second, "Fill");
        var instances = PapyrusStateSave.Read(Saved(before))!;

        var session = Started(Scalar(), Holder(Second));
        var sessionBefore = Saved(session);
        var scripts = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[]
            {
                new QuestScriptInstance("QuestScript", new[]
                {
                    new QuestScriptProperty("Count", "Int", 99),
                    new QuestScriptProperty("Flag", "Bool", new object()),
                }),
            }),
        });

        Assert.Throws<ArgumentException>(() => SessionRestore.Apply(session, instances, scripts, null));

        Assert.Equal(sessionBefore[PapyrusStateSave.SectionName], Saved(session)[PapyrusStateSave.SectionName]);
        Assert.Null(Prop(session, Second, "Items"));
        Assert.Null(Prop(session, Second, "Count"));
    }

    [Fact]
    public void ASessionRestoreAppliesInstancesScriptsAndStagesTogether()
    {
        var before = Holders(Quest);
        Call(before, Quest, "Fill");
        var instances = PapyrusStateSave.Read(Saved(before))!;
        var session = Holders(Quest);

        var result = SessionRestore.Apply(session, instances, null, QuestStateSnapshot.Empty);

        Assert.Null(result.Instances!.Refused);
        Assert.Null(result.Scripts);
        Assert.Equal(0, result.DroppedQuestStages);
        Assert.Equal(3, Prop(session, Quest, "Count"));
    }

    [Fact]
    public void AFailedStageRestoreAfterTheScalarsWereWrittenPutsTheInstanceStateBack()
    {
        var session = Started(Scalar());
        Bump(session);
        var before = Values(session);
        var scripts = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[] { new QuestScriptInstance("QuestScript", new[] { new QuestScriptProperty("Count", "Int", 99) }) }),
        });
        var stages = new QuestStateSnapshot(new[] { new QuestStateEntry(Quest, null, 5, Array.Empty<int>()) });

        var error = Assert.Throws<FormatException>(() => SessionRestore.Apply(session, null, scripts, stages));

        Assert.False(error.Data.Contains(SessionRestore.RollbackFailureKey));
        Assert.Equal(before, Values(session));
    }

    [Fact]
    public void ARestoreThatCannotCaptureTheEarlierStateSaysSoOnTheException()
    {
        var session = Started(Scalar(), Holder(Second));
        var scripts = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[] { new QuestScriptInstance("QuestScript", new[] { new QuestScriptProperty("Count", "Int", 99) }) }),
        });
        Exception? caught = null;
        session.Host.RegisterNative(new NativeId("StateScript", "Pick"), _ =>
        {
            caught = Record.Exception(() => SessionRestore.Apply(session, null, scripts, null));
            return PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Second)));
        });

        Call(session, Second, "Grab");

        Assert.IsType<InvalidOperationException>(caught);
        Assert.True(caught!.Data.Contains(SessionRestore.RollbackUnavailableKey));
    }

    [Fact]
    public void AnInstanceRestoreThatThrowsIsInsideTheRollbackAndSaysTheEarlierStateWasUnavailable()
    {
        var session = Started(Scalar(), Holder(Second));
        var instances = PapyrusStateSave.Read(Saved(session))!;
        Exception? caught = null;
        session.Host.RegisterNative(new NativeId("StateScript", "Pick"), _ =>
        {
            caught = Record.Exception(() => SessionRestore.Apply(session, instances, null, null));
            return PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Second)));
        });

        Call(session, Second, "Grab");

        Assert.IsType<InvalidOperationException>(caught);
        Assert.Contains("restored", caught!.Message);
        Assert.True(caught.Data.Contains(SessionRestore.RollbackUnavailableKey));
    }

    private static int CapturedInstances(GameSession session) =>
        System.Text.Json.Nodes.JsonNode.Parse(Radicle(session).CaptureState().Blob)!["instances"]!.AsArray().Count;

    [Fact]
    public void ADiscardedInstanceLeavesTheRuntimeAndTheHostListAndItsIdentityCanBeAttachedAgain()
    {
        var session = Holders(Quest);
        var identity = PapyrusOwnerId.ForQuestAlias(Quest, 1);
        var instance = session.Host.Attach("StateScript", null, stateIdentity: identity);
        var host = Radicle(session);
        Assert.Equal(2, CapturedInstances(session));

        host.Discard(instance);

        Assert.True(instance.Detached);
        Assert.DoesNotContain(instance, session.Host.Instances);
        Assert.Equal(1, CapturedInstances(session));
        Assert.False(session.Host.RunEvent(instance, "Fill").Completed);
        host.Discard(instance);
        session.Host.Attach("StateScript", null, stateIdentity: identity);
        Assert.Contains(PapyrusStateSave.SectionName, PapyrusStateSave.Sections(session.Host).Keys);
    }

    [Fact]
    public void ADiscardInsideANativeCallbackIsRefusedAndLeavesTheInstanceAttached()
    {
        var session = Holders(Quest);
        var instance = session.Host.Attach("StateScript", null, stateIdentity: PapyrusOwnerId.ForQuestAlias(Quest, 1));
        Exception? refused = null;
        RegisterEngineNatives(session, () =>
        {
            try { Radicle(session).Discard(instance); }
            catch (InvalidOperationException error) { refused = error; }
            return "000999:QuestProbe.esp";
        });

        Call(session, Quest, "Grab");

        Assert.Equal("a script cannot be discarded inside a native callback", refused!.Message);
        Assert.False(instance.Detached);
        Assert.Contains(instance, session.Host.Instances);
    }

    private static readonly ScriptSpec SeedScript = TestHosts.Stub("SeedScript", null,
        "Int Property Seed Auto",
        "Function OnInit()\n  Seed = 5\nEndFunction");

    private static readonly ScriptSpec[] ScriptsWithSeed = Scripts.Append(SeedScript).ToArray();

    private static object? SeedOf(GameSession session, ScriptInstance instance) =>
        Radicle(session).Runtime.TryGetProperty(instance.HostId, "Seed", out var value) ? value : null;

    private static PapyrusOwnerId Alias(int index) => PapyrusOwnerId.ForQuestAlias(Quest, index);

    private static ScriptInstance AttachReady(GameSession session, string script, PapyrusOwnerId identity)
    {
        var instance = session.Host.Attach(script, null, stateIdentity: identity);
        Assert.True(session.Host.Initialize(instance).Completed);
        return instance;
    }

    // Invariant: guards against a save made after a load but before a skipped instance attached dropping that instance's saved values.
    [Fact]
    public void ASaveMadeBeforeASkippedInstanceAttachesStillCarriesItsState()
    {
        var first = Holders(Quest);
        SetCount(first, AttachReady(first, "StateScript", Alias(1)), 11);
        SetCount(first, AttachReady(first, "StateScript", Alias(2)), 22);
        var original = Saved(first);

        var second = Holders(Quest);
        AttachReady(second, "StateScript", Alias(1));
        Assert.Equal(1, Load(original, second).SkippedInstances);
        Assert.Single(Radicle(second).PendingStates());
        var resaved = Saved(second);

        var third = Holders(Quest);
        AttachReady(third, "StateScript", Alias(1));
        Load(resaved, third);
        var late = AttachReady(third, "StateScript", Alias(2));

        Assert.Equal(22, CountOf(third, late));
        Assert.Equal(1, Radicle(third).LateRestoredInstances);
    }

    [Fact]
    public void AnOverlongPendingListIsRefused()
    {
        var first = Holders(Quest);
        SetCount(first, AttachReady(first, "StateScript", Alias(1)), 11);
        var section = PapyrusStateSave.Deserialize(System.Text.Encoding.UTF8.GetString(Saved(first)[PapyrusStateSave.SectionName]));
        var pending = Enumerable.Repeat(section with { Pending = null }, RadicleScriptHost.MaxRetainedStates + 1).ToList();

        Assert.Throws<FormatException>(() => PapyrusStateSave.Deserialize(PapyrusStateSave.Serialize(section with { Pending = pending })));
    }

    // Invariant: guards against an instance that attaches after a load being counted as skipped and losing its saved values, or the late restore rewinding an instance the load already restored.
    [Fact]
    public void AnInstanceAttachedAfterTheLoadGetsItsSavedValuesWhileAnEarlierOneKeepsItsNewerValues()
    {
        var before = Holders(Quest);
        SetCount(before, AttachReady(before, "StateScript", Alias(1)), 11);
        SetCount(before, AttachReady(before, "StateScript", Alias(2)), 22);
        Call(before, Quest, "Fill");
        var sections = Saved(before);

        var after = Holders(Quest);
        var early = AttachReady(after, "StateScript", Alias(1));
        var report = Load(sections, after);
        Assert.Equal((2, 1), (report.Instances, report.SkippedInstances));
        Assert.Equal(11, CountOf(after, early));
        SetCount(after, early, 77);

        var late = AttachReady(after, "StateScript", Alias(2));

        Assert.Equal(22, CountOf(after, late));
        Assert.Equal(77, CountOf(after, early));
        Assert.Equal(3, Prop(after, Quest, "Count"));
        Assert.Equal(1, Radicle(after).LateRestoredInstances);
        Assert.Empty(Radicle(after).LateRestoreRefusals);
    }

    // Invariant: guards against the late restore running before OnInit, so OnInit overwrites a saved variable the load should have put back.
    [Fact]
    public void AnInstanceRestoredLateKeepsTheSavedValueOverWhatOnInitWrote()
    {
        var before = StartedWith(ScriptsWithSeed, Holder(Quest));
        var saved = AttachReady(before, "SeedScript", Alias(1));
        Assert.Equal(5, SeedOf(before, saved));
        Radicle(before).Runtime.SetVariable(saved.HostId, "::Seed_var", 99);
        var sections = Saved(before);

        var after = StartedWith(ScriptsWithSeed, Holder(Quest));
        Assert.Equal(1, Load(sections, after).SkippedInstances);
        var late = AttachReady(after, "SeedScript", Alias(1));

        Assert.Equal(99, SeedOf(after, late));
        Assert.Equal(1, Radicle(after).LateRestoredInstances);
    }

    // Invariant: guards against a discarded identity that attaches again being restored a second time from the blob, undoing what the script did since.
    [Fact]
    public void ADiscardedAndReattachedIdentityIsNotRestoredASecondTime()
    {
        var before = Holders(Quest);
        SetCount(before, AttachReady(before, "StateScript", Alias(2)), 22);
        var sections = Saved(before);

        var after = Holders(Quest);
        Assert.Equal(1, Load(sections, after).SkippedInstances);
        var first = AttachReady(after, "StateScript", Alias(2));
        Assert.Equal(22, CountOf(after, first));
        Radicle(after).Discard(first);

        var again = AttachReady(after, "StateScript", Alias(2));
        SetCount(after, again, 5);
        Radicle(after).Discard(again);
        var third = AttachReady(after, "StateScript", Alias(2));

        Assert.Null(CountOf(after, third));
        Assert.Equal(1, Radicle(after).LateRestoredInstances);
    }

    // Invariant: guards against a load that skipped nothing, or a session restore that failed after one that skipped, leaving a blob behind that a later attach would be restored from.
    [Fact]
    public void ALoadThatSkippedNothingRetainsNothingAndARefusedApplyPutsBackTheEarlierPendingState()
    {
        var before = Started(Scalar(), Holder(Second));
        SetCount(before, AttachReady(before, "StateScript", Alias(2)), 22);
        var sections = Saved(before);

        var complete = Started(Scalar(), Holder(Second));
        AttachReady(complete, "StateScript", Alias(2));
        Assert.Equal(0, Load(sections, complete).SkippedInstances);
        var extra = AttachReady(complete, "StateScript", Alias(3));
        Assert.Null(CountOf(complete, extra));
        Assert.Equal(0, Radicle(complete).LateRestoredInstances);

        var skipped = Started(Scalar(), Holder(Second));
        Assert.Equal(1, Load(sections, skipped).SkippedInstances);
        var scripts = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[] { new QuestScriptInstance("QuestScript", new[] { new QuestScriptProperty("Count", "Int", new object()) }) }),
        });
        Assert.ThrowsAny<Exception>(() => SessionRestore.Apply(skipped, null, scripts, null));

        var late = AttachReady(skipped, "StateScript", Alias(2));
        Assert.Equal(22, CountOf(skipped, late));
        Assert.Equal(1, Radicle(skipped).LateRestoredInstances);
    }

    // Invariant: guards against a session restore that failed while the earlier state could not be captured leaving the retained blob behind, so a later attach is restored from a load that was refused.
    [Fact]
    public void AFailedRestoreWhoseEarlierStateCouldNotBeCapturedDropsTheRetainedState()
    {
        var before = Started(Scalar(), Holder(Second));
        SetCount(before, AttachReady(before, "StateScript", Alias(2)), 22);
        var sections = Saved(before);

        var session = Started(Scalar(), Holder(Second));
        Assert.Equal(1, Load(sections, session).SkippedInstances);
        var instances = PapyrusStateSave.Read(Saved(session))!;
        Exception? caught = null;
        session.Host.RegisterNative(new NativeId("StateScript", "Pick"), _ =>
        {
            caught = Record.Exception(() => SessionRestore.Apply(session, instances, null, null));
            return PapyrusValue.FromObject(new EngineObjectReference(PapyrusOwnerId.ForForm(Second)));
        });
        Call(session, Second, "Grab");
        Assert.True(caught!.Data.Contains(SessionRestore.RollbackUnavailableKey));

        var late = AttachReady(session, "StateScript", Alias(2));

        Assert.Null(CountOf(session, late));
        Assert.Equal(0, Radicle(session).LateRestoredInstances);
    }

    private static readonly ScriptSpec ChangedStateScript = TestHosts.Stub("StateScript", null, "Int Property Count Auto", "Int Property Items Auto");

    private GameSession ChangedSession() =>
        StartedWith(new[] { Scripts[0], Scripts[1], ChangedStateScript, Scripts[3] }, Scalar(), Holder(Second));

    // Invariant: guards against a refused load dropping the pending late restores of the load before it.
    [Fact]
    public void ARefusedLoadKeepsTheEarlierLoadsPendingLateRestore()
    {
        var source = ChangedSession();
        SetCount(source, AttachReady(source, "StateScript", Alias(2)), 22);
        var refusing = Started(Scalar(), Holder(Second));
        Call(refusing, Second, "Fill");

        var session = ChangedSession();
        Assert.Equal(1, Load(Saved(source), session).SkippedInstances);
        var refused = SessionRestore.Apply(session, PapyrusStateSave.Read(Saved(refusing)), null, null);
        Assert.NotNull(refused.Instances!.Refused);

        var late = AttachReady(session, "StateScript", Alias(2));
        Assert.Equal(22, CountOf(session, late));
        Assert.Equal(1, Radicle(session).LateRestoredInstances);
    }

    // Invariant: guards against a failed session restore forgetting an attached instance that still awaits its late restore.
    [Fact]
    public void AFailedSessionRestoreKeepsAnAttachedInstanceAwaitingItsLateRestore()
    {
        var source = Started(Scalar(), Holder(Second));
        SetCount(source, AttachReady(source, "StateScript", Alias(2)), 22);
        var session = Started(Scalar(), Holder(Second));
        Assert.Equal(1, Load(Saved(source), session).SkippedInstances);
        var waiting = session.Host.Attach("StateScript", null, stateIdentity: Alias(2));
        var scripts = new QuestScriptSnapshot(new[]
        {
            new QuestScriptEntry(Quest, new[] { new QuestScriptInstance("QuestScript", new[] { new QuestScriptProperty("Count", "Int", new object()) }) }),
        });

        Assert.ThrowsAny<Exception>(() => SessionRestore.Apply(session, null, scripts, null));
        Assert.True(session.Host.Initialize(waiting).Completed);

        Assert.Equal(22, CountOf(session, waiting));
        Assert.Equal(1, Radicle(session).LateRestoredInstances);
    }

    // Invariant: an absent instance-state section means the committed save holds no pending late restores, so none of an earlier save may survive.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadingASaveWithoutInstanceStateDropsTheRetainedLateRestore(bool withQuestState)
    {
        var source = Started(Scalar(), Holder(Second));
        SetCount(source, AttachReady(source, "StateScript", Alias(2)), 22);
        var session = Started(Scalar(), Holder(Second));
        Assert.Equal(1, Load(Saved(source), session).SkippedInstances);
        var sections = withQuestState
            ? QuestStateSave.Sections(session.QuestStages).ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, byte[]>();

        Assert.True(GameScriptSave.Restore(sections, session).Ok);

        var late = AttachReady(session, "StateScript", Alias(2));
        Assert.Null(CountOf(session, late));
        Assert.Equal(0, Radicle(session).LateRestoredInstances);
    }

    private const string Third = "000803:QuestProbe.esp";

    private GameSession SaveSession() => Started(Scalar(), Holder(Second), Holder(Third));

    private GameSession Played()
    {
        var session = SaveSession();
        Bump(session);
        Call(session, Second, "Fill");
        Call(session, Third, "GoBusy");
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 10));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetStage, 20));
        Assert.True(session.QuestStages.Apply(Quest, QuestNative.SetActive));
        Assert.True(session.QuestStages.Apply(Second, QuestNative.CompleteQuest));
        return session;
    }

    private static Dictionary<string, byte[]> AllSections(GameSession session)
    {
        var sections = QuestStateSave.Sections(session.QuestStages).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var (name, payload) in Saved(session)) sections[name] = payload;
        return sections;
    }

    private static SessionRestoreResult Reload(IReadOnlyDictionary<string, byte[]> sections, GameSession to) =>
        SessionRestore.Apply(to, PapyrusStateSave.Read(sections),
            sections.ContainsKey(QuestScriptSave.SectionName) ? QuestScriptSave.Read(sections) : null,
            sections.ContainsKey(QuestStateSave.SectionName) ? QuestStateSave.Read(sections) : null);

    private static string Fingerprint(GameSession session) =>
        string.Join("|", AllSections(session).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + Convert.ToHexString(pair.Value)));

    // Detects: a save/load that must bring back quest stages, script values and instance variables but loses or half-applies one of the three sections.
    [Fact]
    public void AFullSaveRestoresStagesScriptValuesAndInstanceVariablesIntoAFreshSessionThroughTheContainer()
    {
        var before = Played();
        var playedValues = Values(before);
        Assert.Equal(22, playedValues["Count"]);
        var file = GeSaveContainer.Write(AllSections(before));

        var after = SaveSession();
        Assert.Equal(0, after.QuestStages.StageOf(Quest));
        Assert.False(after.QuestStages.IsActive(Quest));
        Assert.Equal(QuestState.Running, after.QuestStages.StateOf(Second));
        Assert.Equal(17, Values(after)["Count"]);
        Assert.False(Only(after, Third).DefinesFunction("OnX"));

        var read = GeSaveContainer.Read(file);
        Assert.Equal(GeSaveStatus.Ok, read.Status);
        Assert.Equal(new[] { PapyrusStateSave.SectionName, QuestScriptSave.SectionName, QuestStateSave.SectionName }, read.Sections.Keys.Order(StringComparer.Ordinal));
        var result = Reload(read.Sections, after);

        Assert.Null(result.Instances!.Refused);
        Assert.Equal(0, result.Instances.SkippedInstances);
        Assert.Equal(0, result.Instances.UnboundReferences);
        Assert.Equal(0, result.DroppedQuestStages);
        var scripts = result.Scripts!;
        Assert.Equal(0, scripts.DroppedQuests + scripts.DroppedScripts + scripts.DroppedProperties + scripts.NotInSave);

        Assert.Equal(QuestState.Running, after.QuestStages.StateOf(Quest));
        Assert.Equal(20, after.QuestStages.StageOf(Quest));
        Assert.True(after.QuestStages.StageDone(Quest, 10));
        Assert.True(after.QuestStages.StageDone(Quest, 20));
        Assert.False(after.QuestStages.StageDone(Quest, 15));
        Assert.True(after.QuestStages.IsActive(Quest));
        Assert.Equal(QuestState.Completed, after.QuestStages.StateOf(Second));
        Assert.Equal(AllSections(before)[QuestStateSave.SectionName], QuestStateSave.Sections(after.QuestStages)[QuestStateSave.SectionName]);

        Assert.Equal(playedValues, Values(after));
        Assert.Equal(3, Prop(after, Second, "Count"));
        Assert.Equal(new object?[] { 4, 5, 6 }, ((IEnumerable<object?>)Prop(after, Second, "Items")!).ToArray());
        Assert.Equal(9, Call(after, Second, "ReadA").Integer);
        Assert.Equal("nine", Call(after, Second, "ReadB").Text);
        Assert.Equal(6, Call(after, Second, "ReadItem", PapyrusValue.FromInt(2)).Integer);
        Assert.True(Only(after, Third).DefinesFunction("OnX"));
        Bump(after);
        Assert.Equal(27, Values(after)["Count"]);
    }

    // Detects: a damaged quest-bearing save that must be refused whole but hands partial sections to the session.
    [Fact]
    public void AQuestBearingSaveThatIsTruncatedOrTamperedOrNewerIsRefusedWithNoSections()
    {
        var file = GeSaveContainer.Write(AllSections(Played()));
        Assert.Equal(GeSaveStatus.Ok, GeSaveContainer.Read(file).Status);

        foreach (var cut in new[] { 0, 7, 39, file.Length / 2, file.Length - 1 })
        {
            var truncated = GeSaveContainer.Read(file[..cut]);
            Assert.Equal(GeSaveStatus.Corrupt, truncated.Status);
            Assert.Empty(truncated.Sections);
        }

        var flipped = (byte[])file.Clone();
        flipped[file.Length / 2] ^= 0x01;
        var tampered = GeSaveContainer.Read(flipped);
        Assert.Equal(GeSaveStatus.Corrupt, tampered.Status);
        Assert.Empty(tampered.Sections);

        foreach (var (version, status) in new[] { ((byte)2, GeSaveStatus.UnsupportedVersion), ((byte)0, GeSaveStatus.Corrupt) })
        {
            var body = file[..^32];
            body[4] = version;
            var read = GeSaveContainer.Read(body.Concat(System.Security.Cryptography.SHA256.HashData(body)).ToArray());
            Assert.Equal(status, read.Status);
            Assert.Empty(read.Sections);
        }
    }

    // Detects: a section reader that must refuse a newer or cut-short section.
    [Theory]
    [InlineData(QuestStateSave.SectionName)]
    [InlineData(QuestScriptSave.SectionName)]
    [InlineData(PapyrusStateSave.SectionName)]
    public void ASectionOfAnotherVersionOrCutShortRefusesTheLoadAndLeavesTheLiveSessionAsItWas(string section)
    {
        var sections = GeSaveContainer.Read(GeSaveContainer.Write(AllSections(Played()))).Sections;
        var text = Encoding.UTF8.GetString(sections[section]);
        Assert.Contains("\"version\": 1", text);
        var after = SaveSession();
        var untouched = Fingerprint(after);

        foreach (var change in new Func<string, string>[] { saved => saved.Replace("\"version\": 1", "\"version\": 2"), saved => saved[..^10] })
        {
            var damaged = new Dictionary<string, byte[]>(sections) { [section] = Encoding.UTF8.GetBytes(change(text)) };
            Assert.Throws<FormatException>(() => Reload(damaged, after));
            Assert.Equal(untouched, Fingerprint(after));
            Assert.Equal(17, Values(after)["Count"]);
        }
    }
}
