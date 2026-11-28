using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Ui.Streaming;

public static class AfterAll
{
    public static Task Run(IEnumerable<Task> tasks, Action then) =>
        Task.WhenAll(tasks).ContinueWith(_ => then(), TaskScheduler.Default);
}
