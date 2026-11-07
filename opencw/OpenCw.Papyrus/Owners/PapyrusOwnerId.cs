using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Papyrus.Owners;

public enum PapyrusOwnerKind
{
    /// <summary>A record form.</summary>
    Form,

    /// <summary>A placed reference in a cell.</summary>
    PlacedReference,

    /// <summary>A reference created at runtime.</summary>
    DynamicReference,

    /// <summary>One alias of a quest.</summary>
    QuestAlias,

    /// <summary>One runtime script instance.</summary>
    RuntimeInstance,
}

/// <summary>
/// The canonical identity of whatever a script runs on.
/// </summary>
/// <remarks>
/// A FormKey alone is not an identity: two placed references, two aliases of one quest and two runtime
/// instances of one base form all share a base FormKey and must stay distinct everywhere scripts,
/// properties, threads and save data need identity. The owner therefore carries its kind, its base
/// form key and a discriminator, and equality is over all three, so a shared base never collapses two
/// owners into one. <see cref="Key"/> is the stable text form a property bag, a thread table or a save
/// record keys on.
/// </remarks>
public readonly record struct PapyrusOwnerId
{
    private PapyrusOwnerId(PapyrusOwnerKind kind, string baseFormKey, string discriminator)
    {
        Kind = kind;
        BaseFormKey = baseFormKey;
        Discriminator = discriminator;
    }

    public PapyrusOwnerKind Kind { get; }

    public string BaseFormKey { get; }

    public string Discriminator { get; }

    /// <summary>The stable text key for identity maps, threads and save data.</summary>
    public string Key => $"{Prefix(Kind)}:{BaseFormKey}#{Discriminator}";

    public static PapyrusOwnerId ForForm(string baseFormKey) =>
        Create(PapyrusOwnerKind.Form, baseFormKey, "");

    public static PapyrusOwnerId ForPlacedReference(string baseFormKey, string reference) =>
        Create(PapyrusOwnerKind.PlacedReference, baseFormKey, Require(reference, "a placed reference"));

    public static PapyrusOwnerId ForDynamicReference(string baseFormKey, string token) =>
        Create(PapyrusOwnerKind.DynamicReference, baseFormKey, Require(token, "a dynamic reference token"));

    public static PapyrusOwnerId ForQuestAlias(string questFormKey, int aliasId) =>
        Create(PapyrusOwnerKind.QuestAlias, questFormKey, aliasId.ToString(CultureInfo.InvariantCulture));

    public static PapyrusOwnerId ForRuntimeInstance(string baseFormKey, long instance) =>
        Create(PapyrusOwnerKind.RuntimeInstance, baseFormKey, instance.ToString(CultureInfo.InvariantCulture));

    public override string ToString() => Key;

    private static PapyrusOwnerId Create(PapyrusOwnerKind kind, string baseFormKey, string discriminator)
    {
        if (string.IsNullOrWhiteSpace(baseFormKey))
        {
            throw new ArgumentException("a Papyrus owner needs a base form key", nameof(baseFormKey));
        }
        return new PapyrusOwnerId(kind, baseFormKey, discriminator);
    }

    private static string Require(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"a Papyrus owner needs {what}", nameof(value));
        }
        return value;
    }

    private static string Prefix(PapyrusOwnerKind kind) => kind switch
    {
        PapyrusOwnerKind.Form => "form",
        PapyrusOwnerKind.PlacedReference => "placed",
        PapyrusOwnerKind.DynamicReference => "dynamic",
        PapyrusOwnerKind.QuestAlias => "alias",
        _ => "instance",
    };
}

/// <summary>
/// Allocates runtime instance owners so two instances of one base form stay distinct.
/// </summary>
public sealed class PapyrusOwnerRegistry
{
    private readonly Dictionary<string, long> _next = new(StringComparer.Ordinal);
    private readonly List<PapyrusOwnerId> _allocated = new();

    public IReadOnlyList<PapyrusOwnerId> Allocated => _allocated;

    public PapyrusOwnerId AllocateRuntimeInstance(string baseFormKey)
    {
        var next = _next.TryGetValue(baseFormKey, out var previous) ? previous + 1 : 1;
        _next[baseFormKey] = next;
        var owner = PapyrusOwnerId.ForRuntimeInstance(baseFormKey, next);
        _allocated.Add(owner);
        return owner;
    }

    public int CountFor(string baseFormKey) => _allocated.Count(owner => owner.BaseFormKey == baseFormKey);
}
