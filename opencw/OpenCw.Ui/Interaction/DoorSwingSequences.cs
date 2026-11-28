using System;
using System.Collections.Generic;
using System.Linq;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Interaction;

// Requires: bytes are a whole NIF; a NIF Radicle cannot read throws RadicleException (UNVERIFIED whether one with no animation gives an empty list or throws).
public static class DoorSwingSequences
{
    public static IReadOnlyList<DoorSwingSequence> Read(ReadOnlySpan<byte> bytes)
    {
        using var file = NifAnimFile.FromBytes(bytes);
        return From(file.Sequences);
    }

    public static IReadOnlyList<DoorSwingSequence> From(IEnumerable<NifAnimSequenceInfo> sequences) =>
        sequences.Select(sequence => new DoorSwingSequence(sequence.Name, sequence.StartTime, sequence.StopTime, sequence.Frequency)).ToArray();
}
