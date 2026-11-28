using System;

namespace OpenCommonwealth.Services.Papyrus.Vm;

public sealed class NativeWait : Exception
{
    public const string Key = "vm.native.latent";

    public NativeWait(long token) : base($"waiting on token {token}")
    {
        if (token <= 0) throw new ArgumentOutOfRangeException(nameof(token));
        Token = token;
    }

    public long Token { get; }
}
