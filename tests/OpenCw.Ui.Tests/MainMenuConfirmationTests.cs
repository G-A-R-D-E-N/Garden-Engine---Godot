using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public sealed class MainMenuConfirmationTests
{
    sealed class Saves : ISaveSource
    {
        public List<SaveSlotInfo> Slots { get; } = new();

        public IReadOnlyList<SaveSlotInfo> List() => Slots.ToList();

        public bool Delete(string id) => Slots.RemoveAll(s => s.Id == id) > 0;
    }

    static SaveSlotInfo Slot(string id, string location = "Sanctuary", string character = "Ann", TimeSpan? play = null) =>
        new(id, id, character, 7, play ?? TimeSpan.FromMinutes(95), location, new DateTimeOffset(2026, 2, 3, 0, 0, 0, TimeSpan.Zero), "");

    static MainMenuEntry Entry(MainMenuModel model, MainMenuAction action) => model.Entries.Single(e => e.Action == action);

    [Fact]
    public void With_no_saves_new_game_starts_at_once_and_only_quit_asks()
    {
        var model = new MainMenuModel(new Saves());

        Assert.False(Entry(model, MainMenuAction.NewGame).NeedsConfirmation);
        Assert.False(Entry(model, MainMenuAction.Continue).NeedsConfirmation);
        Assert.True(Entry(model, MainMenuAction.Quit).NeedsConfirmation);
        Assert.NotEqual("", Entry(model, MainMenuAction.Quit).ConfirmPrompt);
    }

    [Fact]
    public void With_a_save_new_game_and_continue_ask_and_continue_names_the_newest_save()
    {
        var saves = new Saves();
        saves.Slots.Add(Slot("older"));
        var newest = Slot("newest", "Diamond City") with { Timestamp = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero) };
        saves.Slots.Add(newest);
        var model = new MainMenuModel(saves);

        var newGame = Entry(model, MainMenuAction.NewGame);
        var cont = Entry(model, MainMenuAction.Continue);

        Assert.True(newGame.NeedsConfirmation);
        Assert.NotEqual("", newGame.ConfirmPrompt);
        Assert.True(cont.NeedsConfirmation);
        Assert.Contains("newest", cont.ConfirmPrompt);
        Assert.Contains("Diamond City", cont.ConfirmPrompt);
        Assert.False(Entry(model, MainMenuAction.Load).NeedsConfirmation);
        Assert.False(Entry(model, MainMenuAction.Settings).NeedsConfirmation);
    }

    [Fact]
    public void Refresh_drops_the_new_game_confirmation_when_the_last_save_disappears()
    {
        var saves = new Saves();
        saves.Slots.Add(Slot("a"));
        var model = new MainMenuModel(saves);
        Assert.True(Entry(model, MainMenuAction.NewGame).NeedsConfirmation);

        saves.Slots.Clear();
        model.Refresh();

        Assert.False(Entry(model, MainMenuAction.NewGame).NeedsConfirmation);
        Assert.Equal("", Entry(model, MainMenuAction.Continue).ConfirmPrompt);
    }

    [Fact]
    public void Clip_keeps_text_within_the_limit_with_an_ellipsis_mark()
    {
        Assert.Equal(new string('a', 30), SaveSlotText.Clip(new string('a', 30), 30));
        string clipped = SaveSlotText.Clip(new string('b', 40), 30);
        Assert.Equal(new string('b', 29) + "…", clipped);
        Assert.Equal("", SaveSlotText.Clip("", 20));
    }

    [Fact]
    public void Clip_does_not_split_a_surrogate_pair()
    {
        string text = string.Concat(Enumerable.Repeat("\U0001F600", 25));
        string clipped = SaveSlotText.Clip(text, 20);

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F600", 19)) + "…", clipped);
        Assert.False(char.IsHighSurrogate(clipped[^2]) && !char.IsLowSurrogate(clipped[^1]));
    }

    [Fact]
    public void Character_names_clip_at_20_and_locations_at_30()
    {
        var slot = Slot("a", new string('L', 40), new string('N', 25));

        Assert.Equal(new string('N', 19) + "…", SaveSlotText.CharacterName(slot));
        Assert.Equal(new string('L', 29) + "…", SaveSlotText.Location(slot));
        Assert.Contains(new string('L', 29) + "…", SaveSlotText.Summary(slot));
        Assert.DoesNotContain(new string('L', 30), SaveSlotText.Summary(slot));
    }

    [Fact]
    public void Play_time_shows_days_from_24_hours_with_hours_modulo_24()
    {
        Assert.Equal("1d 1h 01m", SaveSlotText.Playtime(TimeSpan.FromSeconds(90061)));
        Assert.Equal("1d 0h 05m", SaveSlotText.Playtime(TimeSpan.FromMinutes(1445)));
        Assert.Equal("23h 59m", SaveSlotText.Playtime(TimeSpan.FromMinutes(1439)));
        Assert.Equal("2d 3h 00m", SaveSlotText.Playtime(TimeSpan.FromHours(51)));
        Assert.Equal("0m", SaveSlotText.Playtime(TimeSpan.FromSeconds(59)));
    }

    [Fact]
    public void The_preview_clears_at_once_on_change_and_appears_after_200_ms_of_stillness()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");
        Assert.Null(p.ShownId);
        Assert.False(p.Tick(0.15));
        Assert.Null(p.ShownId);
        Assert.True(p.Tick(0.06));
        Assert.Equal("a", p.ShownId);

        p.Select("b");
        Assert.Null(p.ShownId);
        p.Tick(0.15);
        p.Select("c");
        Assert.False(p.Tick(0.15));
        Assert.Null(p.ShownId);
        Assert.True(p.Tick(0.1));
        Assert.Equal("c", p.ShownId);
    }

    [Fact]
    public void Flush_shows_the_pending_preview_immediately_and_reselecting_the_shown_id_changes_nothing()
    {
        var p = new SavePreviewDebounce();
        p.Select("a");

        Assert.True(p.Flush());
        Assert.Equal("a", p.ShownId);
        Assert.False(p.Flush());

        p.Select("a");
        Assert.Equal("a", p.ShownId);

        p.Select(null);
        Assert.Null(p.ShownId);
        Assert.False(p.Tick(1));
    }

    [Fact]
    public void Clip_with_a_tiny_limit_does_not_throw()
    {
        Assert.Equal("", SaveSlotText.Clip("abcdef", 0));
        Assert.Equal("", SaveSlotText.Clip("abcdef", -1));
        Assert.Equal("…", SaveSlotText.Clip("abcdef", 1));
        Assert.Equal("a…", SaveSlotText.Clip("abcdef", 2));
        Assert.Equal("abc", SaveSlotText.Clip("abc", 3));
        Assert.Equal("ab…", SaveSlotText.Clip("abcdef", 3));
    }
}
