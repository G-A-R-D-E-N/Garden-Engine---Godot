#if RADICLE_HOST_AVAILABLE
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Host;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCw.Papyrus.Tests;

public sealed class RadicleHostConformance : ScriptHostConformance
{
    protected override IScriptHost Create(IReadOnlyDictionary<string, byte[]> pex, EvidenceGate gate) =>
        new RadicleScriptHost(gate, pex.Values);
}
#endif
