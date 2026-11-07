using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using OpenCommonwealth.Services.Papyrus.Save;
using Xunit;

namespace OpenCw.Papyrus.Tests;

public class PapyrusSaveStatesTests
{
    private const string Base = "000800:Quests.esp";

    private static SavedThread Thread(string owner, string script, string fingerprint, int pc) =>
        new(owner, script, fingerprint, "Fragment_Stage", pc, new[] { "1", "two" });

    private static PapyrusSaveState State(params SavedThread[] threads) =>
        PapyrusSaveStates.Capture(threads,
            new[]
            {
                new SavedRegistration("placed:" + Base + "#000A00", "Timer", "", 1.5, true, "RealTime"),
                new SavedRegistration("placed:" + Base + "#000A01", "CustomEvent", "OnItemAdded", 0, false, "RealTime"),
            },
            new[] { new SavedTimer("placed:" + Base + "#000A00", "", 0.75, 3) },
            new[] { new SavedObjective("placed:" + Base + "#000A00", 10, 2) });

    private static Dictionary<string, string> Fingerprints(params (string Script, string Fingerprint)[] entries) =>
        entries.ToDictionary(entry => entry.Script, entry => entry.Fingerprint, StringComparer.Ordinal);

    [Theory]
    [InlineData("threads", "null")]
    [InlineData("threads", "42")]
    [InlineData("registrations", "null")]
    [InlineData("registrations", "42")]
    [InlineData("timers", "null")]
    [InlineData("timers", "42")]
    [InlineData("objectives", "null")]
    [InlineData("objectives", "42")]
    public void MalformedEntriesRefuseTheSaveInsteadOfSilentlyDroppingState(string collection, string invalid)
    {
        var state = JsonNode.Parse(State(Thread("owner", "QuestScript", "abc", 7)).Serialize())!;
        state[collection]!.AsArray().Insert(1, JsonNode.Parse(invalid));

        Assert.Throws<FormatException>(() => PapyrusSaveState.Deserialize(state.ToJsonString()));
    }

    [Theory]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void TimerFireCountsRoundTripWithoutDoubleConversion(long fires)
    {
        var state = State() with { Timers = new[] { new SavedTimer("owner", "OnTimer", 0.75, fires) } };

        var restored = PapyrusSaveState.Deserialize(state.Serialize());
        var report = PapyrusSaveStates.Restore(restored, Fingerprints());

        Assert.Equal(fires, Assert.Single(report.Timers).Fires);
        Assert.Equal(state.Digest(), restored.Digest());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UnsupportedSaveVersionsAreRejectedBeforeRestore(int version)
    {
        var state = State(Thread("owner", "QuestScript", "abc", 7)) with { Version = version };

        Assert.Throws<FormatException>(() => PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "abc"))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void UnsupportedSerializedVersionsAreRejected(int version)
    {
        var state = State() with { Version = version };

        Assert.Throws<FormatException>(() => PapyrusSaveState.Deserialize(state.Serialize()));
    }

    [Fact]
    public void TheStateRoundTripsThroughItsSerializedForm()
    {
        var state = State(Thread("placed:" + Base + "#000A00", "QuestScript", "abc", 7));

        var restored = PapyrusSaveState.Deserialize(state.Serialize());

        Assert.Equal(PapyrusSaveStates.CurrentVersion, restored.Version);
        Assert.Equal(state.Digest(), restored.Digest());
        Assert.Single(restored.Threads);
        Assert.Equal(7, restored.Threads[0].Pc);
        Assert.Equal(new[] { "1", "two" }, restored.Threads[0].Locals);
    }

    [Fact]
    public void AMatchingFingerprintIsRejectedUntilLiveFramesCanBeRestored()
    {
        var state = State(Thread("placed:" + Base + "#000A00", "QuestScript", "abc", 7));

        var report = PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "abc")));

        Assert.Equal(0, report.Resumed);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(ThreadRestoreState.RejectedFrameRestore, report.Threads[0].State);
        Assert.Contains("frame restoration is not implemented", report.Threads[0].Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void MatchingMetadataCannotReportAThreadResumedWithoutRestoringItsFrames(int pc)
    {
        var state = State(Thread("owner", "QuestScript", "abc", pc));
        var restored = PapyrusSaveState.Deserialize(state.Serialize());
        var report = PapyrusSaveStates.Restore(restored, Fingerprints(("QuestScript", "abc")));

        Assert.Equal(0, report.Resumed);
        Assert.Equal(1, report.Rejected);
        Assert.NotEqual(ThreadRestoreState.Resumed, Assert.Single(report.Threads).State);
        Assert.Contains("frame", report.Threads[0].Reason, StringComparison.Ordinal);
        Assert.Equal(state.Digest(), restored.Digest());
        Assert.Equal(restored.Registrations, report.Registrations);
        Assert.Equal(restored.Timers, report.Timers);
        Assert.Equal(restored.Objectives, report.Objectives);
    }

    [Fact]
    public void AChangedScriptIsRejectedAndNeverResumed()
    {
        var state = State(Thread("placed:" + Base + "#000A00", "QuestScript", "abc", 7));

        var report = PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "def")));

        Assert.Equal(0, report.Resumed);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(ThreadRestoreState.RejectedChangedScript, report.Threads[0].State);
        Assert.Contains("guessed program counter", report.Threads[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentScriptIsRejected()
    {
        var state = State(Thread("placed:" + Base + "#000A00", "GoneScript", "abc", 7));

        var report = PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "abc")));

        Assert.Equal(1, report.Rejected);
        Assert.Equal(ThreadRestoreState.RejectedUnknownScript, report.Threads[0].State);
    }

    [Fact]
    public void DistinctOwnerKeysSurviveTheMetadataRoundTrip()
    {
        var state = State(
            Thread("placed:" + Base + "#000A00", "QuestScript", "abc", 1),
            Thread("dynamic:" + Base + "#000A00#1", "QuestScript", "abc", 2));

        var restored = PapyrusSaveState.Deserialize(state.Serialize());
        var report = PapyrusSaveStates.Restore(restored, Fingerprints(("QuestScript", "abc")));

        Assert.Equal(0, report.Resumed);
        Assert.Equal(2, report.Rejected);
        Assert.All(report.Threads, thread => Assert.Equal(ThreadRestoreState.RejectedFrameRestore, thread.State));
        Assert.NotEqual(report.Threads[0].OwnerKey, report.Threads[1].OwnerKey);
        Assert.Equal(new[] { "dynamic:" + Base + "#000A00#1", "placed:" + Base + "#000A00" },
            report.Threads.Select(thread => thread.OwnerKey).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TypedRegistrationsAndTimersKeepTheirPayloads()
    {
        var state = PapyrusSaveState.Deserialize(State().Serialize());

        var timer = state.Registrations.Single(registration => registration.Kind == "Timer");
        Assert.Equal(1.5, timer.Interval);
        Assert.True(timer.Repeats);
        Assert.Equal("RealTime", timer.Clock);
        var customEvent = state.Registrations.Single(registration => registration.Kind == "CustomEvent");
        Assert.Equal("OnItemAdded", customEvent.EventName);

        var fired = Assert.Single(state.Timers);
        Assert.Equal(0.75, fired.Elapsed);
        Assert.Equal(3, fired.Fires);
    }

    [Fact]
    public void ObjectivesAndCountersSurviveAndTheReportIsDeterministic()
    {
        var state = State(Thread("placed:" + Base + "#000A00", "QuestScript", "abc", 1));

        var report = PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "abc")));
        var again = PapyrusSaveStates.Restore(state, Fingerprints(("QuestScript", "abc")));

        var objective = Assert.Single(report.Objectives);
        Assert.Equal(10, objective.Index);
        Assert.Equal(2, objective.State);
        Assert.Equal(report.Threads.Count, report.Resumed + report.Rejected);
        Assert.Equal(report.ToReport(), again.ToReport());
        Assert.Equal(report.Digest(), again.Digest());
        Assert.Equal(64, report.Digest().Length);
    }
}
