using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed record QuestObjective(string Id, string Text, bool Completed = false, bool Optional = false);

public sealed record Quest(string Id, string Name, string Group, string Description,
                           IReadOnlyList<QuestObjective> Objectives, bool Completed = false, int Recent = 0,
                           string? MapTarget = null)
{
    public string SearchText => string.Join(' ', new[] { Name, Group, Description }.Concat(Objectives.Select(o => o.Text)));
}

public sealed record Note(string Id, string Title, string Text, int Recent = 0);

public enum QuestStatusFilter
{
    Active,
    Completed,
    All,
}

public enum QuestSort
{
    Recent,
    Name,
    Group,
}

public enum DataPage
{
    Quests,
    Notes,
    Workshops,
    Statistics,
}

public enum QuestEventKind
{
    Track,
    Untrack,
}

public sealed record QuestEvent(QuestEventKind Kind, string QuestId);

public sealed record ShowOnMapResult(string TargetId, QuestEvent? Tracked);

public sealed class QuestLogModel : IPipBoyTabSource
{
    private const string QuestPrefix = "quest:";
    private const string NotePrefix = "note:";
    private const string WorkshopPrefix = "workshop:";
    private const string StatisticsPrefix = "stats:";
    private const string StatusFilterName = "status";

    private readonly List<Quest> _quests;
    private readonly List<Note> _notes;
    private int _noteCounter;

    public QuestLogModel(IEnumerable<Quest> quests, IEnumerable<Note>? notes = null, WorkshopLog? workshops = null,
                         StatisticsLog? statistics = null, bool withNotes = true)
    {
        NotesOffered = withNotes;
        Workshops = workshops;
        Statistics = statistics;
        _quests = quests.ToList();
        _notes = (withNotes ? notes ?? Array.Empty<Note>() : Array.Empty<Note>()).ToList();
        _noteCounter = _notes.Count == 0 ? 0 : _notes.Max(n => n.Recent);
        Quests = new MenuList<Quest>(_quests, q => q.Id, q => q.SearchText, q => q.Group);
        Notes = new MenuList<Note>(_notes, n => n.Id, n => n.Title + " " + n.Text);
        SetStatusFilter(QuestStatusFilter.Active);
        SetSort(QuestSort.Recent);
    }

    public MenuList<Quest> Quests { get; }

    public MenuList<Note> Notes { get; }

    // Invariant: false hides the Notes page for good; the page cannot be opened, and no note can be added or selected.
    public bool NotesOffered { get; }

    public WorkshopLog? Workshops { get; }

    public StatisticsLog? Statistics { get; }

    public event Action<QuestEvent>? TrackedChanged;

    public PipBoyTab Tab => PipBoyTab.Data;

    public DataPage Page { get; private set; }

    public QuestStatusFilter StatusFilter { get; private set; }

    public QuestSort Sort { get; private set; }

    public bool SortDescending { get; private set; }

    public string? TrackedId { get; private set; }

    public Quest? TrackedQuest => TrackedId is null ? null : _quests.Find(q => q.Id == TrackedId);

    public string? CurrentKey => Page switch
    {
        DataPage.Quests => Quests.Current is { } quest ? QuestPrefix + quest.Id : null,
        DataPage.Notes => Notes.Current is { } note ? NotePrefix + note.Id : null,
        DataPage.Workshops => Workshops?.View.Current is { } workshop ? WorkshopPrefix + workshop.Id : null,
        _ => Statistics?.View.Current is { } category ? StatisticsPrefix + category.Name : null,
    };

    public string? CurrentMapTarget
    {
        get
        {
            var target = Page switch
            {
                DataPage.Quests => Quests.Current is { Completed: false } quest ? quest.MapTarget : null,
                DataPage.Workshops => Workshops?.View.Current?.MapTarget,
                _ => null,
            };
            return string.IsNullOrEmpty(target) ? null : target;
        }
    }

    public void SetPage(DataPage page)
    {
        if (page != DataPage.Notes || NotesOffered) Page = page;
    }

    public void SetSearch(string? query)
    {
        Quests.SetSearch(query);
        Notes.SetSearch(query);
        Workshops?.View.SetSearch(query);
    }

    public ShowOnMapResult? ShowOnMap()
    {
        if (CurrentMapTarget is not { } target) return null;
        if (Page != DataPage.Quests) return new ShowOnMapResult(target, null);
        var tracked = Quests.Current is { } quest && TrackedId != quest.Id ? TrackCurrent() : null;
        return new ShowOnMapResult(target, tracked);
    }

    public void SetStatusFilter(QuestStatusFilter status)
    {
        StatusFilter = status;
        Quests.SetFilter(StatusFilterName, status switch
        {
            QuestStatusFilter.Active => q => !q.Completed,
            QuestStatusFilter.Completed => q => q.Completed,
            _ => null,
        });
    }

    public void SetSort(QuestSort sort, bool? descending = null)
    {
        Sort = sort;
        SortDescending = descending ?? sort == QuestSort.Recent;
        Quests.SetSort(sort switch
        {
            QuestSort.Recent => SortKey<Quest>.By("Recent", q => q.Recent),
            QuestSort.Name => new SortKey<Quest>("Name", (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)),
            QuestSort.Group => new SortKey<Quest>("Group", (a, b) => string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase)),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, null),
        }, SortDescending);
    }

    public QuestEvent? TrackCurrent()
    {
        if (Page != DataPage.Quests || Quests.Current is not { Completed: false } quest) return null;
        var evt = TrackedId == quest.Id
            ? new QuestEvent(QuestEventKind.Untrack, quest.Id)
            : new QuestEvent(QuestEventKind.Track, quest.Id);
        TrackedId = evt.Kind == QuestEventKind.Track ? quest.Id : null;
        TrackedChanged?.Invoke(evt);
        return evt;
    }

    public Note AddNote(string title, string text)
    {
        if (!NotesOffered) throw new InvalidOperationException("the Notes page is not offered");
        var next = ++_noteCounter;
        var id = "note-" + next.ToString(CultureInfo.InvariantCulture);
        while (_notes.Any(n => n.Id == id)) id += "x";
        var note = new Note(id, title, text, next);
        _notes.Add(note);
        Notes.Refresh();
        return note;
    }

    public bool Select(string key)
    {
        if (key.StartsWith(QuestPrefix, StringComparison.Ordinal)) return SelectQuest(key[QuestPrefix.Length..]);
        if (key.StartsWith(NotePrefix, StringComparison.Ordinal)) return SelectNote(key[NotePrefix.Length..]);
        if (key.StartsWith(WorkshopPrefix, StringComparison.Ordinal)) return SelectWorkshop(key[WorkshopPrefix.Length..]);
        if (key.StartsWith(StatisticsPrefix, StringComparison.Ordinal)) return SelectStatistics(key[StatisticsPrefix.Length..]);
        return false;
    }

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens)
    {
        var hits = new List<PipBoySearchHit>();
        foreach (var quest in _quests.Where(q => ListSearch.Matches(q.SearchText, tokens)))
        {
            hits.Add(new PipBoySearchHit(PipBoyTab.Data, QuestPrefix + quest.Id, quest.Name,
                                         quest.Completed ? quest.Group + " (completed)" : quest.Group));
        }
        foreach (var note in _notes.Where(n => ListSearch.Matches(n.Title + " " + n.Text, tokens)))
        {
            hits.Add(new PipBoySearchHit(PipBoyTab.Data, NotePrefix + note.Id, note.Title, "Note"));
        }
        foreach (var workshop in (Workshops?.Items ?? Array.Empty<Workshop>()).Where(w => ListSearch.Matches(w.Name, tokens)))
        {
            hits.Add(new PipBoySearchHit(PipBoyTab.Data, WorkshopPrefix + workshop.Id, workshop.Name, "Workshop"));
        }
        return hits;
    }

    private bool SelectQuest(string id)
    {
        var quest = _quests.Find(q => q.Id == id);
        if (quest is null) return false;
        if (!Quests.Visible.Contains(quest))
        {
            Quests.SetSearch(null);
            Notes.SetSearch(null);
            Quests.SetCategory(null);
            SetStatusFilter(QuestStatusFilter.All);
        }
        Page = DataPage.Quests;
        return Quests.SelectVisible(Quests.Visible.ToList().FindIndex(q => q.Id == id));
    }

    private bool SelectWorkshop(string id)
    {
        if (Workshops is null || !Workshops.Items.Any(w => w.Id == id)) return false;
        Workshops.View.SetSearch(null);
        if (!Workshops.Select(id)) return false;
        Page = DataPage.Workshops;
        return true;
    }

    private bool SelectStatistics(string name)
    {
        if (Statistics is null || !Statistics.Select(name)) return false;
        Page = DataPage.Statistics;
        return true;
    }

    private bool SelectNote(string id)
    {
        if (!_notes.Any(n => n.Id == id)) return false;
        if (!Notes.Visible.Any(n => n.Id == id))
        {
            Quests.SetSearch(null);
            Notes.SetSearch(null);
        }
        Page = DataPage.Notes;
        return Notes.SelectVisible(Notes.Visible.ToList().FindIndex(n => n.Id == id));
    }
}
