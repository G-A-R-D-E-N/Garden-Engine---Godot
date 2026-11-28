using System.Collections.Generic;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public enum PipBoyTab
{
    Stats,
    Inventory,
    Data,
    Map,
    Radio,
}

public enum PipBoyPhase
{
    Closed,
    Opening,
    Open,
    Closing,
}

public abstract record PipBoyEvent;

public sealed record PipBoyPhaseChanged(PipBoyPhase Phase) : PipBoyEvent;

public sealed record PipBoyTabChanged(PipBoyTab From, PipBoyTab To) : PipBoyEvent;

public sealed record PipBoySearchHit(PipBoyTab Tab, string Key, string Title, string Detail);

public sealed record PipBoySearchGroup(PipBoyTab Tab, IReadOnlyList<PipBoySearchHit> Hits);

public sealed record PipBoySavedState(PipBoyTab Tab, IReadOnlyDictionary<PipBoyTab, string> Cursors);

public interface IPipBoyTabSource
{
    PipBoyTab Tab { get; }

    string? CurrentKey { get; }

    bool Select(string key);

    IReadOnlyList<PipBoySearchHit> Search(IReadOnlyList<string> tokens);
}

public interface IPipBoyJumpSource : IPipBoyTabSource
{
    bool Jump(string key);
}
