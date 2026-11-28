using OpenCommonwealth.Services.Ui.Hud;

namespace OpenCommonwealth.Services.Ui.Swf;

public static class HudArt
{
    public const string Movie = "Interface/HUDMenu.swf";

    public const string Health = "LeftMeters_mc/HPMeter_mc";
    public const string ActionPoints = "RightMeters_mc/ActionPointMeter_mc";
    public const string Ammo = "RightMeters_mc/AmmoCount_mc";
    public const string Explosive = "RightMeters_mc/ExplosiveAmmoCount_mc";
    public const string Enemy = "TopCenterGroup_mc/EnemyHealthMeter_mc";
    public const string Stealth = "TopCenterGroup_mc/StealthMeter_mc";
    public const string CrosshairBase = "CenterGroup_mc/HUDCrosshair_mc/CrosshairBase_mc";
    public const string Rollover = "CenterGroup_mc/RolloverWidget_mc";
    public const string Notifications = "HUDNotificationsGroup_mc";
    public const string Experience = Notifications + "/XPMeter_mc";
    public const string Messages = Notifications + "/Messages_mc";
    public const string Location = "LeftMeters_mc/LocationText_mc";
    public const string Effects = "RightMeters_mc/HUDActiveEffectsWidget_mc";
    public const string Crit = "BottomCenterGroup_mc/CritMeter_mc";
    public const string PowerArmorWarning = "RightMeters_mc/PowerArmorLowBatteryWarning_mc";
    public const string CritFullTemplate = "crit_star_full";
    public const string CritEmptyTemplate = "crit_star_empty";
    public const string EffectTemplate = "fx_icon";
    public const ushort EffectIconCharacter = 388;
    public const double ApPreviewDim = 0.45;
    public const double ToastPitch = 26;
    public const double ToastWidth = 420;
    public const int ToastFontSize = 20;
    public const double ToastCharWidth = 0.45 * ToastFontSize * SwfTextScale.Factor;
    public const double CritStarSpacing = 22;
    public const double EffectPitch = 25.66;
    public const double EffectScale = 0.6;
    public const double EffectOrigin = 11.52;
    public const string ActivateTemplate = "crosshair_activate";
    public const string DotTemplate = "crosshair_dot";
    public const double RadsBarWidth = 200;
    public const double ActionPointsBarWidth = 202;

    private static readonly string[] Groups =
    {
        "LeftMeters_mc", "RightMeters_mc", "BottomCenterGroup_mc", "CenterGroup_mc", "TopCenterGroup_mc", "HUDNotificationsGroup_mc",
    };

    private static readonly string[] NotificationChildren =
    {
        "/ObjectiveUpdates_mc", "/PromptMessageHolder_mc", "/QuestUpdates_mc", "/QuestVaultBoy_mc", "/TutorialText_mc",
    };

    private static readonly string[] ExperienceParts =
    {
        "", "/LevelUPBar", "/LevelUpBracket", "/NumberText", "/PlusSign", "/xptext",
    };

    private static readonly string[] Hidden =
    {
        "RightMeters_mc/FlashLightWidget_mc", "RightMeters_mc/FatigueWarning_mc", "BottomCenterGroup_mc/SubtitleText_mc",
        "BottomCenterGroup_mc/PerkVaultBoy_mc", "LeftMeters_mc/RadsMeter_mc",
        "LeftMeters_mc/HPMeter_mc/Optional_mc", "CenterGroup_mc/QuickContainerWidget_mc",
        "CenterGroup_mc/HitIndicator_mc", "CenterGroup_mc/DirectionalHitIndicatorBase_mc", "CenterGroup_mc/ExplosiveIndicatorBase_mc",
        "CenterGroup_mc/HUDCrosshair_mc/CrosshairBase_mc/CrosshairClips_mc",
        Enemy + "/Optional_mc",
        Rollover + "/LegendaryIcon_mc", Rollover + "/TaggedForSearchIcon_mc",
    };

    public static IEnumerable<SwfTemplate> Templates()
    {
        foreach (var t in HudCompassArt.Templates())
            yield return t;
        yield return new SwfTemplate(ActivateTemplate, null, CrosshairBase + "/CrosshairClips_mc/Activate_Activate", 1);
        yield return new SwfTemplate(DotTemplate, null, CrosshairBase + "/CrosshairClips_mc/Dot_Dot", 1);
        yield return new SwfTemplate(CritEmptyTemplate, "CritMeterStar", null, 0);
        yield return new SwfTemplate(CritFullTemplate, "CritMeterStar", null, 1);
        foreach (var frame in HudEffectIcon.Used)
            yield return new SwfTemplate(EffectTemplate + frame, null, null, frame - 1, EffectIconCharacter);
    }

    public static SwfBindings Build(HudState s) => Build(s, HudStagePlacement.StageWidth, HudStagePlacement.StageHeight, null);

    public static SwfBindings Build(HudState s, double areaWidth, double areaHeight, CompassModel? compass)
    {
        var b = new SwfBindings();
        if (s.Hidden)
        {
            foreach (var g in Groups)
                b.Hide.Add(g);
            return b;
        }
        foreach (var p in Hidden)
            b.Hide.Add(p);
        foreach (var (element, entry) in HudStagePlacement.Elements)
        {
            if (!s.IsShown(element))
                b.Hide.Add(entry.Path);
            else if (HudStagePlacement.Compute(element, s.Settings, areaWidth, areaHeight) is { IsIdentity: false } place)
                b.Place[entry.Path] = place;
        }
        if (HudStagePlacement.ComputeTop(s.Settings, areaWidth, areaHeight) is { IsIdentity: false } top)
            b.Place[HudStagePlacement.TopPath] = top;
        AddVitals(b, s);
        AddAmmo(b, s, areaWidth, areaHeight);
        AddCompass(b, s, compass);
        AddCrosshair(b, s);
        AddInteraction(b, s);
        AddTop(b, s);
        AddCritical(b, s, areaWidth, areaHeight);
        AddLocation(b, s, areaWidth, areaHeight);
        AddEffects(b, s, areaWidth, areaHeight);
        AddNotifications(b, s);
        AddPowerArmor(b, s, areaWidth, areaHeight);
        return b;
    }

    private static void Follow(SwfBindings b, HudState s, string path, HudElement element, double areaWidth, double areaHeight)
    {
        if (HudStagePlacement.Compute(element, s.Settings, areaWidth, areaHeight) is { IsIdentity: false } place)
            b.Place[path] = place;
    }

    private static void AddVitals(SwfBindings b, HudState s)
    {
        var rads = s.Instruments.Radiation;
        var radsBar = Health + "/RadsBar_mc";
        if (s.IsShown(HudElement.Health) && rads > 0)
        {
            b.Show.Add(radsBar);
            b.FillRight[radsBar + "/MeterBarInternal_mc"] = new SwfFill(Math.Clamp(rads, 0, 1), RadsBarWidth);
        }
        else
            b.Hide.Add(radsBar);
        b.FillX[Health + "/MeterBar_mc/MeterBarInternal_mc"] = s.HealthFraction;
        b.FillRight[ActionPoints + "/MeterBar_mc/MeterBarInternal_mc"] = new SwfFill(s.ActionPointsFraction, ActionPointsBarWidth);
        b.Alpha[ActionPoints] = s.ActionPointsAlpha;
        var preview = ActionPoints + "/Optional_mc";
        if (s.Instruments.ActionPointPreview is { } projected && s.ActionPointsMax > 0)
        {
            b.Show.Add(preview);
            b.FillRight[preview + "/MeterBarInternal_mc"] = new SwfFill(Math.Clamp(projected / s.ActionPointsMax, 0, 1), ActionPointsBarWidth);
            b.Alpha[ActionPoints + "/MeterBar_mc"] = ApPreviewDim;
        }
        else
            b.Hide.Add(preview);
    }

    private static void AddAmmo(SwfBindings b, HudState s, double areaWidth, double areaHeight)
    {
        var ammo = s.Ammo;
        if (!s.IsShown(HudElement.Ammo))
            b.Hide.Add(Ammo);
        b.Text[Ammo + "/ClipCount_tf"] = SwfBindings.Number(ammo.Clip, 3);
        b.Text[Ammo + "/ReserveCount_tf"] = ammo.Infinite ? "--" : SwfBindings.Number(ammo.Reserve);
        if (s.Instruments.SecondaryCount is { } count && s.Instruments.IsGroupVisible(HudGroup.Ammo))
        {
            b.Show.Add(Explosive);
            b.Text[Explosive + "/AvailableCount_tf"] = SwfBindings.Number(count, 2);
            if (HudStagePlacement.Compute(HudElement.Ammo, s.Settings, areaWidth, areaHeight) is { IsIdentity: false } place)
                b.Place[Explosive] = place;
        }
        else
            b.Hide.Add(Explosive);
    }

    private static void AddCompass(SwfBindings b, HudState s, CompassModel? compass)
    {
        if (!s.IsShown(HudElement.Compass))
            return;
        compass ??= new CompassModel();
        compass.Update(s.CompassHeading, s.Markers);
        HudCompassArt.Add(b, compass);
    }

    private static void AddCrosshair(SwfBindings b, HudState s)
    {
        if (!s.IsShown(HudElement.Crosshair))
            return;
        var ticks = CrosshairBase + "/CrosshairTicks_mc";
        switch (s.Crosshair)
        {
            case CrosshairState.Interactable:
                b.Hide.Add(ticks);
                b.Sprites.Add(new SwfSprite(ActivateTemplate, CrosshairBase, 0, 0, 1));
                break;
            case CrosshairState.Sneaking:
                b.Hide.Add(ticks);
                b.Sprites.Add(new SwfSprite(DotTemplate, CrosshairBase, 0, 0, 1));
                break;
            default:
                b.Show.Add(ticks);
                foreach (var arm in new[] { "Left", "Up", "Right", "Down" })
                    b.Show.Add(ticks + "/" + arm);
                break;
        }
    }

    private static void AddInteraction(SwfBindings b, HudState s)
    {
        if (!s.IsShown(HudElement.Interaction) || s.Interaction is not { } prompt)
            return;
        foreach (var part in new[] { "", "/ButtonHintBar_mc", "/ButtonHintBar_mc/ButtonBracket_Left_mc", "/ButtonHintBar_mc/ButtonBracket_Right_mc", "/RolloverName_tf" })
            b.Show.Add(Rollover + part);
        b.Text[Rollover + "/RolloverName_tf"] = prompt.Target.Length > 0 ? prompt.Target : prompt.Verb;
        if (prompt.KeyLabel.Length > 0)
            b.Sprites.Add(new SwfSprite("", Rollover, 0, 0, 1, new SwfSpriteText(prompt.KeyLabel, -16, 20, 32, 28, 20, true)));
        if (prompt.Target.Length > 0 && prompt.Verb.Length > 0)
            b.Sprites.Add(new SwfSprite("", Rollover, 0, 0, 1, new SwfSpriteText(prompt.Verb, -150, 62, 300, 28, 20, false)));
        if (prompt.HoldProgress > 0)
            b.Sprites.Add(new SwfSprite("", Rollover, 0, 0, 1, null, new SwfSpriteBar(-80, 94, 160, 4, prompt.HoldProgress)));
        if (prompt.Notice.Length > 0)
            b.Sprites.Add(new SwfSprite("", Rollover, 0, 0, 1, new SwfSpriteText(prompt.Notice, -160, 102, 320, 28, 18, false)));
    }

    private static void AddTop(SwfBindings b, HudState s)
    {
        var src = s.Instruments;
        var centre = src.IsGroupVisible(HudGroup.Centre);
        if (centre && src.EnemyAlpha > 0)
        {
            b.Show.Add(Enemy);
            b.Alpha[Enemy] = src.EnemyAlpha;
            b.Text[Enemy + "/DisplayText_tf"] = src.EnemyName.ToUpperInvariant();
            b.FillX[Enemy + "/MeterBar_mc/MeterBarInternal_mc"] = Math.Clamp(src.EnemyHealth, 0, 1);
            b.Show.Add(HudStagePlacement.TopPath);
            if (src.EnemyLegendary)
                b.Show.Add(Enemy + "/LegendaryIcon_mc");
            else
                b.Hide.Add(Enemy + "/LegendaryIcon_mc");
            if (src.EnemySkull)
                b.Show.Add(Enemy + "/SkullIcon_mc");
            else
                b.Hide.Add(Enemy + "/SkullIcon_mc");
        }
        else
            b.Hide.Add(Enemy);
        if (centre && src.SneakText.Length > 0)
        {
            b.Show.Add(HudStagePlacement.TopPath);
            b.Show.Add(Stealth);
            b.Text[Stealth + "/StealthTextInstance"] = src.SneakText.ToUpperInvariant();
            var gap = Math.Clamp(src.SneakGap, 0, HudInstruments.MaxSneakGap);
            b.Place[Stealth + "/BracketLeftInstance"] = new SwfPlace(1, -gap, 0);
            b.Place[Stealth + "/BracketRightInstance"] = new SwfPlace(1, gap, 0);
        }
        else
            b.Hide.Add(Stealth);
    }

    private static void AddCritical(SwfBindings b, HudState s, double areaWidth, double areaHeight)
    {
        var src = s.Instruments;
        if (!src.IsGroupVisible(HudGroup.Centre) || !s.IsShown(HudElement.Compass) || !src.CriticalVisible)
        {
            b.Hide.Add(Crit);
            return;
        }
        b.Show.Add(Crit);
        b.Alpha[Crit] = src.CriticalAlpha;
        b.FillX[Crit + "/MeterBar_mc/MeterBarInternal_mc"] = src.CriticalFraction;
        Follow(b, s, Crit, HudElement.Compass, areaWidth, areaHeight);
        for (var i = 0; i < HudInstruments.MaxCharges; i++)
            b.Sprites.Add(new SwfSprite(i < src.CriticalCharges ? CritFullTemplate : CritEmptyTemplate, Crit + "/CritMeterStars_mc", i * CritStarSpacing, 0, 1));
    }

    private static void AddLocation(SwfBindings b, HudState s, double areaWidth, double areaHeight)
    {
        if (!s.Instruments.IsGroupVisible(HudGroup.Vitals) || s.Location.Alpha <= 0)
        {
            b.Hide.Add(Location);
            return;
        }
        b.Show.Add(Location);
        b.Alpha[Location] = s.Location.Alpha;
        b.Text[Location + "/RegionText"] = s.Location.Name;
        Follow(b, s, Location, HudElement.Health, areaWidth, areaHeight);
    }

    private static void AddEffects(SwfBindings b, HudState s, double areaWidth, double areaHeight)
    {
        var icons = s.Instruments.EffectIcons;
        if (!s.Instruments.IsGroupVisible(HudGroup.Effects) || !s.IsShown(HudElement.ActionPoints) || icons.Count == 0)
            return;
        b.Show.Add(Effects);
        Follow(b, s, Effects, HudElement.ActionPoints, areaWidth, areaHeight);
        for (var i = 0; i < icons.Count; i++)
            b.Sprites.Add(new SwfSprite(EffectTemplate + icons[i], Effects, s.EffectsOffsetX + EffectOrigin, s.EffectsOffsetY + EffectOrigin + i * EffectPitch, 1, null, null, EffectScale));
    }

    private static void AddNotifications(SwfBindings b, HudState s)
    {
        foreach (var child in NotificationChildren)
            b.Hide.Add(Notifications + child);
        var xp = s.Experience;
        var toasts = s.Toasts?.Visible ?? Array.Empty<ToastView>();
        if (!s.IsShown(HudElement.Toasts) || !xp.Active && toasts.Count == 0)
        {
            b.Hide.Add(Notifications);
            return;
        }
        b.Show.Add(Notifications);
        if (xp.Active)
        {
            foreach (var part in ExperienceParts)
                b.Show.Add(Experience + part);
            b.Alpha[Experience] = xp.Alpha;
            b.Text[Experience + "/NumberText"] = xp.LastGain.ToString(System.Globalization.CultureInfo.InvariantCulture);
            b.FillX[Experience + "/LevelUPBar/MeterBarInternal_mc"] = xp.Progress;
            if (xp.LevelUp)
            {
                b.Show.Add(Experience + "/LeveUpTextClip");
                b.Text[Experience + "/LeveUpTextClip/textField"] = "LEVEL " + xp.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
                b.Hide.Add(Experience + "/LeveUpTextClip");
        }
        else
            b.Hide.Add(Experience);
        b.Show.Add(Messages);
        var row = 0.0;
        for (var i = 0; i < toasts.Count; i++)
        {
            var text = toasts[i].DisplayText;
            var lines = ToastLines(text);
            b.Sprites.Add(new SwfSprite("", Messages, 0, row, toasts[i].Alpha, new SwfSpriteText(text, 0, 0, ToastWidth, lines * ToastPitch + 2, ToastFontSize, false, Align: SwfAlign.Left, Wrap: true)));
            row += lines * ToastPitch;
        }
    }

    public static int ToastLines(string text)
    {
        var per = Math.Max(1, (int)(ToastWidth / ToastCharWidth));
        var lines = 0;
        foreach (var line in text.Split('\n'))
        {
            lines++;
            var used = 0;
            foreach (var word in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                for (var rest = word.Length; rest > 0; rest -= per)
                {
                    var piece = Math.Min(rest, per);
                    var need = used == 0 ? piece : used + 1 + piece;
                    if (need > per)
                    {
                        lines++;
                        need = piece;
                    }
                    used = need;
                }
            }
        }
        return lines;
    }

    private static void AddPowerArmor(SwfBindings b, HudState s, double areaWidth, double areaHeight)
    {
        var pa = s.PowerArmor;
        var holder = PowerArmorWarning + "/WarningTextHolder_mc";
        if (!s.Instruments.IsGroupVisible(HudGroup.PowerArmor) || !pa.WarningVisible)
        {
            b.Hide.Add(PowerArmorWarning);
            return;
        }
        b.Show.Add(PowerArmorWarning);
        b.Show.Add(holder);
        b.Alpha[PowerArmorWarning] = pa.WarningAlpha;
        b.Text[holder + "/PowerArmorLowBatteryWarning_tf"] = pa.WarningText;
        Follow(b, s, PowerArmorWarning, HudElement.Ammo, areaWidth, areaHeight);
    }
}
