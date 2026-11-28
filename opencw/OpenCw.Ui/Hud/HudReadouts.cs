using System;
using OpenCommonwealth.Services.Ui.Records;

namespace OpenCommonwealth.Services.Ui.Hud;

public sealed class HudExperience
{
    public const double PopupSeconds = 4;
    public const double FadeSeconds = 0.4;

    private double _time;
    private bool _known;
    private bool _gained;

    public int Level { get; private set; } = 1;

    public double Progress { get; private set; }

    public long LastGain { get; private set; }

    public bool LevelUp { get; private set; }

    public bool Active => _time > 0;

    public double Alpha => _time <= 0 ? 0 : Math.Min(1, _time / FadeSeconds);

    public void Sync(int level, double progress)
    {
        if (!double.IsFinite(progress))
            return;
        var next = Math.Max(1, level);
        if (_known && _gained && next > Level)
        {
            LevelUp = true;
            _time = PopupSeconds;
        }
        Level = next;
        Progress = Math.Clamp(progress, 0, 1);
        _known = true;
        _gained = false;
    }

    public void Gain(long amount)
    {
        if (amount <= 0)
            return;
        LastGain = amount;
        _gained = true;
        _time = PopupSeconds;
    }

    public void Tick(double dt)
    {
        if (!(dt > 0) || _time <= 0)
            return;
        _time = Math.Max(0, _time - dt);
        if (_time == 0)
            LevelUp = false;
    }
}

public sealed class HudLocation
{
    public const double ShowSeconds = 5;
    public const double FadeSeconds = 0.5;

    private double _time;
    private bool _seen;

    public string Name { get; private set; } = "";

    public double Alpha => _time <= 0 ? 0 : Math.Min(1, Math.Min(_time, ShowSeconds - _time) / FadeSeconds);

    public void Set(string? name)
    {
        var next = name?.Trim() ?? "";
        if (next == Name)
            return;
        Name = next;
        _time = next.Length > 0 && _seen ? ShowSeconds : 0;
        _seen |= next.Length > 0;
    }

    public void Announce(string? name)
    {
        Name = name?.Trim() ?? "";
        _time = Name.Length > 0 ? ShowSeconds : 0;
        _seen |= Name.Length > 0;
    }

    public void Tick(double dt)
    {
        if (dt > 0)
            _time = Math.Max(0, _time - dt);
    }
}

public static class HudEnemyRules
{
    public const string SkullSetting = "iHUDEnemyHealthSkullIconLevelDifference";

    public static int? ReadSkullDifference(IRecordReader reader)
    {
        try { return GmstSettings.Int(reader, SkullSetting); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    public static bool ShowsSkull(int enemyLevel, int playerLevel, int? levelDifference) =>
        levelDifference is { } diff && enemyLevel - playerLevel >= diff;

    public const string LegendaryKeyword = "EncTypeLegendary";

    // Requires: npcId is the base NPC_ key.
    public static bool IsLegendary(IRecordReader reader, string npcId) =>
        reader.FindEditorId(LegendaryKeyword, "KYWD") is { } keyword && reader.FormLinks(npcId, "KWDA").Contains(keyword, StringComparer.OrdinalIgnoreCase);
}

public static class HudEffectIcon
{
    public const int None = 1;
    public const int Disease = 50;
    public const int Hunger = 52;
    public const int Thirst = 53;
    public const int Chems = 54;
    public const int Max = 59;

    public static readonly int[] Used = { Disease, Hunger, Thirst, Chems };
}
