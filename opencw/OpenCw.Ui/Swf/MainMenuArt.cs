namespace OpenCommonwealth.Services.Ui.Swf;

public sealed record MainMenuRow(string Label, bool Enabled, bool Selected);

public static class MainMenuArt
{
    public const string Movie = "Interface/MainMenu.swf";
    public const string Root = "Menu_mc/";
    public const string Panel = Root + "MainPanel_mc";
    public const string List = Panel + "/List_mc";

    public const double OriginX = 303;
    public const double OriginY = 302;
    public const double Pitch = 29.2;
    public const double RowWidth = 134.12;
    public const double RowHeight = 28;
    public const double TextX = 12;
    public const double TextY = 2.6;
    public const double TextWidth = 121;
    public const double TextHeight = 22.8;
    public const int FontSize = 19;
    public const double NoticeY = 9 * Pitch + 8;
    public const double NoticeWidth = 300;

    public const uint Enabled = 0xFFFFFF;
    public const uint Selected = 0x000000;
    public const uint DisabledRest = 4473924;
    public const uint DisabledSelected = 2236962;
    public const double DisabledBarAlpha = 0.35;

    public static readonly string[] Panels =
    [
        "GamerIcon_mc", "GamerTag_mc", "SplashScreenHolder_mc", "VersionText_tf", "ColorReference_mc", "ButtonHintBar_mc",
        "BackgroundAndBrackets_mc", "LoginHolder_mc", "ModManager_mc", "DLCPanel_mc", "DLCImageSizer_mc", "ConfirmPanel_mc",
        "SettingsPanel_mc", "BackgroundAndBracketsInstalledContentTopic_mc", "InstalledContentPanel_mc",
        "BackgroundAndBracketsHelpTopic_mc", "HelpPanel_mc", "ControlsPanel_mc", "RemapPrompt_mc", "OptionsPanel_mc",
        "SaveLoadHolder_mc", "CharacterSelectList_mc", "MessageOfTheDay_mc", "Upsell_mc", "BlackLoading_mc", "Spinner_mc",
    ];

    public const string Logo = "BethesdaLogo_mc";

    public static readonly string[] Shown = ["MainPanel_mc", Logo];

    public static string Label(string text) => text.ToUpperInvariant();

    public static (double X, double Y, double W, double H) RowRect(int visibleRow) =>
        (OriginX, OriginY + visibleRow * Pitch, RowWidth, RowHeight);

    public static SwfListWindow Window(SwfScroll? scroll, IReadOnlyList<MainMenuRow> rows) =>
        SwfScroll.Window(scroll, "main", rows.Select(r => new SwfListRow(r.Label, r.Selected)).ToList());

    public static SwfBindings Build(IReadOnlyList<MainMenuRow> rows, bool pause, string notice = "", SwfScroll? scroll = null)
    {
        var b = new SwfBindings();
        foreach (var panel in Panels)
            b.Hide.Add(Root + panel);
        if (pause)
            b.Hide.Add(Root + Logo);
        else
            b.Show.Add(Root + Logo);
        var window = Window(scroll, rows);
        for (var i = 0; i < window.Rows.Count; i++)
        {
            var row = rows[window.First + i];
            var y = i * Pitch;
            if (row.Selected)
                b.Sprites.Add(new SwfSprite("", List, 0, y, row.Enabled ? 1 : DisabledBarAlpha, null, new SwfSpriteBar(0, 0, RowWidth, RowHeight, 1, 0)));
            var color = row.Enabled ? (row.Selected ? Selected : Enabled) : (row.Selected ? DisabledSelected : DisabledRest);
            b.Sprites.Add(new SwfSprite("", List, 0, y, 1, new SwfSpriteText(Label(row.Label), TextX, TextY, TextWidth, TextHeight, FontSize, false, color, SwfAlign.Left, true)));
        }
        if (notice.Length > 0)
            b.Sprites.Add(new SwfSprite("", List, 0, NoticeY, 1, new SwfSpriteText(notice, 0, 0, NoticeWidth, 60, 14, false, Enabled, SwfAlign.Left, false, true)));
        return b;
    }
}
