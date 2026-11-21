using System;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class RadioListView : PipBoyListView<RadioStation>
{
    private readonly RadioModel _radio;

    public RadioListView(RadioModel radio)
        : base(radio.View)
    {
        _radio = radio;
    }

    public event Action<RadioEvent>? Command;

    public RadioModel Radio => _radio;

    public override string Summary
    {
        get
        {
            if (_radio.Tape is { } tape) return "Playing holotape " + tape.Title;
            if (_radio.Playing is not { } station) return "Radio off";
            var text = "Playing " + station.Name;
            if (_radio.NowPlayingText is { Length: > 0 } now) text += " - " + now;
            return _radio.Muted ? text + " (muted)" : text;
        }
    }

    protected override string AcceptLabel =>
        Selected is { } row && _radio.IsTapeRow(row) ? "Stop tape" : Selected?.Id == _radio.PlayingId ? "Stop" : "Play";

    protected override string TextOf(RadioStation item) =>
        _radio.IsTapeRow(item) || item.Id == _radio.PlayingId ? item.Name + " (playing)"
        : item.Enabled ? item.Name
        : item.Name + " (unavailable)";

    protected override string DetailOf(RadioStation item) => item.Description;

    protected override bool OnAccept(RadioStation item) => Emit(_radio.PlayCurrent());

    protected override void SelectionChanged() => _radio.ClearError();

    public bool Next() => Emit(_radio.Next());

    public bool Prev() => Emit(_radio.Prev());

    public bool StopTape() => Emit(_radio.StopTape());

    public bool ToggleMute() => Emit(_radio.ToggleMute());

    private bool Emit(RadioEvent? result)
    {
        if (result is not null) Command?.Invoke(result);
        Refresh();
        return result is not null;
    }
}
