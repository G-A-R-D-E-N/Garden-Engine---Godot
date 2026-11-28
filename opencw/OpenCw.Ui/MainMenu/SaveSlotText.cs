using System;
using System.Globalization;

namespace OpenCommonwealth.Services.Ui.MainMenu;

public static class SaveSlotText
{
    public static string Playtime(TimeSpan playtime)
    {
        var minutes = Math.Max(0L, (long)Math.Floor(playtime.TotalMinutes));
        var hours = minutes / 60;
        var days = hours / 24;
        if (days > 0)
            return FormattableString.Invariant($"{days}d {hours % 24}h {minutes % 60:00}m");
        return hours > 0
            ? FormattableString.Invariant($"{hours}h {minutes % 60:00}m")
            : FormattableString.Invariant($"{minutes}m");
    }

    public static string Timestamp(DateTimeOffset timestamp) =>
        timestamp == DateTimeOffset.MinValue ? "Unknown date" : timestamp.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public static string Clip(string text, int max)
    {
        if (max <= 0)
            return "";
        var info = new StringInfo(text);
        if (info.LengthInTextElements <= max)
            return text;
        return info.SubstringByTextElements(0, max - 1) + "…";
    }

    public static string Location(SaveSlotInfo slot) => Clip(slot.Location, 30);

    public static string CharacterName(SaveSlotInfo slot) => Clip(slot.Character, 20);

    public static string Summary(SaveSlotInfo slot) =>
        slot.Location.Length == 0
            ? FormattableString.Invariant($"Level {slot.Level}, {Playtime(slot.Playtime)}")
            : FormattableString.Invariant($"Level {slot.Level}, {Location(slot)}, {Playtime(slot.Playtime)}");

    public static string RowText(SaveSlotInfo slot) => slot.Kind switch
    {
        SaveKind.Quick => "Quick: " + slot.Name,
        SaveKind.Auto => "Auto: " + slot.Name,
        _ => slot.Name,
    };

    public static string RowDetail(SaveSlotInfo slot) => FormattableString.Invariant($"Lv {slot.Level}");
}
