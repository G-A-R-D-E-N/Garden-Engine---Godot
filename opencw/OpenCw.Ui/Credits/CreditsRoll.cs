using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Credits;

public sealed record CreditsLine(string Text, double Top, double Height, double Opacity, bool Visible);

public sealed class CreditsRoll
{
    public const double Speed = 70.0;
    public const double WheelStep = 30.0;
    public const double BlankHeight = 15.0;
    public const double EndEdge = -25.0;
    public const double FadeBand = 60.0;
    public const double FadeInset = 15.0;

    private readonly string[] _texts;
    private readonly double[] _heights;
    private double _position;
    private double _area;

    public CreditsRoll(IReadOnlyList<string> lines, Func<string, double> measure, double areaHeight)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(measure);
        _texts = new string[lines.Count];
        _heights = new double[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            _texts[i] = lines[i] ?? string.Empty;
            var height = _texts[i].Length == 0 ? BlankHeight : measure(_texts[i]);
            _heights[i] = double.IsFinite(height) && height > 0 ? height : BlankHeight;
        }
        _area = double.IsFinite(areaHeight) && areaHeight > 0 ? areaHeight : 0;
    }

    public double Position => _position;

    public double AreaHeight
    {
        get => _area;
        set => _area = double.IsFinite(value) && value > 0 ? value : _area;
    }

    public int Count => _texts.Length;

    public bool IsFinished => LastBottom() <= EndEdge;

    public IReadOnlyList<CreditsLine> Lines
    {
        get
        {
            var lines = new List<CreditsLine>(_texts.Length);
            var top = _area - _position;
            for (var i = 0; i < _texts.Length; i++)
            {
                var bottom = top + _heights[i];
                lines.Add(new CreditsLine(_texts[i], top, _heights[i], OpacityOf(bottom), bottom > EndEdge && top < _area));
                top = bottom;
            }
            return lines;
        }
    }

    public void Advance(double seconds)
    {
        if (IsFinished || !double.IsFinite(seconds) || seconds <= 0) return;
        _position += Speed * seconds;
    }

    public void Wheel(double notches, double factor = 1.0)
    {
        if (!double.IsFinite(notches) || !double.IsFinite(factor)) return;
        var next = _position + notches * WheelStep * factor;
        _position = notches < 0 ? Math.Max(0, next) : next;
    }

    public void Restart() => _position = 0;

    public double OpacityOf(double bottom)
    {
        var fadeStart = _area - FadeInset;
        return Math.Clamp((fadeStart - bottom) / FadeBand, 0.0, 1.0);
    }

    private double LastBottom()
    {
        if (_texts.Length == 0) return double.NegativeInfinity;
        var total = 0.0;
        foreach (var height in _heights) total += height;
        return _area - _position + total;
    }
}
