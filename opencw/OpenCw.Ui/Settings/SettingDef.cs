using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Settings;

public enum SettingCategory
{
    Display,
    Graphics,
    Audio,
    Controls,
    Gameplay,
    Accessibility,
}

public enum SettingKind
{
    Toggle,
    Choice,
    Slider,
    Keybinding,
}

public readonly record struct SettingValue(SettingKind Kind, bool Flag, double Number, string Text)
{
    public static SettingValue Toggle(bool on) => new(SettingKind.Toggle, on, 0, "");

    public static SettingValue Choice(string id) => new(SettingKind.Choice, false, 0, id);

    public static SettingValue Slider(double value) => new(SettingKind.Slider, false, value, "");

    public static SettingValue Binding(string code) => new(SettingKind.Keybinding, false, 0, code);
}

public sealed record SettingChoice(string Id, string Label);

public sealed record SettingDef(string Id, SettingCategory Category, string Label, SettingKind Kind, SettingValue Default)
{
    public string Keywords { get; init; } = "";

    public string Description { get; init; } = "";

    public IReadOnlyList<SettingChoice> Choices { get; init; } = Array.Empty<SettingChoice>();

    public double Min { get; init; }

    public double Max { get; init; }

    public double Step { get; init; }

    public bool Risky { get; init; }

    public string SearchText => $"{Label} {Keywords} {Description} {Category}";

    public static SettingDef Toggle(string id, SettingCategory category, string label, bool on) =>
        new(id, category, label, SettingKind.Toggle, SettingValue.Toggle(on));

    public static SettingDef Choice(string id, SettingCategory category, string label, string defaultChoice,
                                    params SettingChoice[] choices) =>
        new(id, category, label, SettingKind.Choice, SettingValue.Choice(defaultChoice)) { Choices = choices };

    public static SettingDef Slider(string id, SettingCategory category, string label, double value,
                                    double min, double max, double step) =>
        new(id, category, label, SettingKind.Slider, SettingValue.Slider(value)) { Min = min, Max = max, Step = step };

    public bool TryNormalize(SettingValue value, out SettingValue normalized)
    {
        normalized = value;
        if (value.Kind != Kind) return false;
        switch (Kind)
        {
            case SettingKind.Slider:
                if (!double.IsFinite(value.Number)) return false;
                var n = Math.Clamp(value.Number, Min, Max);
                if (Step > 0) n = Math.Clamp(Min + Math.Round((n - Min) / Step, MidpointRounding.AwayFromZero) * Step, Min, Max);
                normalized = SettingValue.Slider(Math.Round(n, 6));
                return true;
            case SettingKind.Choice:
                if (value.Text is null || !Choices.Any(c => c.Id == value.Text)) return false;
                normalized = SettingValue.Choice(value.Text);
                return true;
            case SettingKind.Keybinding:
                normalized = SettingValue.Binding(value.Text ?? "");
                return true;
            default:
                normalized = SettingValue.Toggle(value.Flag);
                return true;
        }
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Setting id is empty.");
        if (Kind == SettingKind.Slider && !(Max > Min)) throw new ArgumentException($"Slider {Id} needs Max > Min.");
        if (Kind == SettingKind.Choice && Choices.Count == 0) throw new ArgumentException($"Choice {Id} has no choices.");
        if (!TryNormalize(Default, out var fixedDefault) || fixedDefault != Default)
            throw new ArgumentException($"Default of {Id} is not a valid value for it.");
    }
}
