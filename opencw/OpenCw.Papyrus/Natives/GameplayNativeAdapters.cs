using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Items;
using OpenCommonwealth.Services.Papyrus.Magic;
using OpenCommonwealth.Services.Papyrus.Owners;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

/// <summary>A gameplay native family and the Garden system that owns it.</summary>
public sealed record GameplayNativeFamily(string Family, string OwningSystem, string Key, NativeId Native);

public enum AdapterState
{
    /// <summary>The owning Garden system exists, so the native was bound.</summary>
    Bound,

    /// <summary>The owning Garden system does not exist, so the native stays unbound.</summary>
    AbsentSystem,

    Unimplemented,

    Conflict,
}

public sealed record AdapterOutcome(GameplayNativeFamily Family, AdapterState State, string Reason);

public sealed record AdapterReport(IReadOnlyList<AdapterOutcome> Natives, int Bound, int Unbound, IReadOnlyList<string> AbsentSystems)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"natives={Natives.Count} bound={Bound} unbound={Unbound} absent-systems={AbsentSystems.Count}\n");
        foreach (var outcome in Natives)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Family.Native} family={outcome.Family.Family} system={outcome.Family.OwningSystem} {outcome.State.ToString().ToLowerInvariant()} {outcome.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

public sealed class GameplayNativeAdapters
{
    public const string SystemKeyPrefix = "system.";

    private readonly EvidenceGate _gate;
    private readonly IScriptHost _host;
    private readonly InventoryRuntime _inventory;
    private readonly MagicPerkRuntime _magic;

    public GameplayNativeAdapters(
        IScriptHost host,
        InventoryRuntime inventory,
        MagicPerkRuntime magic,
        EvidenceGate gate)
    {
        _gate = gate;
        _host = host;
        _inventory = inventory;
        _magic = magic;
    }

    public int Bound { get; private set; }

    public int Unbound { get; private set; }

    /// <summary>The gameplay native families Garden maps, each with its owning system.</summary>
    public static IReadOnlyList<GameplayNativeFamily> Catalogue { get; } = new[]
    {
        new GameplayNativeFamily("inventory", "Garden.Inventory", "system.inventory", new NativeId("ObjectReference", "AddItem")),
        new GameplayNativeFamily("inventory", "Garden.Inventory", "system.inventory", new NativeId("ObjectReference", "RemoveItem")),
        new GameplayNativeFamily("inventory", "Garden.Inventory", "system.inventory", new NativeId("ObjectReference", "GetItemCount")),
        new GameplayNativeFamily("inventory", "Garden.Inventory", "system.inventory", new NativeId("Actor", "EquipItem")),
        new GameplayNativeFamily("perk", "Garden.MagicPerk", "system.magic_perk", new NativeId("Actor", "AddPerk")),
        new GameplayNativeFamily("actorvalue", "Garden.ActorValues", "system.actor_values", new NativeId("Actor", "GetValue")),
        new GameplayNativeFamily("faction", "Garden.Factions", "system.factions", new NativeId("Actor", "GetFactionRank")),
        new GameplayNativeFamily("ai", "Garden.AI", "system.ai", new NativeId("Actor", "SetPackage")),
        new GameplayNativeFamily("movement", "Garden.Movement", "system.movement", new NativeId("Actor", "TranslateTo")),
        new GameplayNativeFamily("combat", "Garden.Combat", "system.combat", new NativeId("Actor", "StartCombat")),
        new GameplayNativeFamily("crime", "Garden.Crime", "system.crime", new NativeId("Actor", "GetCrimeFaction")),
    };

    public AdapterReport Bind()
    {
        var outcomes = new List<AdapterOutcome>(Catalogue.Count);
        var absent = new List<string>();
        var unbound = 0;
        var bound = 0;

        foreach (var family in Catalogue.OrderBy(family => family.Native.ToString(), StringComparer.Ordinal))
        {
            if (!_gate.Gate(family.Key).Allowed)
            {
                unbound++;
                Unbound++;
                if (!absent.Contains(family.OwningSystem, StringComparer.Ordinal))
                {
                    absent.Add(family.OwningSystem);
                }
                outcomes.Add(new AdapterOutcome(family, AdapterState.AbsentSystem,
                    $"the owning system {family.OwningSystem} is not available, so the native stays unbound"));
                continue;
            }

            if (family.Family is "inventory" or "perk")
            {
                if (!_host.TryRegisterNative(family.Native,
                    (receiver, arguments) => Apply(family, receiver, arguments), NativeTargetKind.EngineObject))
                {
                    unbound++;
                    Unbound++;
                    outcomes.Add(new AdapterOutcome(family, AdapterState.Conflict,
                        "registration conflict: the existing handler was preserved"));
                    continue;
                }
                bound++;
                Bound++;
                outcomes.Add(new AdapterOutcome(family, AdapterState.Bound, "bound to the receiver's Garden owner"));
                continue;
            }

            unbound++;
            Unbound++;
            outcomes.Add(new AdapterOutcome(family, AdapterState.Unimplemented,
                $"there is no Garden implementation for {family.Family}, so the native stays unbound"));
        }

        return new AdapterReport(outcomes, bound, unbound, absent);
    }

    private PapyrusValue Apply(GameplayNativeFamily family, PapyrusValue receiver, IReadOnlyList<PapyrusValue> arguments)
    {
        var target = EngineReference(receiver);
        var owner = target.Owner ?? throw new InvalidOperationException("the receiver has no resolved Garden owner");
        if (string.IsNullOrWhiteSpace(owner.BaseFormKey) || target.Identity != owner.Key)
            throw new InvalidOperationException("the receiver has no valid Garden owner");
        if (owner.Kind is not (PapyrusOwnerKind.PlacedReference or PapyrusOwnerKind.DynamicReference))
            throw new InvalidOperationException($"the receiver owner kind {owner.Kind} is unsupported");
        if (family.Native.Function == "GetItemCount")
        {
            if (arguments.Count > 1)
                throw new InvalidOperationException("the native has an extra argument");
            string? query = null;
            if (arguments.Count == 1 && arguments[0].Kind != PapyrusValueKind.None)
            {
                var reference = EngineReference(arguments[0]);
                if (reference.Owner is not null)
                    throw new InvalidOperationException("the native does not support an item reference with a resolved owner");
                query = reference.Identity;
            }
            return PapyrusValue.FromInt(_inventory.CountOf(owner, query));
        }
        var inventory = family.Family == "inventory";
        var removal = family.Native.Function == "RemoveItem";
        var equipment = family.Native.Function == "EquipItem";
        if (arguments.Count < 1 || arguments.Count > (removal ? 4 : inventory ? 3 : 2))
            throw new InvalidOperationException("the native has a missing or extra argument");
        var itemReference = EngineReference(arguments[0]);
        if (itemReference.Owner is not null)
            throw new InvalidOperationException("the native does not support an item reference with a resolved owner");
        var item = itemReference.Identity;
        if (inventory && !equipment && arguments.Count > 1
            && (arguments[1].Kind != PapyrusValueKind.Int || arguments[1].Integer != 1))
            throw new InvalidOperationException("Garden inventory supports only an integer count of one");
        if (equipment && arguments.Count > 1
            && (arguments[1].Kind != PapyrusValueKind.Bool || arguments[1].Flag))
            throw new InvalidOperationException("equipment removal-prevention is unsupported");
        var flagIndex = inventory ? 2 : 1;
        if (arguments.Count > flagIndex
            && (arguments[flagIndex].Kind != PapyrusValueKind.Bool || arguments[flagIndex].Flag))
            throw new InvalidOperationException("the native supports only the default false notification flag");
        if (removal && arguments.Count == 4 && arguments[3].Kind != PapyrusValueKind.None)
            throw new InvalidOperationException("inventory transfer to another container is unsupported");
        var applied = inventory
            ? _inventory.Apply(owner, item, equipment ? ItemOperation.Equip : removal ? ItemOperation.Remove : ItemOperation.Add,
                equipment ? ItemStorage.Equipped : ItemStorage.Carried).State == ItemState.Applied
            : _magic.AddPerk(owner, item);
        if (!applied)
            throw new InvalidOperationException($"Garden {family.Family} refused the operation");
        return PapyrusValue.None;
    }

    private static EngineObjectReference EngineReference(PapyrusValue value) =>
        value.Kind == PapyrusValueKind.Object && value.Reference is EngineObjectReference reference
            && !string.IsNullOrWhiteSpace(reference.Identity)
                ? reference
                : throw new InvalidOperationException("the native needs an engine object identity");
}
