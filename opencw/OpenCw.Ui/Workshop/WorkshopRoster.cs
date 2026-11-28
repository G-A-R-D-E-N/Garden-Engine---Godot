using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.Workshop;

// Requires: main thread only; the host supplies the settlers and objects of one settlement, read once per Refresh so a menu does not rescan the world on every redraw.
public sealed class WorkshopRoster(SettlementInfo settlement, Func<IReadOnlyList<SettlerInfo>> settlers, Func<IReadOnlyList<WorkObject>> objects)
    : ISettlerRoster
{
    private IReadOnlyList<SettlerInfo>? _settlers;
    private IReadOnlyList<WorkObject>? _objects;

    public event Action? Changed;

    public IReadOnlyList<SettlementInfo> Settlements { get; } = new[] { settlement };

    public IReadOnlyList<SettlerInfo> Settlers => _settlers ??= settlers();

    public IReadOnlyList<WorkObject> Objects(string settlerId) => _objects ??= objects();

    public void Refresh()
    {
        _settlers = null;
        _objects = null;
        Changed?.Invoke();
    }
}
