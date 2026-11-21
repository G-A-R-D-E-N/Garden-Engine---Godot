using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using Xunit;

namespace OpenCw.Ui.Tests;

public class ServiceTextEdgeTests
{
    [Theory]
    [InlineData(-5L, "0 B")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1025L, "1.0 KB")]
    [InlineData(1048575L, "1.0 MB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1.0 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    [InlineData(1099511627776L, "1.0 TB")]
    [InlineData(1125899906842624L, "1024.0 TB")]
    public void Bytes_scales_by_1024_and_stops_at_terabytes(long bytes, string expected)
    {
        Assert.Equal(expected, ServiceText.Bytes(bytes));
    }

    [Theory]
    [InlineData(null, "not supplied")]
    [InlineData("", "not supplied")]
    [InlineData("   ", "not supplied")]
    [InlineData(" kept ", " kept ")]
    public void A_blank_text_is_shown_as_not_supplied(string? text, string expected)
    {
        Assert.Equal(expected, ServiceText.OrNotSupplied(text));
    }

    [Theory]
    [InlineData(ModKind.Master, "ESM")]
    [InlineData(ModKind.Plugin, "ESP")]
    [InlineData(ModKind.Light, "ESL")]
    [InlineData((ModKind)9, "ESP")]
    public void Plugin_kinds_have_their_file_extension_label(ModKind kind, string expected)
    {
        Assert.Equal(expected, ServiceText.KindLabel(kind));
    }

    [Theory]
    [InlineData(ContentType.Installed, "Installed plugin")]
    [InlineData(ContentType.CreationClub, "Creation Club (manifest)")]
    [InlineData((ContentType)7, "Installed plugin")]
    public void Content_types_have_a_display_label(ContentType type, string expected)
    {
        Assert.Equal(expected, ServiceText.TypeLabel(type));
    }
}

public class InMemoryModLibraryEdgeTests
{
    static ModEntry E(string name, bool on = true, string[]? masters = null, bool missing = false, string error = "") =>
        new(name, ModKind.Plugin, on, "Data/" + name, masters ?? Array.Empty<string>(), 1, false, missing, error);

    static string[] Messages(IReadOnlyList<ModProblem> problems) => problems.Select(p => p.Message).ToArray();

    [Fact]
    public void An_enabled_missing_or_unreadable_file_is_an_error_and_a_disabled_one_is_ignored()
    {
        var library = new InMemoryModLibrary(Array.Empty<ModEntry>());

        var problems = library.Validate(new[]
        {
            E("Gone.esp", missing: true),
            E("Bad.esp", error: "truncated"),
            E("Off.esp", on: false, missing: true, masters: new[] { "Nothing.esm" }),
        });

        Assert.Equal(new[] { "Gone.esp is enabled but the file is missing.", "Bad.esp cannot be read: truncated" }, Messages(problems));
        Assert.All(problems, p => Assert.Equal(ProblemSeverity.Error, p.Severity));
        Assert.Equal(new[] { "Gone.esp", "Bad.esp" }, problems.Select(p => p.FileName));
    }

    [Fact]
    public void A_master_must_be_present_enabled_and_earlier()
    {
        var library = new InMemoryModLibrary(Array.Empty<ModEntry>());

        var problems = library.Validate(new[]
        {
            E("Early.esp", masters: new[] { "Late.esm" }),
            E("Late.esm"),
            E("Orphan.esp", masters: new[] { "Absent.esm", "Late.esm" }),
            E("Off.esm", on: false),
            E("NeedsOff.esp", masters: new[] { "Off.esm" }),
        });

        Assert.Equal(new[]
        {
            "Early.esp loads before its master Late.esm.",
            "Orphan.esp needs Absent.esm enabled.",
            "NeedsOff.esp needs Off.esm enabled.",
        }, Messages(problems));
    }

    [Fact]
    public void A_valid_order_has_no_problems()
    {
        var library = new InMemoryModLibrary(Array.Empty<ModEntry>());

        Assert.Empty(library.Validate(new[] { E("Base.esm"), E("Mod.esp", masters: new[] { "Base.esm" }) }));
    }

    [Fact]
    public void A_failed_apply_changes_nothing_and_a_good_one_replaces_the_entries()
    {
        var library = new InMemoryModLibrary(new[] { E("A.esp"), E("B.esp", on: false) });
        var order = new[] { E("B.esp"), E("A.esp", on: false) };
        library.FailApply = true;

        var failed = library.Apply(order);

        Assert.False(failed.Ok);
        Assert.Equal("The load order could not be written.", failed.Message);
        Assert.Equal(0, library.ApplyCount);
        Assert.Null(library.Applied);
        Assert.Equal(new[] { "A.esp", "B.esp" }, library.Entries.Select(e => e.FileName));

        library.FailApply = false;
        Assert.True(library.Apply(order).Ok);
        Assert.Equal(1, library.ApplyCount);
        Assert.Equal(new[] { "B.esp", "A.esp" }, library.Load().Select(e => e.FileName));
        Assert.True(library.IsEnabled("B.esp"));
        Assert.False(library.IsEnabled("A.esp"));
        Assert.False(library.IsEnabled("Nope.esp"));
    }
}

public class ModManagerEdgeTests
{
    sealed class WarnLibrary(params ModEntry[] entries) : IModLibrary
    {
        public IReadOnlyList<ModEntry> Load() => entries;

        public IReadOnlyList<ModProblem> Validate(IReadOnlyList<ModEntry> order) =>
            order.Select(e => new ModProblem(e.FileName, ProblemSeverity.Warning, "slow to load")).ToList();

        public ServiceResult Apply(IReadOnlyList<ModEntry> order) => ServiceResult.Success();
    }

    static ModEntry E(string name, bool on = true, string[]? masters = null, int records = 10, bool missing = false, string error = "") =>
        new(name, ModKind.Plugin, on, "Data/" + name, masters ?? Array.Empty<string>(), records, false, missing, error);

    static string[] Lines(string text) => text.Split(Environment.NewLine);

    [Fact]
    public void The_summary_counts_enabled_plugins_then_unsaved_changes_then_error_problems()
    {
        var model = new ModManagerModel(new InMemoryModLibrary(new[]
        {
            E("A.esp", masters: new[] { "Ghost.esm" }),
            E("B.esp", masters: new[] { "Ghost2.esm" }),
            E("C.esp"),
        }));

        Assert.Equal("3 of 3 shown, 3 enabled, 2 problem(s)", model.Summary);
        Assert.Equal(model.Summary, model.View.Summary);

        Assert.True(model.View.Select(2));
        Assert.True(model.ToggleSelected());
        Assert.Equal("3 of 3 shown, 2 enabled, unsaved changes, 2 problem(s)", model.Summary);
    }

    [Fact]
    public void Applying_with_several_errors_names_the_first_and_counts_the_rest()
    {
        var library = new InMemoryModLibrary(new[]
        {
            E("A.esp", masters: new[] { "Ghost.esm" }),
            E("B.esp", masters: new[] { "Ghost2.esm" }),
            E("C.esp"),
        });
        var model = new ModManagerModel(library);
        model.View.Select(2);
        model.ToggleSelected();

        Assert.False(model.Apply());

        Assert.Equal("Cannot apply: A.esp needs Ghost.esm enabled. (and 1 more)", model.Status);
        Assert.Equal(0, library.ApplyCount);
        Assert.True(model.Dirty);
        Assert.False(model.RestartRequired);
    }

    [Fact]
    public void Warnings_never_block_an_apply_and_show_as_warnings_in_the_details()
    {
        var library = new WarnLibrary(E("A.esp"), E("B.esp"));
        var model = new ModManagerModel(library);
        model.View.Select(1);
        model.ToggleSelected();

        Assert.Equal("2 of 2 shown, 1 enabled, unsaved changes", model.Summary);
        Assert.Equal(new[]
        {
            "B.esp",
            "Type: ESP",
            "State: Disabled",
            "Load order: 2 of 2",
            "Path: Data/B.esp",
            "Masters: none",
            "Records: 10",
            "Warning: slow to load",
        }, Lines(model.Details(model.Order[1])));
        Assert.True(model.Apply());
        Assert.True(model.RestartRequired);
    }

    [Fact]
    public void Details_list_masters_and_flag_unknown_records_missing_files_and_read_errors()
    {
        var model = new ModManagerModel(new InMemoryModLibrary(new[]
        {
            E("Base.esm"),
            E("Odd.esp", masters: new[] { "Base.esm", "Other.esm" }, records: -1, missing: true, error: "bad header"),
        }));

        Assert.Equal(new[]
        {
            "Odd.esp",
            "Type: ESP",
            "State: Enabled",
            "Load order: 2 of 2",
            "Path: Data/Odd.esp",
            "Masters: Base.esm, Other.esm",
            "Records: unknown",
            "The file is missing from the configured folders.",
            "Error: bad header",
            "Problem: Odd.esp is enabled but the file is missing.",
            "Problem: Odd.esp needs Other.esm enabled.",
        }, Lines(model.Details(model.Order[1])));
        Assert.Equal("002 [x] Odd.esp (missing)", model.View.RowText(1));
    }

    [Fact]
    public void An_unreadable_file_that_is_not_missing_is_marked_unreadable_in_its_row()
    {
        var model = new ModManagerModel(new InMemoryModLibrary(new[] { E("Odd.esp", error: "bad header") }));

        Assert.Equal("001 [x] Odd.esp (unreadable)", model.View.RowText(0));
    }
}

public class ContentLibraryEdgeTests
{
    static ContentItem Item(string name, ContentType type, string error = "", params ContentFile[] files) =>
        new(name, type, true, "Au", "", files, error);

    [Fact]
    public void Sorting_by_type_puts_installed_first_and_breaks_ties_by_name()
    {
        var model = new ContentLibraryModel(new InMemoryContentLibrary(new[]
        {
            Item("ccBeta.esl", ContentType.CreationClub),
            Item("Gamma.esp", ContentType.Installed),
            Item("Alpha.esp", ContentType.Installed),
            Item("ccAlpha.esl", ContentType.CreationClub),
        }));

        model.View.SortBy(3);

        Assert.Equal(new[] { "Alpha.esp", "Gamma.esp", "ccAlpha.esl", "ccBeta.esl" }, model.View.Inner.Rows.Select(i => i.FileName));
    }

    [Fact]
    public void An_empty_library_has_nothing_to_manage_and_says_so()
    {
        var model = new ContentLibraryModel(new InMemoryContentLibrary(Array.Empty<ContentItem>()));
        var requested = 0;
        model.ManageRequested += _ => requested++;

        Assert.False(model.Manage());
        Assert.Equal(0, requested);
        Assert.Equal("No content matches.", model.DetailText);
        Assert.Null(model.Selected);
    }

    [Fact]
    public void Details_show_a_load_error_and_one_line_per_associated_file()
    {
        var item = Item("Odd.esp", ContentType.Installed, "bad zip", new ContentFile("Data/a.ba2", 1536), new ContentFile("Data/b.esp", 10));

        Assert.Equal(new[]
        {
            "Odd.esp",
            "Type: Installed plugin",
            "Enabled: Yes (active profile)",
            "Author: Au",
            "Description: not supplied",
            "Error: bad zip",
            "Associated files: 1.5 KB total (not the package size)",
            "  Data/a.ba2 (1.5 KB, 1536 bytes)",
            "  Data/b.esp (10 B, 10 bytes)",
        }, ContentLibraryModel.Details(item).Split(Environment.NewLine));
        Assert.Equal("[x] Odd.esp  Installed plugin (error)", new ContentLibraryModel(new InMemoryContentLibrary(new[] { item })).View.RowText(0));
    }
}

public class SetupEdgeTests
{
    sealed class StubSteps : ISetupSteps
    {
        public IReadOnlyList<SetupStepInfo> Steps { get; } = new[]
        {
            new SetupStepInfo("go", "Go", "Runs.", Runs: true),
        };

        public bool IsComplete => false;

        public string Detect(string stepId) => string.Empty;

        public SetupValidation Validate(string stepId, string value) => new(true, string.Empty);

        public ISetupRun? Start(string stepId, string value) => null;

        public ServiceResult MarkComplete() => ServiceResult.Success();
    }

    static SetupModel Running(InMemorySetupSteps steps)
    {
        var model = new SetupModel(steps);
        foreach (var index in new[] { 0, 1, 2 })
        {
            model.View.Select(index);
            model.View.Handle(MenuInputAction.Accept);
        }
        Assert.True(model.IsRunning);
        return model;
    }

    [Fact]
    public void A_step_whose_run_cannot_start_is_marked_as_needing_attention()
    {
        var model = new SetupModel(new StubSteps());

        Assert.False(model.Activate());

        Assert.Equal(SetupStepState.Invalid, model.Rows[0].State);
        Assert.Equal("This step could not start.", model.Rows[0].Message);
        Assert.Equal("This step could not start.", model.Status);
        Assert.Equal("1. Go - needs attention", model.View.RowText(0));
        Assert.False(model.IsRunning);
    }

    [Fact]
    public void The_summary_follows_the_run_through_stopping_and_the_row_ends_as_cancelled()
    {
        var model = Running(new InMemorySetupSteps(new[] { "a", "b", "c" }));
        Assert.Equal("Running, 0%", model.Summary);

        model.Pump();
        Assert.Equal("Running, 17%", model.Summary);

        Assert.True(model.Cancel());
        Assert.Equal("Stopping after the current item, 17%", model.Summary);
        Assert.False(model.Cancel());

        Assert.False(model.Pump());
        Assert.Equal("2 of 3 steps done", model.Summary);
        Assert.Equal("3. Extract and convert - cancelled", model.View.RowText(2));
        Assert.Equal(new[] { (MenuInputAction.Accept, "Resume"), (MenuInputAction.Back, "Close") }, model.View.Hints.Select(h => (h.Action, h.Label)));
    }

    [Fact]
    public void A_setup_without_steps_has_nothing_to_activate_and_only_offers_close()
    {
        var model = new SetupModel(new EmptySteps());

        Assert.False(model.Activate());
        Assert.False(model.RequestValue());
        Assert.True(model.AllDone);
        Assert.Equal("All steps done", model.Summary);
        Assert.Equal(new[] { (MenuInputAction.Back, "Close") }, model.View.Hints.Select(h => (h.Action, h.Label)));
        Assert.Equal(string.Empty, model.DetailText);
    }

    sealed class EmptySteps : ISetupSteps
    {
        public IReadOnlyList<SetupStepInfo> Steps { get; } = Array.Empty<SetupStepInfo>();

        public bool IsComplete => false;

        public string Detect(string stepId) => string.Empty;

        public SetupValidation Validate(string stepId, string value) => new(true, string.Empty);

        public ISetupRun? Start(string stepId, string value) => null;

        public ServiceResult MarkComplete() => ServiceResult.Success();
    }
}
