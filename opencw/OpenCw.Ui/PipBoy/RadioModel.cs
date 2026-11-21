using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record RadioStation(string Id, string Name, string Description = "", bool Enabled = true, double? Frequency = null);

public sealed record RadioTape(string Id, string Title, int Part = 0, int Parts = 0);

public sealed record RadioProgress(int Track, int TrackCount, double ElapsedSeconds);

public enum RadioEventKind
{
    Play,
    Stop,
    Mute,
    Unmute,
    StopTape,
}

public sealed record RadioEvent(RadioEventKind Kind, string? StationId = null, string? TapeId = null);

public sealed class RadioModel : IPipBoyTabSource
{
    public const string TapePrefix = "tape:";
    public const string Unavailable = "That station is not available.";

    private readonly List<RadioStation> _stations;
    private readonly List<RadioStation> _rows = new();

    public RadioModel(IEnumerable<RadioStation> stations)
    {
        _stations = stations.ToList();
        Arrange();
        View = new MenuList<RadioStation>(_rows, s => s.Id, s => s.Name + " " + s.Description) { Wrap = true };
    }

    public MenuList<RadioStation> View { get; }

    public PipBoyTab Tab => PipBoyTab.Radio;

    public string? CurrentKey => View.Current?.Id;

    public string? PlayingId { get; private set; }

    public RadioStation? Playing => PlayingId is null ? null : _stations.Find(s => s.Id == PlayingId);

    public RadioTape? Tape { get; private set; }

    public RadioProgress? Progress { get; private set; }

    public string? Error { get; private set; }

    public string? NowPlayingText { get; private set; }

    public bool Muted { get; private set; }

    public IReadOnlyList<RadioStation> Stations => _stations;

    public bool IsTapeRow(RadioStation row) => Tape is { } tape && row.Id == TapePrefix + tape.Id;

    public IReadOnlyList<string> NowPlayingLines
    {
        get
        {
            var lines = new List<string>();
            if (Tape is { } tape)
            {
                lines.Add("HOLOTAPE");
                lines.Add(tape.Title);
                if (tape.Parts > 0) lines.Add(string.Create(CultureInfo.InvariantCulture, $"PART {tape.Part} / {tape.Parts}"));
            }
            else if (Playing is { } station)
            {
                lines.Add("NOW PLAYING");
                lines.Add(station.Name);
                if (station.Frequency is { } frequency && double.IsFinite(frequency))
                    lines.Add("FREQ " + frequency.ToString("0.0", CultureInfo.InvariantCulture));
                if (Progress is { TrackCount: > 0 } progress)
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"TRACK {progress.Track} / {progress.TrackCount}"));
                if (NowPlayingText is { Length: > 0 } text) lines.Add(text);
            }
            else
            {
                lines.Add("RADIO OFF");
                return lines;
            }
            if (Progress is { } elapsed && double.IsFinite(elapsed.ElapsedSeconds) && elapsed.ElapsedSeconds >= 0)
            {
                var seconds = (int)elapsed.ElapsedSeconds;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}"));
            }
            return lines;
        }
    }

    public void SetSearch(string? query) => View.SetSearch(query);

    public RadioEvent? Play(string id)
    {
        var station = _stations.Find(s => s.Id == id);
        if (station is null || PlayingId == id) return null;
        if (!station.Enabled)
        {
            Error = Unavailable;
            return null;
        }
        PlayingId = id;
        NowPlayingText = null;
        Progress = null;
        Error = null;
        return new RadioEvent(RadioEventKind.Play, id);
    }

    public RadioEvent? PlayCurrent()
    {
        if (View.Current is not { } row) return null;
        if (IsTapeRow(row)) return StopTape();
        return PlayingId == row.Id ? Stop() : Play(row.Id);
    }

    public RadioEvent? Stop()
    {
        if (PlayingId is null) return null;
        var id = PlayingId;
        PlayingId = null;
        NowPlayingText = null;
        Progress = null;
        Error = null;
        return new RadioEvent(RadioEventKind.Stop, id);
    }

    public RadioEvent? StopTape() =>
        Tape is { } tape ? new RadioEvent(RadioEventKind.StopTape, null, tape.Id) : null;

    public RadioEvent? Next() => Step(1);

    public RadioEvent? Prev() => Step(-1);

    public RadioEvent? ToggleMute() => SetMuted(!Muted);

    public RadioEvent? SetMuted(bool muted)
    {
        if (Muted == muted) return null;
        Muted = muted;
        return new RadioEvent(muted ? RadioEventKind.Mute : RadioEventKind.Unmute, PlayingId);
    }

    public void ClearError() => Error = null;

    public void SetNowPlaying(string? text) => NowPlayingText = PlayingId is null ? null : text;

    public void SetProgress(RadioProgress? progress) => Progress = progress;

    public void SetTape(RadioTape? tape)
    {
        if (Tape?.Id != tape?.Id) Progress = null;
        Tape = tape;
        Rebuild();
    }

    public void Upsert(RadioStation station)
    {
        var index = _stations.FindIndex(s => s.Id == station.Id);
        if (index >= 0) _stations[index] = station;
        else _stations.Add(station);
        Rebuild();
    }

    public bool Remove(string id)
    {
        if (PlayingId == id) Stop();
        var removed = _stations.RemoveAll(s => s.Id == id) > 0;
        if (removed) Rebuild();
        return removed;
    }

    public bool Select(string key)
    {
        var station = _stations.Find(s => s.Id == key);
        if (station is null) return false;
        if (!View.Visible.Contains(station)) View.SetSearch(null);
        return View.SelectVisible(View.Visible.ToList().FindIndex(s => s.Id == key));
    }

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens) =>
        _stations.Where(s => ListSearch.Matches(s.Name + " " + s.Description, tokens))
                 .Select(s => new PipBoySearchHit(PipBoyTab.Radio, s.Id, s.Name, s.Description))
                 .ToArray();

    private IEnumerable<RadioStation> Ordered() =>
        _stations.OrderBy(s => !s.Enabled)
                 .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(s => s.Id, StringComparer.Ordinal);

    private void Arrange()
    {
        _rows.Clear();
        if (Tape is { } tape) _rows.Add(new RadioStation(TapePrefix + tape.Id, tape.Title, "Holotape"));
        _rows.AddRange(Ordered());
    }

    private void Rebuild()
    {
        Arrange();
        View.Refresh();
    }

    private RadioEvent? Step(int delta)
    {
        var order = Ordered().Where(s => s.Enabled).ToList();
        if (order.Count == 0) return null;
        var index = order.FindIndex(s => s.Id == PlayingId);
        var next = index < 0
            ? (delta >= 0 ? 0 : order.Count - 1)
            : ((index + delta) % order.Count + order.Count) % order.Count;
        return Play(order[next].Id);
    }
}
