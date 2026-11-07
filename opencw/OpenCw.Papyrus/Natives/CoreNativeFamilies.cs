using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

/// <summary>The native families the Papyrus runtime binds.</summary>
public enum NativeFamily
{
    Debug,
    Math,
    Utility,
    GameQuery,
    Form,
    ObjectReference,
    GlobalVariable,
    FormList,
    Keyword,
    Cell,
    PlaceAtMe,
}

/// <summary>One member of a family: its name, its arity and what it does.</summary>
public sealed record NativeMember(NativeId Native, int Arity, Func<IReadOnlyList<PapyrusValue>, PapyrusValue> Body);

public enum FamilyState
{
    /// <summary>The family's evidence allows it and every member was bound.</summary>
    Bound,

    /// <summary>The family needs a gameplay system Garden does not have yet.</summary>
    MissingSystem,

    /// <summary>The family's behavior is not proven, so nothing was bound.</summary>
    Unproven,
}

public sealed record FamilyOutcome(NativeFamily Family, FamilyState State, int Members, string Reason);

public sealed record FamilyReport(IReadOnlyList<FamilyOutcome> Families, int Bound, int Refused, int Members)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"families={Families.Count} bound={Bound} refused={Refused} members={Members}\n");
        foreach (var family in Families)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {family.Family} {family.State.ToString().ToLowerInvariant()} members={family.Members} {family.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ToReport()))).ToLowerInvariant();
}

/// <summary>
/// Binds the core native families a census ranks highest, and refuses the rest as families.
/// </summary>
/// <remarks>
/// A family is bound whole or refused whole: a member is never bound on its own, so an unsupported
/// category stops and is counted instead of being reported as generically supported. Garden binds the
/// families it owns — the Debug sinks and pure Math on the arguments — and refuses every family that
/// needs a gameplay system Garden does not have yet, naming the reason for each. The report is
/// ordinally ordered and carries a digest.
/// </remarks>
public static class CoreNativeFamilies
{
    public const string MathKey = "native.family.math";
    public const string FamilyKeyPrefix = "native.family.";

    public static IReadOnlyDictionary<NativeFamily, NativeMember[]> Catalogue { get; } =
        new Dictionary<NativeFamily, NativeMember[]>
        {
            [NativeFamily.Debug] = new NativeMember[]
            {
                new(new NativeId("Debug", "Trace"), 1, _ => PapyrusValue.None),
                new(new NativeId("Debug", "Notification"), 1, _ => PapyrusValue.None),
                new(new NativeId("Debug", "MessageBox"), 1, _ => PapyrusValue.None),
            },
            [NativeFamily.Math] = new NativeMember[]
            {
                new(new NativeId("Math", "Abs"), 1, arguments => arguments[0].IsNumeric
                    ? arguments[0].Kind == PapyrusValueKind.Int
                        ? PapyrusValue.FromInt(Math.Abs(arguments[0].Integer))
                        : PapyrusValue.FromFloat(Math.Abs(arguments[0].Real))
                    : throw new InvalidOperationException("Math.Abs needs a numeric argument")),
                new(new NativeId("Math", "Floor"), 1, arguments => PapyrusValue.FromInt((long)Math.Floor(arguments[0].AsNumber()))),
                new(new NativeId("Math", "Ceiling"), 1, arguments => PapyrusValue.FromInt((long)Math.Ceiling(arguments[0].AsNumber()))),
                new(new NativeId("Math", "Min"), 2, arguments => PapyrusValue.FromFloat(Math.Min(arguments[0].AsNumber(), arguments[1].AsNumber()))),
                new(new NativeId("Math", "Max"), 2, arguments => PapyrusValue.FromFloat(Math.Max(arguments[0].AsNumber(), arguments[1].AsNumber()))),
            },
            [NativeFamily.Utility] = Array.Empty<NativeMember>(),
            [NativeFamily.GameQuery] = Array.Empty<NativeMember>(),
            [NativeFamily.Form] = Array.Empty<NativeMember>(),
            [NativeFamily.ObjectReference] = Array.Empty<NativeMember>(),
            [NativeFamily.GlobalVariable] = Array.Empty<NativeMember>(),
            [NativeFamily.FormList] = Array.Empty<NativeMember>(),
            [NativeFamily.Keyword] = Array.Empty<NativeMember>(),
            [NativeFamily.Cell] = Array.Empty<NativeMember>(),
            [NativeFamily.PlaceAtMe] = Array.Empty<NativeMember>(),
        };

    /// <summary>Families Garden owns and can bind today.</summary>
    public static IReadOnlySet<NativeFamily> GardenOwned { get; } =
        new HashSet<NativeFamily> { NativeFamily.Debug, NativeFamily.Math };

    public static FamilyReport Bind(IScriptHost host, EvidenceGate gate)
    {
        var outcomes = new List<FamilyOutcome>();
        var bound = 0;
        var refused = 0;
        var members = 0;

        foreach (var family in Catalogue.Keys.OrderBy(family => family.ToString(), StringComparer.Ordinal))
        {
            var catalogue = Catalogue[family];
            if (!GardenOwned.Contains(family))
            {
                refused++;
                outcomes.Add(new FamilyOutcome(family, FamilyState.MissingSystem, 0,
                    "the family needs a gameplay system Garden does not have yet, so nothing was bound"));
                continue;
            }

            var key = family == NativeFamily.Debug ? NativeSet.TraceKey : MathKey;
            if (!gate.Gate(key).Allowed)
            {
                refused++;
                outcomes.Add(new FamilyOutcome(family, FamilyState.Unproven, 0,
                    "the family's behavior is not proven, so nothing was bound"));
                continue;
            }

            foreach (var member in catalogue)
            {
                host.RegisterNative(member.Native, member.Body, NativeTargetKind.Static);
            }
            bound++;
            members += catalogue.Length;
            outcomes.Add(new FamilyOutcome(family, FamilyState.Bound, catalogue.Length,
                "every member of the family was bound"));
        }

        return new FamilyReport(outcomes, bound, refused, members);
    }
}
