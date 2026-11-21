using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public sealed class MapFlowSource : IPipBoyJumpSource
{
    private readonly MapTravelFlow _flow;

    public MapFlowSource(MapTravelFlow flow)
    {
        _flow = flow;
    }

    public PipBoyTab Tab => PipBoyTab.Map;

    public string? CurrentKey => _flow.Map?.SelectedId;

    public bool Jump(string key) => _flow.FocusMarker(key);

    public bool Select(string key) => _flow.Map?.SelectMarker(key, true) ?? false;

    public IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens) =>
        _flow.Models.SelectMany(model => model.Search(tokens)).ToArray();
}
