using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCommonwealth.Services.Papyrus.Natives;

public sealed record GameplayNativeFamily(string Family, string OwningSystem, string Key, NativeId Native);

public enum AdapterState
{
    Bound,

    Conflict,
}

public sealed record AdapterOutcome(GameplayNativeFamily Family, AdapterState State, string Reason);

public sealed record AdapterReport(IReadOnlyList<AdapterOutcome> Natives, int Bound, int Unbound)
{
    public string ToReport()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"natives={Natives.Count} bound={Bound} unbound={Unbound}\n");
        foreach (var outcome in Natives)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"  {outcome.Family.Native} family={outcome.Family.Family} system={outcome.Family.OwningSystem} {outcome.State.ToString().ToLowerInvariant()} {outcome.Reason}\n");
        }
        return text.ToString();
    }

    public string Digest() => Sha256Hex.OfText(ToReport());
}
