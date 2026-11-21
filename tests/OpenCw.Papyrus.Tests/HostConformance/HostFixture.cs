using System;
using System.Collections.Generic;
using OpenCommonwealth.Services.Papyrus.Vm;

namespace OpenCw.Papyrus.Tests;

public abstract class HostFixture : IDisposable
{
    protected readonly List<IScriptHost> Hosts = new();

    protected IScriptHost Track(IScriptHost host)
    {
        Hosts.Add(host);
        return host;
    }

    public virtual void Dispose()
    {
        foreach (var host in Hosts)
        {
            TestHosts.Dispose(host);
        }
        Hosts.Clear();
    }
}
