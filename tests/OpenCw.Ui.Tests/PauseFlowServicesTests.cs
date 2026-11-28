using OpenCommonwealth.Services.Ui.Menus;
using System.Collections.Generic;
using System;
using System.Linq;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class PauseFlowServicesTests
{
    [Fact]
    public void The_services_entry_is_absent_unless_asked_for()
    {
        var model = new PauseFlowModel(includeGallery: true);
        model.Open(true);

        Assert.DoesNotContain(model.Entries, e => e.Action == PauseAction.Services);
        Assert.False(model.Select(PauseAction.Services));
    }

    [Fact]
    public void The_services_entry_sits_before_gallery_and_quit()
    {
        var model = new PauseFlowModel(includeGallery: true, includeServices: true);
        model.Open(true);

        var actions = model.Entries.Select(e => e.Action).ToArray();

        Assert.Equal(new[]
        {
            PauseAction.Resume, PauseAction.LoadGame, PauseAction.Settings, PauseAction.Character,
            PauseAction.Appearance, PauseAction.Services, PauseAction.Gallery, PauseAction.Quit,
        }, actions);
        Assert.Equal("Mods and Setup", model.Entries.Single(e => e.Action == PauseAction.Services).Label);
        Assert.True(model.Entries.Single(e => e.Action == PauseAction.Services).Enabled);
    }

    [Fact]
    public void It_is_enabled_even_with_no_saved_games()
    {
        var model = new PauseFlowModel(includeServices: true);
        model.Open(false);

        Assert.True(model.Select(PauseAction.Services));
    }

    [Fact]
    public void Activating_it_enters_the_services_state_and_back_returns_to_the_pause_menu()
    {
        var model = new PauseFlowModel(includeServices: true);
        model.Open(true);
        model.Select(PauseAction.Services);

        var entry = model.Activate();

        Assert.Equal(PauseAction.Services, entry?.Action);
        Assert.Equal(PauseFlowState.Services, model.State);

        Assert.True(model.Back());
        Assert.Equal(PauseFlowState.Pause, model.State);
        Assert.Equal(PauseAction.Services, model.Selected.Action);
    }

    [Fact]
    public void The_hub_lists_the_four_services_and_flags_unfinished_setup()
    {
        var pending = ServiceCatalog.Entries(setupNeeded: true);
        var finished = ServiceCatalog.Entries(setupNeeded: false);

        Assert.Equal(
            new[] { ServiceKind.ModManager, ServiceKind.ContentLibrary, ServiceKind.Profiles, ServiceKind.Setup },
            pending.Select(e => e.Kind).ToArray());
        Assert.Contains("Not finished yet", pending[3].Description);
        Assert.DoesNotContain("Not finished yet", finished[3].Description);
    }

    [Fact]
    public void The_sample_backends_start_with_setup_unfinished_and_a_default_profile()
    {
        var backends = InMemoryServiceBackends.Create();

        Assert.False(backends.Setup.IsComplete);
        Assert.Equal("default", backends.Profiles.ActiveId);
        Assert.Equal(
            new[] { "Base.esm", "Addon.esm", "Weapons.esp", "Armor.esp", "Small.esl", "Gone.esp" },
            backends.Mods.Load().Select(m => m.FileName).ToArray());
        Assert.Equal(
            new[] { ("Base.esm", true), ("Addon.esm", true), ("Weapons.esp", true), ("Armor.esp", false) },
            backends.Content.Load().Select(c => (c.FileName, c.Enabled)).ToArray());
    }

    [Fact]
    public void The_hub_rebuilds_its_entries_when_setup_completes()
    {
        var needed = true;
        var hub = new ServiceHubModel(() => needed);
        Assert.Contains("Not finished yet", hub.Entries[3].Description);

        needed = false;
        hub.Refresh();

        Assert.DoesNotContain("Not finished yet", hub.Entries[3].Description);
        Assert.Equal(4, hub.Entries.Count);
    }

    [Fact]
    public void The_hub_keeps_its_selection_across_a_refresh()
    {
        var hub = new ServiceHubModel(() => false);
        hub.View.Select(3);

        hub.Refresh();

        Assert.Equal(ServiceKind.Setup, hub.Selected?.Kind);
    }

    [Fact]
    public void The_hub_has_no_search_hint_and_accept_requests_the_selected_service()
    {
        var hub = new ServiceHubModel(() => false);
        var opened = new List<ServiceKind>();
        hub.OpenRequested += opened.Add;
        hub.View.Select(2);

        Assert.DoesNotContain(hub.View.Hints, h => h.Action == MenuInputAction.Search);
        Assert.True(hub.View.Handle(MenuInputAction.Accept));

        Assert.Equal(new[] { ServiceKind.Profiles }, opened.ToArray());
        Assert.StartsWith("Profiles", hub.DetailText);
    }

    [Fact]
    public void Left_and_right_scroll_the_detail_pane_and_nothing_else_does()
    {
        var actions = Enum.GetValues<MenuInputAction>();

        Assert.Equal(-ServiceDetailScroll.Step, ServiceDetailScroll.Delta(MenuInputAction.Left));
        Assert.Equal(ServiceDetailScroll.Step, ServiceDetailScroll.Delta(MenuInputAction.Right));
        Assert.Equal(2, actions.Count(a => ServiceDetailScroll.Delta(a) != 0));
        Assert.Equal(2, ServiceDetailScroll.Hints.Count);
    }
}
