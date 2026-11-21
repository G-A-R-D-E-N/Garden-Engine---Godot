using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Combat;
using OpenCommonwealth.Services.Ui.Character;

namespace OpenCommonwealth.Services.Ui.PipBoy;

// Constraint: ReadOnly holds until an aid supply is injected; reports only what the game stores; every unknown stat is null or empty so the model omits it rather than inventing a value.
public sealed class LivePlayerStatus(IReadOnlyList<OwnedPerk>? perks = null, Func<AidKind, AidSupply?>? aid = null, PlayerVitals? vitals = null,
    IReadOnlyList<SpecialEntry>? special = null, PlayerLimbs? limbs = null, bool unreportedLimbsFull = false) : IPlayerStatusSource
{
    public string? PlayerName => PlayerCharacter.Current.Name;

    public double? Health => vitals?.Health;

    public double? MaxHealth => vitals?.MaxHealth;

    public double? PendingHealthGain => vitals?.PendingHealth;

    public double? Radiation => vitals?.RadiationFraction;

    public IReadOnlyList<DamageTotal>? DamageTotals => null;

    public IReadOnlyList<DamageTotal>? ResistTotals => null;

    public AidSupply? Stimpak => aid?.Invoke(AidKind.Stimpak);

    public AidSupply? RadAway => aid?.Invoke(AidKind.RadAway);

    public IReadOnlyList<EffectSource> Effects => LiveBuffEffects.Sources(vitals);

    public IReadOnlyList<SpecialEntry> Special { get; } = special ?? [];

    public IReadOnlyList<OwnedPerk> Perks { get; } = perks ?? [];

    public bool ReadOnly => aid is null;

    public LimbCondition? Limb(LimbKind limb) => limbs?.Get(limb) ?? (unreportedLimbsFull && limbs is not null ? new LimbCondition(1) : null);
}
