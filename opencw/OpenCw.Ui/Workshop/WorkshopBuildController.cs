using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Workshop;

public sealed class WorkshopBuildController : ListDetailController<WorkshopNode>
{
    public const double AutoExitSeconds = 5.0;
    public const double NearlyFull = 0.9;
    public const int MaxStat = 999;
    public const string EmptyText = "Nothing to build here.";
    public const string PoolText = "Counted from workshop stores, linked settlements and what you carry.";
    public const string LimitText = "Build limit reached.";

    private enum PanelKind
    {
        None,
        Store,
        Scrap,
        Repair,
        RepairInfo,
    }

    private readonly IWorkshopCatalog _catalog;
    private readonly IWorkshopWorld _world;
    private readonly ISettlementProvider? _settlers;
    private readonly List<WorkshopNode> _path = new();
    private readonly HashSet<string> _tracked = new(StringComparer.Ordinal);
    private PanelKind _panel;
    private string _panelTargetId = string.Empty;
    private string? _ghostItemId;
    private WorkshopAim _lastAim;
    private double? _outsideLeft;
    private bool _sawSettlement;
    private bool _wasFull;
    private string _signature = string.Empty;

    public WorkshopBuildController(IWorkshopCatalog catalog, IWorkshopWorld world, ISettlementProvider? settlers = null,
                                   string? lastNodeId = null)
        : base(new PlainListModel<WorkshopNode>(node => node.Id, node => TextOf(catalog, node)))
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(world);
        _catalog = catalog;
        _world = world;
        _settlers = settlers;
        Rows.Accepted += OnRowAccepted;
        if (lastNodeId is not null && !FindPath(catalog.Roots, lastNodeId, _path)) _path.Clear();
        Rebuild(lastNodeId);
        _lastAim = world.Aim;
        _signature = Signature();
        if (world.Settlement is { } strip)
        {
            _sawSettlement = true;
            _wasFull = strip.BuildSize >= 1.0;
        }
    }

    public event Action<WorkshopIntent>? IntentRaised;
    public event Action<DestinationPickerController>? PickerRequested;

    public string? LastNodeId => Rows.Selected?.Id;
    public IReadOnlyList<WorkshopNode> Path => _path;
    public bool IsConfirming => _panel != PanelKind.None;
    public override string Title => "Build";
    public override string Header => Breadcrumb;

    public string Breadcrumb => _path.Count == 0 ? "Build" : "Build > " + string.Join(" > ", _path.Select(node => node.Name));

    public override string Message =>
        _outsideLeft is { } left
            ? "Build mode closing in " + ((int)Math.Ceiling(left)).ToString(CultureInfo.InvariantCulture) + " s"
            : base.Message;

    public static string StatusWord(WorkshopItemStatus status) => status switch
    {
        WorkshopItemStatus.LackingMaterials => "Lacking materials",
        WorkshopItemStatus.PerkRequired => "Perk required",
        WorkshopItemStatus.BudgetFull => "Budget full",
        _ => string.Empty,
    };

    public static string PlacementText(PlacementStatus status) => status switch
    {
        PlacementStatus.OutsideArea => "This spot is outside your build area.",
        PlacementStatus.Blocked => "Something blocks this spot.",
        PlacementStatus.Unsupported => "This needs something to rest on.",
        PlacementStatus.NeedsTerrain => "This only fits on open ground.",
        PlacementStatus.NeedsWater => "This only fits in water.",
        PlacementStatus.WireTooLong => "That run is too long.",
        PlacementStatus.WireDuplicate => "These two are already linked.",
        PlacementStatus.BudgetFull => "Build limit reached.",
        _ => string.Empty,
    };

    public override IReadOnlyList<MenuHint> Hints
    {
        get
        {
            var hints = new List<MenuHint>();
            if (_panel != PanelKind.None)
            {
                if (_panel != PanelKind.RepairInfo) hints.Add(new MenuHint(MenuInputAction.Accept, "Confirm"));
                hints.Add(new MenuHint(MenuInputAction.Back, _panel == PanelKind.RepairInfo ? "Close" : "Cancel"));
                return hints;
            }
            var target = _world.Target;
            var blocked = _world.Placement == PlacementStatus.Valid ? string.Empty : " (blocked)";
            switch (_world.Aim)
            {
                case WorkshopAim.Ghost:
                    hints.Add(new MenuHint(MenuInputAction.Accept, "Place" + blocked));
                    hints.Add(new MenuHint(MenuInputAction.Back, "Cancel"));
                    if (SelectedItem is { } item && IsShort(item))
                        hints.Add(new MenuHint(MenuInputAction.TabPrev, _tracked.Contains(item.Id) ? "Stop tracking" : "Track materials"));
                    break;
                case WorkshopAim.Holding:
                    hints.Add(new MenuHint(MenuInputAction.Accept, "Put down" + blocked));
                    hints.Add(new MenuHint(MenuInputAction.Back, "Cancel"));
                    if (target is { Scrappable: true }) hints.Add(new MenuHint(MenuInputAction.Compare, "Scrap"));
                    break;
                case WorkshopAim.WireHeld:
                    hints.Add(new MenuHint(MenuInputAction.Accept, "Place wire end" + blocked));
                    hints.Add(new MenuHint(MenuInputAction.Back, "Cancel"));
                    break;
                case WorkshopAim.TargetObject when target is not null:
                    if (target.Selectable) hints.Add(new MenuHint(MenuInputAction.Accept, "Move"));
                    hints.Add(new MenuHint(MenuInputAction.Back, BackLabel));
                    if (target.Scrappable) hints.Add(new MenuHint(MenuInputAction.Compare, "Scrap"));
                    if (target.Storable) hints.Add(new MenuHint(MenuInputAction.Favourite, "Store"));
                    if (target.Repairable) hints.Add(new MenuHint(MenuInputAction.Search, "Repair"));
                    if (target.PowerConnectable) hints.Add(new MenuHint(MenuInputAction.TabNext, "Attach wire"));
                    break;
                case WorkshopAim.TargetSettler when _world.Settler is not null:
                    hints.Add(new MenuHint(MenuInputAction.Accept, "Command"));
                    hints.Add(new MenuHint(MenuInputAction.Back, BackLabel));
                    hints.Add(new MenuHint(MenuInputAction.Compare, "Move"));
                    hints.Add(new MenuHint(MenuInputAction.Favourite, "Caravan"));
                    break;
                default:
                    if (Rows.Selected is { } node)
                        hints.Add(new MenuHint(MenuInputAction.Accept, node.IsCategory ? "Open" : "Select"));
                    hints.Add(new MenuHint(MenuInputAction.Back, BackLabel));
                    break;
            }
            hints.Add(new MenuHint(MenuInputAction.Sort, "Exit"));
            return hints;
        }
    }

    public override IReadOnlyList<MenuTextLine> DetailLines
    {
        get
        {
            var lines = new List<MenuTextLine>();
            var target = _world.Target;
            if (_panel != PanelKind.None) AddPanel(lines, target);
            else if (_world.Aim == WorkshopAim.TargetObject && target is not null) AddTarget(lines, target);
            else if (_world.Aim == WorkshopAim.TargetSettler && _world.Settler is { } settler) AddSettler(lines, settler);
            else if (Rows.RowCount == 0) lines.Add(new MenuTextLine(EmptyText, LineTone.Muted));
            else if (Rows.Selected is { } node) AddNode(lines, node);
            if (_world.Aim is WorkshopAim.Ghost or WorkshopAim.Holding or WorkshopAim.WireHeld
                && _world.Placement != PlacementStatus.Valid)
                lines.Add(new MenuTextLine(PlacementText(_world.Placement), LineTone.Bad));
            AddStrip(lines);
            return lines;
        }
    }

    private string BackLabel => _path.Count > 0 ? "Up" : "Exit";

    private WorkshopItem? SelectedItem => Rows.Selected?.Item;

    public override bool Handle(MenuInputAction action)
    {
        if (IsFinished) return false;
        if (_panel != PanelKind.None) return HandlePanel(action);
        if (action == MenuInputAction.Sort)
        {
            Exit();
            return true;
        }
        var handled = _world.Aim switch
        {
            WorkshopAim.Ghost => HandleGhost(action),
            WorkshopAim.Holding => HandleHolding(action),
            WorkshopAim.WireHeld => HandleWire(action),
            WorkshopAim.TargetObject => HandleTarget(action),
            WorkshopAim.TargetSettler => HandleSettler(action),
            _ => false,
        };
        return handled || HandleBrowse(action);
    }

    public override void Tick(double deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (IsFinished) return;
        var changed = SyncWorld();
        changed |= TickCountdown(deltaSeconds);
        if (changed && !IsFinished) Raise();
    }

    public void Refresh() => Rebuild(Rows.Selected?.Id);

    public void Exit()
    {
        if (IsFinished) return;
        if (_world.Aim is WorkshopAim.Holding or WorkshopAim.WireHeld) Intent(new CancelRequested());
        ClearGhost();
        _panel = PanelKind.None;
        Sound("build.exit");
        Finish(false);
    }

    private static bool Valid(WorkshopNode? node) =>
        node is not null && !string.IsNullOrWhiteSpace(node.Id) && !string.IsNullOrWhiteSpace(node.Name);

    private static string TextOf(IWorkshopCatalog catalog, WorkshopNode node)
    {
        if (node.Item is null) return node.Name + " (" + node.Children.Count(Valid).ToString(CultureInfo.InvariantCulture) + ")";
        var text = node.Name;
        if (catalog.FreeBuild) text += " free";
        else
        {
            var count = catalog.BuildableCount(node.Id);
            if (count > 0) text += " x " + count.ToString(CultureInfo.InvariantCulture);
        }
        if (node.Item.IsNew) text += " NEW";
        var word = StatusWord(catalog.StatusOf(node.Id));
        return word.Length > 0 ? text + " - " + word : text;
    }

    private static bool FindPath(IReadOnlyList<WorkshopNode> nodes, string id, List<WorkshopNode> path)
    {
        foreach (var node in nodes.Where(Valid))
        {
            if (node.Id == id) return true;
            if (node.Item is not null) continue;
            path.Add(node);
            if (FindPath(node.Children, id, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    private bool IsShort(WorkshopItem item) =>
        !_catalog.FreeBuild && item.Materials.Any(material => _catalog.AvailableCount(material.Id) < material.Needed);

    private void Rebuild(string? selectKey = null)
    {
        var level = _path.Count == 0 ? _catalog.Roots : _path[^1].Children;
        Rows.ShowRows(level.Where(Valid).ToList(), selectKey);
        Rows.Summary = Breadcrumb;
    }

    private void Intent(WorkshopIntent intent) => IntentRaised?.Invoke(intent);

    private bool Blocked(string text)
    {
        Say(text);
        Sound("menu.blocked");
        return true;
    }

    private bool HandleBrowse(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Up:
            case MenuInputAction.Down:
            case MenuInputAction.PageUp:
            case MenuInputAction.PageDown:
                Rows.Handle(action);
                OnCursorMoved();
                return true;
            case MenuInputAction.Accept:
                return Rows.Handle(action);
            case MenuInputAction.Right:
                if (Rows.Selected is { } node) OpenNode(node);
                return true;
            case MenuInputAction.Left:
                GoUp();
                return true;
            case MenuInputAction.Back:
                if (_path.Count > 0) GoUp();
                else Exit();
                return true;
            default:
                return false;
        }
    }

    private bool HandleGhost(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Accept:
                PlaceOrFail();
                return true;
            case MenuInputAction.Back:
                ClearGhost();
                GoUp();
                return true;
            case MenuInputAction.TabPrev:
                return Track();
            default:
                return false;
        }
    }

    private bool HandleHolding(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Accept:
                PlaceOrFail();
                return true;
            case MenuInputAction.Back:
                Intent(new CancelRequested());
                return true;
            case MenuInputAction.Compare:
                var held = _world.Target;
                if (held is { Scrappable: true, ScrapBlocked: false }) return OpenPanel(PanelKind.Scrap, held);
                return Blocked("This can't be scrapped right now.");
            default:
                return false;
        }
    }

    private bool HandleWire(MenuInputAction action)
    {
        switch (action)
        {
            case MenuInputAction.Accept:
                PlaceOrFail();
                return true;
            case MenuInputAction.Back:
                Intent(new CancelRequested());
                return true;
            default:
                return false;
        }
    }

    private bool HandleTarget(MenuInputAction action)
    {
        if (_world.Target is not { } target) return false;
        switch (action)
        {
            case MenuInputAction.Accept:
                if (!target.Selectable) return Blocked("This can't be moved.");
                Intent(new MoveRequested(target.Id));
                Sound("build.pickup");
                return true;
            case MenuInputAction.Favourite:
                return target.Storable ? OpenPanel(PanelKind.Store, target) : Blocked("This can't be stored.");
            case MenuInputAction.Compare:
                if (!target.Scrappable) return Blocked("This can't be scrapped.");
                return target.ScrapBlocked ? Blocked("This can't be scrapped right now.") : OpenPanel(PanelKind.Scrap, target);
            case MenuInputAction.Search:
                if (!target.Repairable) return Blocked("This doesn't need repairs.");
                if (target.RepairBlocked) return Blocked("This can't be repaired right now.");
                return OpenPanel(target.RepairAffordable ? PanelKind.Repair : PanelKind.RepairInfo, target);
            case MenuInputAction.TabNext:
                if (!target.PowerConnectable) return Blocked("This can't take a wire.");
                Intent(new WireRequested(target.Id));
                return true;
            default:
                return false;
        }
    }

    private bool HandleSettler(MenuInputAction action)
    {
        if (_world.Settler is not { } settler) return false;
        switch (action)
        {
            case MenuInputAction.Accept:
                if (!settler.CanCommand)
                    return Blocked(Or(settler.CommandReason, settler.Name + " won't take orders from you."));
                Intent(new SettlerCommandRequested(settler.Id));
                return true;
            case MenuInputAction.Compare:
                return settler.CanMove
                    ? OpenPicker(settler, false)
                    : Blocked(Or(settler.MoveReason, settler.Name + " can't be moved from this settlement."));
            case MenuInputAction.Favourite:
                return settler.CanCaravan
                    ? OpenPicker(settler, true)
                    : Blocked(Or(settler.CaravanReason, settler.Name + " can't be sent on a caravan route."));
            default:
                return false;
        }
    }

    private bool HandlePanel(MenuInputAction action)
    {
        if (action == MenuInputAction.Accept)
        {
            var kind = _panel;
            var id = _panelTargetId;
            _panel = PanelKind.None;
            switch (kind)
            {
                case PanelKind.Store:
                    Intent(new StoreRequested(id));
                    Sound("build.store");
                    break;
                case PanelKind.Scrap:
                    var before = _world.ScrappedCount;
                    Intent(new ScrapRequested(id));
                    if (before >= 0 && _world.ScrappedCount == before)
                    {
                        Say(ScrapFailedText);
                        Sound("build.fail");
                    }
                    else Sound("build.scrap");
                    break;
                case PanelKind.Repair:
                    Intent(new RepairRequested(id));
                    break;
            }
            Raise();
        }
        else if (action == MenuInputAction.Back)
        {
            _panel = PanelKind.None;
            Raise();
        }
        return true;
    }

    private bool OpenPanel(PanelKind kind, WorkshopTarget target)
    {
        _panel = kind;
        _panelTargetId = target.Id;
        Raise();
        return true;
    }

    private bool OpenPicker(WorkshopSettlerCard settler, bool caravan)
    {
        var provider = _settlers;
        if (provider is null) return Blocked("Not available yet.");
        var settlerId = settler.Id;
        var entries = provider.Settlements
            .Where(settlement => settlement.Id != settler.HomeId)
            .Select(settlement => new DestinationEntry(settlement.Id, settlement.Name))
            .ToList();
        var picker = DestinationPickerController.TryCreate(
            caravan ? "Assign route" : "Move settler",
            caravan ? "Set a route to {name}?" : "Send {name} to live there?",
            entries,
            null,
            () => !IsFinished && _world.Settler?.Id == settlerId,
            id => caravan ? provider.AssignCaravan(settlerId, id) : provider.MoveHome(settlerId, id));
        if (picker is null) return Blocked("Not available yet.");
        PickerRequested?.Invoke(picker);
        return true;
    }

    public const string ScrapFailedText = "That could not be scrapped.";

    public const string PlaceFailedText = "That could not be built.";

    private void PlaceOrFail()
    {
        if (_world.Placement == PlacementStatus.Valid)
        {
            var before = _world.PlacedCount;
            Intent(new PlaceRequested());
            if (before >= 0 && _world.PlacedCount == before)
            {
                Say(PlaceFailedText);
                Sound("build.fail");
                return;
            }
            Sound("build.place");
            return;
        }
        Say(PlacementText(_world.Placement));
        Sound("build.fail");
    }

    private bool Track()
    {
        if (SelectedItem is not { } item) return false;
        if (!IsShort(item)) return Blocked("Nothing is short.");
        var on = _tracked.Add(item.Id);
        if (!on) _tracked.Remove(item.Id);
        Intent(new TrackRequested(item.Id, on));
        Say(on ? "Tracking materials." : "Stopped tracking.");
        return true;
    }

    private void OnRowAccepted(WorkshopNode node)
    {
        if (IsFinished || _panel != PanelKind.None) return;
        OpenNode(node);
    }

    private void OpenNode(WorkshopNode node)
    {
        if (node.Item is null)
        {
            ClearGhost();
            _path.Add(node);
            Rebuild();
            Sound("menu.forward");
            return;
        }
        var status = _catalog.StatusOf(node.Id);
        if (status != WorkshopItemStatus.Available)
        {
            Blocked(StatusSentence(status));
            return;
        }
        if (_ghostItemId == node.Id) return;
        _ghostItemId = node.Id;
        Intent(new GhostRequested(node.Id));
        Sound("menu.forward");
    }

    private void GoUp()
    {
        if (_path.Count == 0) return;
        ClearGhost();
        var left = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        Rebuild(left.Id);
        Sound("menu.back");
    }

    private void OnCursorMoved()
    {
        if (_ghostItemId is null) return;
        if (Rows.Selected is { Item: not null } node && _catalog.StatusOf(node.Id) == WorkshopItemStatus.Available)
        {
            if (_ghostItemId == node.Id) return;
            _ghostItemId = node.Id;
            Intent(new GhostRequested(node.Id));
            return;
        }
        ClearGhost();
    }

    private void ClearGhost()
    {
        if (_ghostItemId is null) return;
        _ghostItemId = null;
        Intent(new GhostCleared());
    }

    private bool SyncWorld()
    {
        var changed = false;
        var aim = _world.Aim;
        if (_lastAim == WorkshopAim.Ghost && aim != WorkshopAim.Ghost) ClearGhost();
        _lastAim = aim;
        if (_panel != PanelKind.None && _world.Target?.Id != _panelTargetId)
        {
            _panel = PanelKind.None;
            changed = true;
        }
        var signature = Signature();
        if (signature != _signature)
        {
            _signature = signature;
            changed = true;
        }
        if (_world.Settlement is { } strip)
        {
            var full = strip.BuildSize >= 1.0;
            if (_sawSettlement && full && !_wasFull)
            {
                Say(LimitText);
                Sound("menu.blocked");
            }
            _wasFull = full;
            _sawSettlement = true;
        }
        return changed;
    }

    private bool TickCountdown(double deltaSeconds)
    {
        if (_world.OutsideBuildArea)
        {
            if (_outsideLeft is null)
            {
                _outsideLeft = AutoExitSeconds;
                return true;
            }
            if (double.IsFinite(deltaSeconds) && deltaSeconds > 0) _outsideLeft -= deltaSeconds;
            if (_outsideLeft > 0) return true;
            _outsideLeft = null;
            Say("Build mode closed");
            Exit();
            return false;
        }
        if (_outsideLeft is null) return false;
        _outsideLeft = null;
        Say("Build mode stays open");
        return true;
    }

    private string Signature()
    {
        var strip = _world.Settlement;
        var stripText = strip is null
            ? string.Empty
            : string.Join(',', Enum.GetValues<SettlementStat>()
                  .Select(stat => strip.Values.TryGetValue(stat, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "-"))
              + "|" + string.Join(',', strip.Alerts.OrderBy(stat => stat))
              + "|" + strip.HappinessTrend.ToString(CultureInfo.InvariantCulture)
              + "|" + strip.BuildSize.ToString("R", CultureInfo.InvariantCulture)
              + "|" + strip.ReadoutVisible;
        return _world.Aim + "|" + _world.Placement + "|" + _world.Target?.Id + "|" + _world.Settler?.Id
            + "|" + _world.OutsideBuildArea + "|" + stripText;
    }

    private static string Or(string text, string fallback) => text.Length > 0 ? text : fallback;

    private static string StatusSentence(WorkshopItemStatus status) => status switch
    {
        WorkshopItemStatus.LackingMaterials => "You don't have the materials for that.",
        WorkshopItemStatus.PerkRequired => "You need a perk to build that.",
        WorkshopItemStatus.BudgetFull => LimitText,
        _ => string.Empty,
    };

    private void AddNode(List<MenuTextLine> lines, WorkshopNode node)
    {
        if (node.Item is not { } item)
        {
            lines.Add(new MenuTextLine(node.Name, LineTone.Heading));
            lines.Add(new MenuTextLine(node.Children.Count(Valid).ToString(CultureInfo.InvariantCulture) + " entries", LineTone.Muted));
            return;
        }
        lines.Add(new MenuTextLine(item.Name, LineTone.Heading));
        var status = _catalog.StatusOf(item.Id);
        if (status != WorkshopItemStatus.Available)
            lines.Add(new MenuTextLine("Status: " + StatusWord(status), LineTone.Bad));
        if (item.Description.Length > 0) lines.Add(new MenuTextLine(item.Description));
        if (item.Materials.Count > 0 || item.PowerRequired > 0 || item.RequiresWorker)
            lines.Add(new MenuTextLine("Needs", LineTone.Heading));
        foreach (var material in item.Materials) lines.Add(MaterialLine(material));
        if (item.Materials.Count > 0) lines.Add(new MenuTextLine(PoolText, LineTone.Muted));
        if (item.PowerRequired > 0)
            lines.Add(new MenuTextLine("Power: " + item.PowerRequired.ToString(CultureInfo.InvariantCulture)));
        if (item.RequiresWorker) lines.Add(new MenuTextLine("Needs a worker"));
        var produced = Produced(item.Produces);
        if (produced.Count > 0)
        {
            lines.Add(new MenuTextLine("Produces", LineTone.Heading));
            lines.AddRange(produced.Select(text => new MenuTextLine(text)));
        }
        if (item.Perks.Count > 0)
        {
            lines.Add(new MenuTextLine("Perks", LineTone.Heading));
            foreach (var perk in item.Perks)
                lines.Add(new MenuTextLine(perk.Name + ", rank " + perk.Rank.ToString(CultureInfo.InvariantCulture) + " - "
                    + (perk.Met ? "met" : "locked"), perk.Met ? LineTone.Good : LineTone.Bad));
        }
        if (item.Stored > 0)
            lines.Add(new MenuTextLine("Stored: " + item.Stored.ToString(CultureInfo.InvariantCulture)));
    }

    private MenuTextLine MaterialLine(WorkshopMaterial material)
    {
        if (_catalog.FreeBuild)
            return new MenuTextLine(material.Name + " Unlimited/" + CountText.Short(material.Needed));
        var have = _catalog.AvailableCount(material.Id);
        return new MenuTextLine(CountText.HaveNeed(material.Name, have, material.Needed),
            have < material.Needed ? LineTone.Bad : LineTone.Normal);
    }

    private static List<string> Produced(WorkshopOutput output)
    {
        var produced = new List<string>();
        void Add(string label, int value) { if (value != 0) produced.Add(label + " " + value.ToString(CultureInfo.InvariantCulture)); }
        Add("Food", output.Food);
        Add("Water", output.Water);
        Add("Power", output.Power);
        if (output.Safety > 0) Add("Defense", output.Safety);
        else if (output.Safety < 0) produced.Add("Reduces defense");
        Add("Happiness", output.Happiness);
        return produced;
    }

    private void AddTarget(List<MenuTextLine> lines, WorkshopTarget target)
    {
        lines.Add(new MenuTextLine(target.Name, LineTone.Heading));
        if (target.Selectable) lines.Add(new MenuTextLine("Can be moved"));
        if (target.Storable) lines.Add(new MenuTextLine("Can be stored"));
        if (target.Scrappable)
            lines.Add(target.ScrapBlocked ? new MenuTextLine("Can't be scrapped right now", LineTone.Bad) : new MenuTextLine("Can be scrapped"));
        if (target.Repairable)
            lines.Add(target.RepairBlocked ? new MenuTextLine("Can't be repaired right now", LineTone.Bad) : new MenuTextLine("Can be repaired"));
        if (target.PowerConnectable) lines.Add(new MenuTextLine("Can take a wire"));
    }

    private static void AddSettler(List<MenuTextLine> lines, WorkshopSettlerCard settler)
    {
        lines.Add(new MenuTextLine(settler.Name, LineTone.Heading));
        if (settler.JobText.Length > 0) lines.Add(new MenuTextLine("Job: " + settler.JobText));
        if (settler.HomeName.Length > 0) lines.Add(new MenuTextLine("Home: " + settler.HomeName));
    }

    private void AddPanel(List<MenuTextLine> lines, WorkshopTarget? target)
    {
        var name = target?.Name ?? string.Empty;
        switch (_panel)
        {
            case PanelKind.Store:
                lines.Add(new MenuTextLine("Keep " + name + " in storage?", LineTone.Heading));
                break;
            case PanelKind.Scrap:
                lines.Add(new MenuTextLine("Break down " + name + " for parts?", LineTone.Heading));
                if (target is { Yield.Count: > 0 })
                {
                    lines.Add(new MenuTextLine("You receive", LineTone.Heading));
                    foreach (var part in target.Yield)
                        lines.Add(new MenuTextLine(part.Name + " x " + part.Needed.ToString(CultureInfo.InvariantCulture)));
                }
                break;
            case PanelKind.Repair:
            case PanelKind.RepairInfo:
                lines.Add(new MenuTextLine(
                    _panel == PanelKind.Repair ? "Restore " + name + "? Cost:" : "Restore " + name + "?", LineTone.Heading));
                if (_panel == PanelKind.RepairInfo)
                    lines.Add(new MenuTextLine("You don't have the materials.", LineTone.Bad));
                if (target is not null)
                    foreach (var cost in target.RepairCost) lines.Add(MaterialLine(cost));
                break;
        }
    }

    private void AddStrip(List<MenuTextLine> lines)
    {
        if (_world.Settlement is not { ReadoutVisible: true } strip) return;
        lines.Add(new MenuTextLine("Settlement", LineTone.Heading));
        foreach (var stat in Enum.GetValues<SettlementStat>())
        {
            var shown = strip.Values.TryGetValue(stat, out var value) ? Math.Clamp(value, 0, MaxStat).ToString(CultureInfo.InvariantCulture) : "--";
            var text = stat + ": " + shown;
            var alert = strip.Alerts.Contains(stat);
            if (alert) text += " alert";
            if (stat == SettlementStat.Happiness && strip.HappinessTrend != 0)
                text += strip.HappinessTrend > 0 ? " rising" : " falling";
            lines.Add(new MenuTextLine(text, alert ? LineTone.Bad : LineTone.Normal));
        }
        var size = Math.Clamp(strip.BuildSize, 0.0, 1.0);
        var meter = "Build size: " + ((int)Math.Round(size * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        if (size >= 1.0) meter += " Full";
        else if (size >= NearlyFull) meter += " Nearly full";
        lines.Add(new MenuTextLine(meter, size >= NearlyFull ? LineTone.Bad : LineTone.Normal));
    }
}
