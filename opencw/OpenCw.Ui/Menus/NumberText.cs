using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Menus;

internal static class NumberText
{
    internal static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
