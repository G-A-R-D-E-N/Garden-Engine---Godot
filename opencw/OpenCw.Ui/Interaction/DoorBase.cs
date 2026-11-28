using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCommonwealth.Services.Ui.Records;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Interaction;

public sealed record DoorBase(string? OpenText, string? CloseText, IReadOnlyList<string> RandomDestinations, byte Flags = 0)
{
    public const string RecordSignature = "DOOR";

    public static DoorBase From(DoorRecord door) =>
        new(ActivationTexts.Clean(door.OpenText.Text), ActivationTexts.Clean(door.CloseText.Text),
            door.RandomDestinations.Select(RecordKey.Format).ToArray(), door.Flags ?? 0);

    public static DoorBase? Read(RadicleEnvironment env, StringTables strings, Record record)
    {
        try
        {
            return env.Info(record).Signature == RecordSignature ? From(env.Door(record, strings)) : null;
        }
        catch (Exception ex) when (ex is RadicleException or ArgumentException or InvalidOperationException or DecoderFallbackException)
        {
            return null;
        }
    }
}
