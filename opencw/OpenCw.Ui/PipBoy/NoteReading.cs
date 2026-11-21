using System;

namespace OpenCommonwealth.Services.Ui.PipBoy;

public static class NoteReading
{
    public const string NothingToPlay = "nothing to play";

    // Invariant: a terminal target wins over a sound target; a terminal that does not open refuses with its reason and never falls back to the sound.
    public static InventoryActionResult Read(string name, string? terminalKey, bool hasSound, Func<string, string?> openTerminal, Action playSound)
    {
        if (terminalKey is not null)
            return openTerminal(terminalKey) is { } reason
                ? new InventoryActionResult(InventoryActionStatus.Refused, reason)
                : new InventoryActionResult(InventoryActionStatus.Done, "opened " + name);
        if (!hasSound) return new InventoryActionResult(InventoryActionStatus.Refused, NothingToPlay);
        playSound();
        return new InventoryActionResult(InventoryActionStatus.Done, "playing " + name);
    }
}
