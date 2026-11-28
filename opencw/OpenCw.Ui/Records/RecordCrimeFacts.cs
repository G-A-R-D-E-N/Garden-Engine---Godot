using System;
using System.Globalization;
using OpenCommonwealth.Services.Ui.Trade;

namespace OpenCommonwealth.Services.Ui.Records;

// Requires: every answer is "unknown" (no faction, value 0, no multiplier) when the sources or a record cannot be read; a crime report must never throw into a pickup.
public sealed class RecordCrimeFacts(Func<UiDataSources?> sources, Func<string, string?>? npcFaction = null) : ICrimeFacts
{
    public const string FactionSignature = "FACT";
    public const string StealSetting = "fCrimeGoldSteal";
    public const string IntegerPath = "GMST\\IntegerValue";
    public const string FactionFlagsPath = "FACT\\Flags";
    public const string FactionStealPath = "FACT\\CrimeStealMultiplier";
    public const string FactionPickpocketPath = "FACT\\CrimePickpocket";
    public const uint UseDefaultsFlag = 1u << 12;
    public const uint IgnoreStealingFlag = 1u << 9;
    public const uint IgnorePickpocketFlag = 1u << 13;

    // Invariant: an unreadable flags row reads as not ignoring, so the witness still counts.
    public static bool IgnoresStealing(IRecordReader reader, string faction) =>
        uint.TryParse(reader.Scalar(faction, FactionFlagsPath), NumberStyles.None, CultureInfo.InvariantCulture, out var flags) && (flags & IgnoreStealingFlag) != 0;

    public static bool IgnoresPickpocket(IRecordReader reader, string faction) =>
        uint.TryParse(reader.Scalar(faction, FactionFlagsPath), NumberStyles.None, CultureInfo.InvariantCulture, out var flags) && (flags & IgnorePickpocketFlag) != 0;

    // Requires: set once at start-up by the host that can resolve an NPC_ base key or an actor reference key to its crime faction; null means NPC owners have no faction.
    public static Func<string, string?>? DefaultNpcFaction { get; set; }

    public string? FactionOf(string owner)
    {
        try
        {
            if (sources()?.Records.Signature(owner) == FactionSignature) return owner;
            return (npcFaction ?? DefaultNpcFaction)?.Invoke(owner);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public int ValueOf(string item)
    {
        try { return Math.Max(sources()?.Items.Find(item)?.Value ?? 0, 0); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return 0; }
    }

    public double? StealMultiplier
    {
        get
        {
            try
            {
                if (sources() is not { } held) return null;
                var value = PickpocketSettings.Value(held.Records, StealSetting, float.NaN);
                return float.IsFinite(value) ? value : null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        }
    }

    // Invariant: UNVERIFIED; a faction whose "Crime Gold - Use Defaults" flag is set takes the game setting, any other takes its own CRVA steal multiplier, and unreadable rows answer unknown.
    public double? StealMultiplierFor(string faction)
    {
        try
        {
            if (sources() is not { } held) return null;
            if (!uint.TryParse(held.Records.Scalar(faction, FactionFlagsPath), NumberStyles.None, CultureInfo.InvariantCulture, out var flags)) return null;
            if ((flags & UseDefaultsFlag) != 0) return StealMultiplier;
            return float.TryParse(held.Records.Scalar(faction, FactionStealPath), NumberStyles.Float, CultureInfo.InvariantCulture, out var own) && float.IsFinite(own) ? own : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public int? PickpocketCrimeGold(string faction)
    {
        try
        {
            if (sources() is not { } held) return null;
            return ushort.TryParse(held.Records.Scalar(faction, FactionPickpocketPath), NumberStyles.None, CultureInfo.InvariantCulture, out var gold) ? gold : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public bool FactionIgnoresPickpocket(string faction)
    {
        try { return sources() is { } held && IgnoresPickpocket(held.Records, faction); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }

    public CrimeSettings? Settings
    {
        get
        {
            try
            {
                if (sources() is not { } held) return null;
                var reader = held.Records;
                return new CrimeSettings(Integer(reader, "iCrimeGoldAttack"), Integer(reader, "iCrimeGoldMurder"), Integer(reader, "iCrimeGoldPickpocket"),
                    Integer(reader, "iCrimeGoldTrespass"), Integer(reader, "iCrimeGoldStealHorse"), Integer(reader, "iCrimeGoldMinValue"), Integer(reader, "iCrimeGoldEscape"));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        }
    }

    // Invariant: an absent record or an unparsable value is null; there is no fallback number.
    internal static int? Integer(IRecordReader reader, string editorId) =>
        reader.FindEditorId(editorId, "GMST") is { } id
        && int.TryParse(reader.Scalar(id, IntegerPath), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
