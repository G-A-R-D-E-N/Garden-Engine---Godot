using OpenCommonwealth.Services.Ui.Dialogs;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class DialogueArt
{
    public const string Movie = "Interface/DialogueMenu.swf";
    public const string Root = "Menu_mc/FadeHolder_mc/";
    public const string ButtonTail = "/Holder/Button_mc";

    private static readonly string[] Names = ["Positive", "Negative", "Neutral", "Question"];
    private static readonly char[] KeyGlyphs = ['V', 'U', 'T', 'W'];
    private static readonly char[] PadGlyphs = ['A', 'B', 'C', 'D'];

    public static IEnumerable<char> AllGlyphs => KeyGlyphs.Concat(PadGlyphs);
    private static readonly string[] KeyWords = ["Down", "Right", "Left", "Up"];
    private static readonly string[] PadWords = ["A", "B", "X", "Y"];

    public const string Subtitle = "BottomCenterGroup_mc/SubtitleText_mc";
    public static readonly string[] SubtitleFields = [Subtitle, Subtitle + "/SpeakerName_tf", Subtitle + "/SubtitleText_tf"];
    public const string Success = "speech_ok";
    public const string SuccessSymbol = "DialogueMenu_fla.SpeechChallenge_Anim_8";
    public const int SuccessFrame = 35;
    public const string Challenge = Root + "SpeechChallengeAnim_mc";

    private static readonly string[] HudHidden =
    {
        "SafeRect_mc", "RightMeters_mc", "LeftMeters_mc", "CenterGroup_mc", "TopRightGroup_mc", "TopCenterGroup_mc", "HUDNotificationsGroup_mc",
        "FloatingQuestMarkerBase", "BottomCenterGroup_mc/CompassWidget_mc", "BottomCenterGroup_mc/CritMeter_mc", "BottomCenterGroup_mc/PerkVaultBoy_mc",
    };

    public static IEnumerable<SwfTemplate> Templates()
    {
        yield return new SwfTemplate(Success, SuccessSymbol, null, SuccessFrame);
    }

    public static SwfBindings Subtitles(bool visible, string speaker, string line)
    {
        var b = new SwfBindings();
        foreach (var path in HudHidden)
            b.Hide.Add(path);
        if (!visible)
        {
            b.Hide.Add(Subtitle);
            return b;
        }
        foreach (var path in SubtitleFields)
            b.Show.Add(path);
        b.Text[Subtitle + "/SpeakerName_tf"] = speaker;
        b.Text[Subtitle + "/SubtitleText_tf"] = line;
        return b;
    }

    public static string SlotPath(int slot) => Root + Names[slot];

    public static string ButtonPath(int slot) => SlotPath(slot) + ButtonTail;

    public static string IconTextPath(int slot) => ButtonPath(slot) + "/IconHolderInstance/IconAnimInstance/Icon_tf";

    public static bool RightJustified(int slot) => slot >= 2;

    public static SwfBindings Build(DialogueSession session, bool controller, bool bannerArt = true)
    {
        var b = new SwfBindings();
        if (bannerArt && session.State == DialogueState.ChallengeResult && session.ChallengeSucceeded)
        {
            b.Show.Add(Challenge);
            b.Sprites.Add(new SwfSprite(Success, Challenge, 0, 0, Math.Clamp(session.BannerAlpha, 0, 1)));
        }
        else
            b.Hide.Add(Challenge);
        for (var i = 0; i < DialogueSession.SlotCount; i++)
        {
            var view = session.Slot(i);
            if (!view.Visible || view.Choice is not { } choice)
            {
                b.Hide.Add(SlotPath(i));
                continue;
            }
            b.Hide.Add(ButtonPath(i) + "/SecondaryIconHolderInstance");
            b.Hide.Add(IconTextPath(i));
            b.Alpha[SlotPath(i)] = Math.Clamp(view.Alpha, 0, 1);
            var text = (choice.Tag.Length > 0 ? choice.Tag + " " + choice.Text : choice.Text).ToUpperInvariant();
            b.Text[ButtonPath(i) + "/textField_tf"] = text;
            b.Hints.Add(new SwfHint(ButtonPath(i), RightJustified(i), text, (controller ? PadGlyphs : KeyGlyphs)[i], view.Focused, (controller ? PadWords : KeyWords)[i]));
        }
        return b;
    }
}
