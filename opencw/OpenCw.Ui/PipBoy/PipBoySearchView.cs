using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class PipBoyTabNames
{
    public static string Label(PipBoyTab tab) => tab switch
    {
        PipBoyTab.Stats => "Stats",
        PipBoyTab.Inventory => "Items",
        PipBoyTab.Data => "Data",
        PipBoyTab.Map => "Map",
        PipBoyTab.Radio => "Radio",
        _ => tab.ToString(),
    };
}

public sealed class PipBoySearchView : PipBoyListView<PipBoySearchHit>
{
    private readonly List<PipBoySearchHit> _hits;

    private PipBoySearchView(List<PipBoySearchHit> hits)
        : base(new MenuList<PipBoySearchHit>(hits, hit => hit.Tab + ":" + hit.Key, hit => hit.Title))
    {
        _hits = hits;
    }

    public event Action<int>? Chosen;

    public static PipBoySearchView Create() => new(new List<PipBoySearchHit>());

    public override string Summary => RowCount == 0 ? "No matches" : RowCount + " matches";

    protected override string AcceptLabel => "Jump";

    public void SetHits(IReadOnlyList<PipBoySearchHit> hits)
    {
        _hits.Clear();
        _hits.AddRange(hits);
        Source.Refresh();
        Source.SelectVisible(0);
        Notify();
    }

    protected override string TextOf(PipBoySearchHit item) => PipBoyTabNames.Label(item.Tab) + ": " + item.Title;

    protected override string DetailOf(PipBoySearchHit item) => item.Detail;

    protected override bool OnAccept(PipBoySearchHit item)
    {
        Chosen?.Invoke(SelectedIndex);
        return true;
    }
}
