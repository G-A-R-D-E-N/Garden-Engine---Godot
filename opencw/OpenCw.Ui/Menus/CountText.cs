using System.Globalization;

namespace OpenCommonwealth.Services.Ui.Menus;

public static class CountText
{
    public static string Short(int count) =>
        count >= 10000
            ? (count / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : count.ToString(CultureInfo.InvariantCulture);

    public static string HaveNeed(string name, int have, int need) =>
        name + " " + Short(have) + "/" + Short(need) + (have < need ? " short" : string.Empty);
}
