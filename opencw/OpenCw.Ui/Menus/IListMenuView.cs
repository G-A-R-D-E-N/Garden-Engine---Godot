using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Lists;

namespace OpenCommonwealth.Services.Ui.Menus;

public sealed record MenuHint(MenuInputAction Action, string Label);

public interface IListMenuView
{
    event Action? Changed;

    int RowCount { get; }

    int SelectedIndex { get; }

    int ScrollTop { get; }

    int ViewRows { get; }

    string SearchText { get; }

    bool IsSearching { get; }

    IReadOnlyList<string> Tabs { get; }

    int TabIndex { get; }

    IReadOnlyList<string> SortNames { get; }

    int SortIndex { get; }

    bool SortDescending { get; }

    bool IsComparing { get; }

    string CompareHeading { get; }

    IReadOnlyList<StatComparison> CompareRows { get; }

    string Summary { get; }

    IReadOnlyList<MenuHint> Hints { get; }

    void SetViewRows(int rows);

    string RowText(int index);

    string RowDetail(int index);

    bool RowIsFavourite(int index);

    void SetSearch(string? text);

    void BeginSearch();

    void EndSearch(bool clear);

    void SelectTab(int index);

    void SortBy(int index);

    bool Select(int index);

    bool Activate(int index);

    bool ToggleFavouriteAt(int index);

    bool Handle(MenuInputAction action);
}
