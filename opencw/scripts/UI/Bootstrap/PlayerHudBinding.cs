using Godot;
using System.Collections.Generic;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Ui.Records;
using OpenCommonwealth.Services.Ui.Settings;

namespace OpenCommonwealth.UI.Bootstrap;

public static class PlayerHudBinding
{
    private const float EnemyRange = 25f;
    private const ulong LocationPollMs = 1000;
    private const ulong EnemyPollMs = 250;

    private static readonly List<int> EffectFrames = new();
    private static ulong _locationAt;
    private static Node3D? _enemy;
    private static string _enemyName = "";
    private static bool _enemyLegendary;
    private static ActorRelation _enemyRelation;
    private static ulong _enemyPollAt;

    // Requires: Health stays hidden until PlayerHealth.Current exists, so the bar never shows a made-up value.
    public static void Configure(HudState state, PlayerController player)
    {
        state.Settings.SetEnabled(HudElement.Health, false);
        Vitals.Reset();
        if (player.GetParent() is TestLevelHud)
            state.Settings.SetEnabled(HudElement.Crosshair, false);
    }

    private static readonly HudVitalsFeed Vitals = new();

    public static void Push(HudState state, PlayerController player, KeyBindingModel keys)
    {
        Vitals.Push(state, PlayerHealth.Current);
        state.SetActionPoints(player.ActionPoints, player.MaxActionPoints);
        player.Weapons.SyncHud(state);
        PushEnemy(state, player);
        PushEffects(state);
        if (Time.GetTicksMsec() - _locationAt > LocationPollMs)
        {
            _locationAt = Time.GetTicksMsec();
            if (PipBoyStatusFeed.Location(player) is { } place) state.Location.Set(place);
        }
        state.SetHeading(-Mathf.RadToDeg(player.Rotation.Y));
        if (player.AimedPrompt() is { } prompt && player.CarriedName().Length == 0)
        {
            state.SetInteraction(prompt.Verb, prompt.Target, keys.Get("interact", InputDevice.Keyboard, BindingSlot.Primary) ?? "E",
                player.AimedHoldProgress(), prompt.Icon, prompt.Notice);
            if (state.Crosshair == CrosshairState.Default) state.Crosshair = CrosshairState.Interactable;
        }
        else
        {
            state.ClearInteraction();
            if (state.Crosshair == CrosshairState.Interactable) state.Crosshair = CrosshairState.Default;
        }
    }

    // Constraint: ActiveBuff carries no item category, so every timed buff shows the chem icon frame.
    private static void PushEffects(HudState state)
    {
        EffectFrames.Clear();
        for (var i = 0; i < (PlayerHealth.Current?.Buffs.Count ?? 0); i++)
            EffectFrames.Add(HudEffectIcon.Chems);
        state.Instruments.SetEffectIcons(EffectFrames);
    }

    // Constraint: the long aim pick and the relation lookup run at most every EnemyPollMs, so a changed relation shows within one poll.
    private static void PushEnemy(HudState state, PlayerController player)
    {
        var now = Time.GetTicksMsec();
        if (now - _enemyPollAt >= EnemyPollMs)
        {
            _enemyPollAt = now;
            PollEnemy(player);
        }
        var actor = _enemy is not null && GodotObject.IsInstanceValid(_enemy) ? ActorCombat.Of(_enemy) : null;
        if (_enemyRelation != ActorRelation.Enemy || actor is null || actor.State.IsDead || actor.State.Health is not { } health || actor.State.MaxHealth is not > 0)
        {
            state.Instruments.ClearEnemy();
            return;
        }
        var level = actor.State.LevelUnresolved is null ? (int?)actor.State.Level : null;
        PlayerXp.EnsureCurve();
        state.Instruments.SetEnemy(_enemyName, health / actor.State.MaxHealth.Value);
        state.Instruments.SetEnemyDetails(level, _enemyLegendary, level is { } l && HudEnemyRules.ShowsSkull(l, PlayerExperience.Current.Level, PlayerXp.SkullLevelDifference));
    }

    private static void PollEnemy(PlayerController player)
    {
        var node = player.AimedAt(EnemyRange);
        if (node is null || !GodotObject.IsInstanceValid(node) || ActorCombat.Of(node) is not { } actor)
        {
            _enemy = null;
            _enemyRelation = ActorRelation.Unknown;
            return;
        }
        _enemyRelation = ActorRegistry.Player is { } self ? ActorRelations.Source.Between(actor.Identity, self) : ActorRelation.Unknown;
        if (node != _enemy)
        {
            _enemyName = ActorName(actor, node);
            _enemyLegendary = IsLegendary(actor);
        }
        _enemy = node;
    }

    private static bool IsLegendary(ActorCombat actor)
    {
        GameLoader.EnvLifetime.EnterReadLock();
        try { return GameLoader.HasLoadOrder && UiRecordSources.Current()?.Records is { } records && HudEnemyRules.IsLegendary(records, actor.Identity.NpcKey); }
        catch (System.Exception) { return false; }
        finally { GameLoader.EnvLifetime.ExitReadLock(); }
    }

    private static string ActorName(ActorCombat actor, Node3D node)
    {
        GameLoader.EnvLifetime.EnterReadLock();
        try { return GameLoader.HasLoadOrder && UiRecordSources.Current()?.Records.Name(actor.Identity.NpcKey) is { Length: > 0 } name ? name : node.Name.ToString(); }
        catch (System.Exception) { return node.Name.ToString(); }
        finally { GameLoader.EnvLifetime.ExitReadLock(); }
    }
}
