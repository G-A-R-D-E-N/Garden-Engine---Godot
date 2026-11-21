using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.MainMenu;
using Xunit;

namespace OpenCw.Ui.Tests;

public class MainMenuSaveSlotReconcilerTests
{
    static readonly DateTimeOffset Stamp = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    static SaveSlotInfo Info(string id) => new(id, "Named " + id, "Ann", 7, TimeSpan.FromMinutes(5), "Harbor", Stamp, "", SaveKind.Manual);

    static SaveKind Kind(string id) => id == "quicksave" ? SaveKind.Quick : id == "autosave" ? SaveKind.Auto : SaveKind.Manual;

    [Fact]
    public void A_payload_without_a_sidecar_is_listed_with_a_made_up_entry()
    {
        var slots = SaveSlotReconciler.Reconcile(Array.Empty<SaveSlotInfo>(), new[] { new SavePayload("quicksave", Stamp) }, Kind);

        var slot = Assert.Single(slots);
        Assert.Equal("quicksave", slot.Id);
        Assert.Equal(SaveKind.Quick, slot.Kind);
        Assert.Equal(Stamp, slot.Timestamp);
    }

    [Fact]
    public void A_sidecar_without_a_payload_is_not_offered_as_a_save()
    {
        var slots = SaveSlotReconciler.Reconcile(new[] { Info("gone"), Info("kept") }, new[] { new SavePayload("kept", Stamp) }, Kind);

        Assert.Equal(new[] { "kept" }, slots.Select(s => s.Id).ToArray());
        Assert.Equal("Named kept", slots[0].Name);
    }

    [Fact]
    public void A_payload_with_a_sidecar_keeps_the_sidecar_details_and_an_invalid_payload_name_is_skipped()
    {
        var slots = SaveSlotReconciler.Reconcile(new[] { Info("a") }, new[] { new SavePayload("a", Stamp.AddDays(1)), new SavePayload("a", Stamp), new SavePayload("bad.name", Stamp) }, Kind);

        var slot = Assert.Single(slots);
        Assert.Equal(7, slot.Level);
        Assert.Equal(Stamp, slot.Timestamp);
    }

    sealed class FakeFiles : ISaveFiles
    {
        public HashSet<string> Present { get; } = new();

        public HashSet<string> FailMoveOf { get; } = new();

        public bool Exists(string path) => Present.Contains(path);

        public bool Move(string from, string to)
        {
            if (FailMoveOf.Contains(from) || !Present.Remove(from)) return false;
            Present.Add(to);
            return true;
        }

        public bool Remove(string path) => Present.Remove(path);
    }

    [Fact]
    public void Deleting_removes_every_file_of_the_slot()
    {
        var files = new FakeFiles { Present = { "d/a.sav", "d/a.info.json", "d/a.png" } };

        Assert.True(SaveSlotDeleter.Delete(files, new[] { "d/a.sav", "d/a.info.json", "d/a.png" }));
        Assert.Empty(files.Present);
    }

    [Fact]
    public void A_failed_delete_puts_back_the_files_already_moved_and_reports_failure()
    {
        var files = new FakeFiles { Present = { "d/a.sav", "d/a.info.json", "d/a.png" }, FailMoveOf = { "d/a.png" } };

        Assert.False(SaveSlotDeleter.Delete(files, new[] { "d/a.sav", "d/a.info.json", "d/a.png" }));
        Assert.Equal(new[] { "d/a.info.json", "d/a.png", "d/a.sav" }, files.Present.OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Deleting_a_payload_that_has_no_sidecar_works_and_deleting_nothing_fails()
    {
        var files = new FakeFiles { Present = { "d/a.sav" } };

        Assert.True(SaveSlotDeleter.Delete(files, new[] { "d/a.sav", "d/a.info.json" }));
        Assert.False(SaveSlotDeleter.Delete(files, new[] { "d/a.sav", "d/a.info.json" }));
    }
}
