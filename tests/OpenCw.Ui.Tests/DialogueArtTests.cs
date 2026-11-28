using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using OpenCommonwealth.Services.Ui.Swf;
using Radicle.Interop;
using OpenCommonwealth.Tests;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class DialogueArtTests
{
    private static DialogueSession Choosing(params DialogueChoice[] choices) =>
        new(new DialogueNode("n", "Speaker", "", choices, 0, false, false));

    private static DialogueChoice[] Four() =>
    [
        new("Zero"), new("One", "[Tag]"), new("Two"), new("Three"),
    ];

    private static DialogueSession Waiting()
    {
        var s = Choosing(Four());
        Assert.True(s.Choose(0));
        for (var i = 0; i < 20; i++)
            s.Tick(0.1);
        return s;
    }

    [Fact]
    public void A_successful_challenge_shows_the_banner_clip_with_the_success_frame_at_the_banner_alpha()
    {
        var s = Waiting();
        Assert.True(s.PlayChallengeResult(true));
        s.Tick(0.05);
        Assert.InRange(s.BannerAlpha, 0.01, 0.99);

        var b = DialogueArt.Build(s, false);

        Assert.Contains(DialogueArt.Challenge, b.Show);
        Assert.DoesNotContain(DialogueArt.Challenge, b.Hide);
        var sprite = Assert.Single(b.Sprites);
        Assert.Equal((DialogueArt.Success, DialogueArt.Challenge, s.BannerAlpha), (sprite.Template, sprite.Parent, sprite.Alpha));
        Assert.Equal([new SwfTemplate(DialogueArt.Success, DialogueArt.SuccessSymbol, null, DialogueArt.SuccessFrame)], DialogueArt.Templates());
    }

    [Fact]
    public void Without_the_banner_template_a_successful_challenge_draws_no_sprite_and_keeps_the_clip_hidden()
    {
        var s = Waiting();
        Assert.True(s.PlayChallengeResult(true));
        s.Tick(0.05);

        var b = DialogueArt.Build(s, false, false);

        Assert.Contains(DialogueArt.Challenge, b.Hide);
        Assert.DoesNotContain(DialogueArt.Challenge, b.Show);
        Assert.Empty(b.Sprites);
    }

    [Fact]
    public void A_failed_challenge_and_the_other_states_keep_the_banner_clip_hidden_and_draw_no_sprite()
    {
        var failed = Waiting();
        Assert.True(failed.PlayChallengeResult(false));
        foreach (var s in new[] { failed, Waiting(), Choosing(Four()) })
        {
            var b = DialogueArt.Build(s, false);
            Assert.Contains(DialogueArt.Challenge, b.Hide);
            Assert.Empty(b.Sprites);
        }
    }

    [Fact]
    public void The_subtitle_widget_shows_the_speaker_and_line_and_hides_the_rest_of_the_hud()
    {
        var shown = DialogueArt.Subtitles(true, "Piper", "Hello there");

        Assert.Equal(
            [DialogueArt.Subtitle, DialogueArt.Subtitle + "/SpeakerName_tf", DialogueArt.Subtitle + "/SubtitleText_tf"],
            shown.Show);
        Assert.Equal("Piper", shown.Text[DialogueArt.Subtitle + "/SpeakerName_tf"]);
        Assert.Equal("Hello there", shown.Text[DialogueArt.Subtitle + "/SubtitleText_tf"]);
        Assert.Contains("SafeRect_mc", shown.Hide);
        Assert.Contains("BottomCenterGroup_mc/CompassWidget_mc", shown.Hide);
        Assert.DoesNotContain(DialogueArt.Subtitle, shown.Hide);

        var hidden = DialogueArt.Subtitles(false, "Piper", "Hello there");
        Assert.Contains(DialogueArt.Subtitle, hidden.Hide);
        Assert.Empty(hidden.Show);
        Assert.Empty(hidden.Text);
    }

    [Fact]
    public void Four_choices_bind_the_slot_clips_in_vanilla_order_with_upper_case_text()
    {
        var b = DialogueArt.Build(Choosing(Four()), false);

        Assert.Equal(
            [
                (DialogueArt.ButtonPath(0), "ZERO", false, 'V', "Down"),
                (DialogueArt.ButtonPath(1), "[TAG] ONE", false, 'U', "Right"),
                (DialogueArt.ButtonPath(2), "TWO", true, 'T', "Left"),
                (DialogueArt.ButtonPath(3), "THREE", true, 'W', "Up"),
            ],
            b.Hints.Select(h => (h.Path, h.Text, h.RightJustified, h.Glyph, h.Fallback)).ToArray());
        Assert.Equal("Menu_mc/FadeHolder_mc/Positive/Holder/Button_mc/textField_tf", DialogueArt.ButtonPath(0) + "/textField_tf");
        Assert.Equal("Menu_mc/FadeHolder_mc/Question/Holder/Button_mc/textField_tf", DialogueArt.ButtonPath(3) + "/textField_tf");
        Assert.Contains(DialogueArt.Root + "SpeechChallengeAnim_mc", b.Hide);
        for (var i = 0; i < 4; i++)
        {
            Assert.Contains(DialogueArt.ButtonPath(i) + "/SecondaryIconHolderInstance", b.Hide);
            Assert.Contains(DialogueArt.IconTextPath(i), b.Hide);
            Assert.DoesNotContain(DialogueArt.SlotPath(i), b.Hide);
        }
    }

    [Fact]
    public void A_controller_gets_the_pad_glyphs_and_words()
    {
        var b = DialogueArt.Build(Choosing(Four()), true);

        Assert.Equal(['A', 'B', 'C', 'D'], b.Hints.Select(h => h.Glyph).ToArray());
        Assert.Equal(["A", "B", "X", "Y"], b.Hints.Select(h => h.Fallback).ToArray());
    }

    [Fact]
    public void Missing_slots_and_the_speaking_state_hide_their_clips_and_bind_no_hint()
    {
        var two = DialogueArt.Build(Choosing(new DialogueChoice("A"), new DialogueChoice("B")), false);
        Assert.Equal(2, two.Hints.Count);
        Assert.Contains(DialogueArt.SlotPath(2), two.Hide);
        Assert.Contains(DialogueArt.SlotPath(3), two.Hide);

        var speaking = new DialogueSession(new DialogueNode("n", "Speaker", "Hello", Four(), 1000, false, false));
        var b = DialogueArt.Build(speaking, false);
        Assert.Empty(b.Hints);
        Assert.Empty(b.Text);
        for (var i = 0; i < 4; i++)
            Assert.Contains(DialogueArt.SlotPath(i), b.Hide);
    }

    [Fact]
    public void A_disabled_choice_is_dimmed_and_the_focused_one_is_flagged()
    {
        var choices = Four();
        choices[1] = choices[1] with { Enabled = false };
        var session = Choosing(choices);
        for (var t = 0; t < 100; t++)
            session.Tick(0.05);
        session.FocusSlot(2);
        var b = DialogueArt.Build(session, false);

        Assert.Equal(session.Slot(1).Alpha, b.Alpha[DialogueArt.SlotPath(1)]);
        Assert.True(b.Alpha[DialogueArt.SlotPath(1)] < 1);
        Assert.Equal(1, b.Alpha[DialogueArt.SlotPath(0)]);
        Assert.Equal([false, false, true, false], b.Hints.Select(h => h.Focused).ToArray());
    }

    [Fact]
    public void The_signature_changes_with_the_hint_text_the_focus_and_the_pad()
    {
        var plain = DialogueArt.Build(Choosing(Four()), false).Signature();
        var session = Choosing(Four());
        session.FocusSlot(1);

        Assert.NotEqual(plain, DialogueArt.Build(session, false).Signature());
        Assert.NotEqual(plain, DialogueArt.Build(Choosing(Four()), true).Signature());
        Assert.Equal(plain, DialogueArt.Build(Choosing(Four()), false).Signature());
    }

    [Fact]
    public void A_straight_and_curved_outline_becomes_a_path_with_its_bounds_as_the_view_box()
    {
        var commands = new List<SwfShapeCommandRecord>
        {
            new(SwfShapeCommandKind.Move, 0, 0, 0, 0, 0, 0, 0),
            new(SwfShapeCommandKind.StraightEdge, 2048, 0, 0, 0, 0, 0, 0),
            new(SwfShapeCommandKind.CurvedEdge, 0, 2048, 1024, 0, 0, 0, 0),
            new(SwfShapeCommandKind.StraightEdge, -2048, 0, 0, 0, 0, 0, 0),
        };

        var art = SwfFontOutlines.Outline(commands, 20480, 16384, 20)!;

        Assert.Contains("d=\"M0 0L2 0Q3 0 3 2L1 2Z\"", art.Svg);
        Assert.Contains("viewBox=\"0 0 3 2\"", art.Svg);
        Assert.Equal(3, art.Advance);
        Assert.Equal(16, art.Ascent);
        Assert.Null(SwfFontOutlines.Outline([], 20480, 0, 20));
    }

    private static byte[]? FontMovie(string entry) => Fo4Data.Read(entry);

    [Fact]
    public void The_real_controller_font_gives_a_ring_glyph_for_every_button_code()
    {
        if (FontMovie(SwfFontOutlines.FontMovies[0]) is not { } bytes)
            return;
        foreach (var font in new[] { "$Controller_Buttons_inverted", "$Controller_Buttons" })
            foreach (var code in "VUTWABCD")
            {
                var art = SwfFontOutlines.Read(bytes, font, code, 20);
                Assert.NotNull(art);
                Assert.InRange(art!.Advance, 8, 40);
            }
    }

    private static void Collect(SwfNode node, HashSet<string> into)
    {
        into.Add(node.Path);
        foreach (var child in node.Children)
            Collect(child, into);
    }

    [Fact]
    public void Every_bound_path_exists_in_the_real_dialogue_movie()
    {
        if (FontMovie(DialogueArt.Movie) is not { } bytes)
            return;
        using var stage = SwfStage.Read(bytes);
        var paths = new HashSet<string>();
        Collect(stage.Root, paths);
        var b = DialogueArt.Build(Choosing(Four()), false);
        var bound = b.Hide.Concat(b.Show).Concat(b.Text.Keys).Concat(b.Alpha.Keys)
            .Concat(b.Hints.SelectMany(h => new[] { h.Path, h.Path + "/textField_tf", h.Path + "/IconHolderInstance", h.Path + "/IconHolderInstance/IconAnimInstance" }));
        foreach (var path in bound)
            Assert.Contains(path, paths);
    }

    [Fact]
    public void The_subtitle_view_leaves_only_the_subtitle_visible_in_the_real_hud_movie()
    {
        if (FontMovie(HudArt.Movie) is not { } bytes)
            return;
        using var stage = SwfStage.Read(bytes);
        var hide = DialogueArt.Subtitles(true, "", "").Hide;
        var bottom = stage.Root.Children.Single(c => c.Path == "BottomCenterGroup_mc");

        Assert.Equal(["BottomCenterGroup_mc"], stage.Root.Children.Where(c => !c.IsMask && !hide.Contains(c.Path)).Select(c => c.Path).ToArray());
        Assert.Equal([DialogueArt.Subtitle], bottom.Children.Where(c => !c.IsMask && !hide.Contains(c.Path)).Select(c => c.Path).ToArray());
    }

    [Fact]
    public void The_real_hud_movie_marks_four_fields_wrapped_and_the_speaker_is_not_one()
    {
        if (FontMovie(HudArt.Movie) is not { } bytes)
            return;
        using var stage = SwfStage.Read(bytes);
        var wrapped = new List<string>();
        void Walk(SwfNode n)
        {
            if (n.Text is { Multiline: true, WordWrap: true })
                wrapped.Add(n.Path);
            foreach (var c in n.Children)
                Walk(c);
        }
        Walk(stage.Root);

        Assert.Contains(DialogueArt.Subtitle + "/SubtitleText_tf", wrapped);
        Assert.DoesNotContain(DialogueArt.Subtitle + "/SpeakerName_tf", wrapped);
        Assert.Equal(["ReserveCount_tf", "SubtitleText_tf", "RolloverName_tf", "TutorialText_tf"], wrapped.Select(p => p[(p.LastIndexOf('/') + 1)..]).ToArray());
    }
}
