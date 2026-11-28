using OpenCommonwealth.Services.Audio;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ExteriorPlayWiringTests
{
    [Fact]
    public void StartupEnsuresPapyrusBeforeStreaming()
    {
        var calls = new List<string>();
        ExteriorAudioLifecycle<object>.Start(() => calls.Add("papyrus"), () => calls.Add("streaming"));
        Assert.Equal(new[] { "papyrus", "streaming" }, calls);
    }

    [Fact]
    public void ExteriorAttachmentRetriesUntilAvailableAndDoesNotDuplicate()
    {
        var audio = new ExteriorAudioLifecycle<object>(_ => { });
        var instance = new object();
        var attempts = 0;
        object? Attach() => ++attempts == 1 ? null : instance;
        audio.Attach(Attach);
        audio.Attach(Attach);
        audio.Attach(Attach);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void FailedInteriorLoadKeepsExteriorAndSuccessfulLoadReleasesIt()
    {
        var calls = new List<string>();
        var exterior = new object();
        var interior = new object();
        var audio = new ExteriorAudioLifecycle<object>(_ => calls.Add("release"));
        audio.Attach(() => exterior);
        Assert.Null(audio.LoadInterior<object>(() => { calls.Add("failed load"); return null; }));
        Assert.Same(interior, audio.LoadInterior(() => { calls.Add("loaded"); return interior; }));
        audio.Attach(() => { calls.Add("return"); return exterior; });
        Assert.Equal(new[] { "failed load", "loaded", "release", "return" }, calls);
    }

    [Fact]
    public void FinishedEventReleasesPreviousPlayerAndStartsAnotherPlayableTrack()
    {
        var players = new List<Player>();
        var attempts = new List<int>();
        var lifecycle = new MusicLifecycle<Player>(2, index =>
        {
            attempts.Add(index);
            if (attempts.Count == 1) return null;
            var player = new Player();
            players.Add(player);
            return player;
        }, (player, finished) => player.Finished += finished,
           (player, finished) => player.Finished -= finished,
           player => player.Released = true);
        lifecycle.Start(0);
        Assert.Equal(new[] { 0, 1 }, attempts);
        players[0].Finish();
        Assert.True(players[0].Released);
        Assert.Equal(2, players.Count);
        players[0].Finish();
        Assert.Equal(2, players.Count);
    }

    [Fact]
    public void FailedNextTrackDoesNotEndContinuousPlayback()
    {
        var attempts = new List<int>();
        var players = new List<Player>();
        var lifecycle = new MusicLifecycle<Player>(2, index =>
        {
            attempts.Add(index);
            if (attempts.Count == 2) return null;
            var player = new Player();
            players.Add(player);
            return player;
        }, (player, finished) => player.Finished += finished,
           (player, finished) => player.Finished -= finished,
           player => player.Released = true);
        lifecycle.Start(0);
        players[0].Finish();
        Assert.Equal(new[] { 0, 1, 0 }, attempts);
        Assert.True(players[0].Released);
        Assert.Equal(2, players.Count);
    }

    [Fact]
    public void ExhaustedMusicReportsInitialAndLaterFailures()
    {
        var warnings = 0;
        var player = new Player();
        var canPlay = false;
        var lifecycle = new MusicLifecycle<Player>(2, _ => canPlay ? player : null,
            (current, finished) => current.Finished += finished,
            (current, finished) => current.Finished -= finished,
            current => current.Released = true, () => warnings++);
        lifecycle.Start(0);
        Assert.Equal(1, warnings);
        canPlay = true;
        lifecycle.Start(0);
        Assert.Equal(1, warnings);
        canPlay = false;
        player.Finish();
        Assert.Equal(2, warnings);
        Assert.True(player.Released);
    }

    [Fact]
    public void StopReleasesCurrentPlayerAndIgnoresLaterFinish()
    {
        var players = new List<Player>();
        var lifecycle = new MusicLifecycle<Player>(2, _ =>
        {
            var player = new Player();
            players.Add(player);
            return player;
        }, (player, finished) => player.Finished += finished,
           (player, finished) => player.Finished -= finished,
           player => player.Released = true);
        lifecycle.Start(0);
        lifecycle.Stop();
        lifecycle.Stop();
        Assert.True(players[0].Released);
        players[0].Finish();
        Assert.Single(players);
        lifecycle.Start(0);
        Assert.Equal(2, players.Count);
    }

    [Fact]
    public void SameFrameReattachmentCreatesFreshAudioAfterRelease()
    {
        var children = new List<object>();
        var audio = new ExteriorAudioLifecycle<object>(node => children.Remove(node));
        object Attach() { var node = new object(); children.Add(node); return node; }
        audio.Attach(Attach);
        var old = Assert.Single(children);
        audio.Clear();
        audio.Attach(Attach);
        Assert.NotSame(old, Assert.Single(children));
    }

    [Fact]
    public void ClearBeforeDeferredAttachLeavesHolderEmptyUntilAttachRuns()
    {
        var released = 0;
        var attached = 0;
        var audio = new ExteriorAudioLifecycle<object>(_ => released++);
        audio.Attach(() => { attached++; return new object(); });
        Assert.Equal(1, attached);
        audio.Clear();
        Assert.Equal(1, released);
        Assert.Equal(1, attached);
        audio.Attach(() => { attached++; return new object(); });
        Assert.Equal(2, attached);
    }

    private sealed class Player
    {
        public event Action? Finished;
        public bool Released;
        public void Finish() => Finished?.Invoke();
    }
}
