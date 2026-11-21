using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record SwfListRow(string Text, bool Selected);

public sealed record SwfListWindow(IReadOnlyList<SwfListRow> Rows, bool MoreAbove, bool MoreBelow);

public sealed class SwfBindings
{
    public HashSet<string> Hide { get; } = new();
    public HashSet<string> Headers { get; } = new();
    public Dictionary<string, string> Text { get; } = new();
    public Dictionary<string, double> FillX { get; } = new();
    public Dictionary<string, IReadOnlyList<SwfListRow>> Lists { get; } = new();

    public const int ListRows = 9;
    public const double HeaderArrowGap = 10;

    public const string HeaderText = "/textField";
    public const string HeaderRightArrow = "/RightArrow";

    public static double HeaderArrowX(double textX, double textWidth, double fieldWidth) =>
        textX + Math.Clamp(textWidth, 0, Math.Max(0, fieldWidth)) + HeaderArrowGap;

    public static SwfListWindow Window(IReadOnlyList<SwfListRow> rows, int visible = ListRows)
    {
        var selected = -1;
        for (var i = 0; i < rows.Count && selected < 0; i++)
            if (rows[i].Selected)
                selected = i;
        var first = Math.Max(0, Math.Min(selected - visible + 1, rows.Count - visible));
        return new SwfListWindow(rows.Skip(first).Take(visible).ToList(), first > 0, first + visible < rows.Count);
    }

    public static string Clean(string s) =>
        s.StartsWith('$') ? Regex.Replace(s[1..].Replace('_', ' '), @"\s+", " ").Trim() : s;

    public static string Number(int n, int digits = 0) => n.ToString(digits > 0 ? "D" + digits : "D", CultureInfo.InvariantCulture);
}
