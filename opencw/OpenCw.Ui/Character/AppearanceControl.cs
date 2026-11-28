using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Character;

public sealed record AppearanceControl(string Id, string Label, string Category, double Min, double Max, double Default,
                                       double Step = 0, string? MirrorId = null, IReadOnlyList<string>? Options = null)
{
    public bool IsDiscrete => Options is not null;

    public static AppearanceControl Slider(string id, string label, string category, double min, double max,
                                           double defaultValue, double step = 0, string? mirrorId = null) =>
        new(id, label, category, min, max, defaultValue, step, mirrorId);

    public static AppearanceControl Choice(string id, string label, string category, IReadOnlyList<string> options,
                                           int defaultIndex = 0, string? mirrorId = null) =>
        new(id, label, category, 0, options.Count - 1, defaultIndex, 1, mirrorId, options);

    public double Normalize(double value)
    {
        if (double.IsNaN(value)) return Default;
        var v = Math.Clamp(value, Min, Max);
        if (Step > 0) v = Math.Clamp(Min + Math.Round((v - Min) / Step, MidpointRounding.AwayFromZero) * Step, Min, Max);
        return v;
    }
}

public interface IAppearancePresetStore
{
    IReadOnlyList<string> Names();

    string? Load(string name);

    bool Save(string name, string json);

    bool Delete(string name);
}

public sealed class InMemoryAppearancePresetStore : IAppearancePresetStore
{
    private readonly Dictionary<string, string> _presets = new();

    public IReadOnlyList<string> Names() => _presets.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public string? Load(string name) => _presets.TryGetValue(name, out var json) ? json : null;

    public bool Save(string name, string json)
    {
        _presets[name] = json;
        return true;
    }

    public bool Delete(string name) => _presets.Remove(name);
}
