using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class StatusListView : PipBoyListView<StatusRow>, IDisposable
{
    private readonly PlayerStatusModel _status;

    public StatusListView(PlayerStatusModel status)
        : base(status.View)
    {
        _status = status;
        status.Changed += Notify;
    }

    public PlayerStatusModel Status => _status;

    public void Dispose() => _status.Changed -= Notify;

    public override IReadOnlyList<string> Tabs => _status.SubTabLabels;

    public override int TabIndex => _status.SubTabIndex;

    public override string Summary => _status.Heading.Length > 0 ? _status.Heading : "Status";

    protected override string AcceptLabel => Selected?.Key switch
    {
        "aid:stimpak" or "aid:radaway" => "Use",
        "effects" => _status.EffectsShown ? "Hide effects" : "Show effects",
        _ => "Select",
    };

    protected override string? FavouriteLabel => _status.SubTab switch
    {
        StatusSubTab.Status when _status.HasEffects => _status.EffectsShown ? "Hide effects" : "Show effects",
        StatusSubTab.Perks when _status.CanNextRank => "Next rank",
        _ => null,
    };

    protected override IEnumerable<MenuHint> ExtraHints =>
        _status.CanPrevRank ? new[] { new MenuHint(MenuInputAction.Compare, "Previous rank") } : Array.Empty<MenuHint>();

    public bool PrevRank() => _status.PrevRank();

    protected override string TextOf(StatusRow item) => item.Label;

    protected override string DetailOf(StatusRow item) => item.Value;

    protected override void ApplyTab(int index) => _status.SetSubTab(index);

    protected override bool ToggleFavourite() => _status.SubTab switch
    {
        StatusSubTab.Status => _status.ToggleEffects(),
        StatusSubTab.Perks => _status.NextRank(),
        _ => false,
    };

    protected override bool OnAccept(StatusRow item) => item.Key switch
    {
        "aid:stimpak" => _status.RequestAid(AidKind.Stimpak),
        "aid:radaway" => _status.RequestAid(AidKind.RadAway),
        "effects" => _status.ToggleEffects(),
        _ => false,
    };
}
