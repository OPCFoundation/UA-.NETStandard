// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Migration shim &#8212; restores the APM
    /// <c>BeginFindServers</c> / <c>EndFindServers</c> pair on
    /// <see cref="LocalDiscoveryServerClient"/> that was removed in 1.6 in
    /// favour of <c>FindServersAsync</c>.
    /// </summary>
    public static class LocalDiscoveryServerClientShim
    {
        extension(LocalDiscoveryServerClient client)
        {
            /// <summary>
            /// Begins an asynchronous <c>FindServers</c> call using the
            /// classic Begin/End APM pattern.
            /// </summary>
            [Obsolete("BeginFindServers/EndFindServers were removed in 1.6. Use FindServersAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0015")]
            [OpcUaShim("UA0015")]
            public IAsyncResult BeginFindServers(AsyncCallback? callback, object? state)
            {
                var tcs = new TaskCompletionSource<ArrayOf<ApplicationDescription>>(state);
                client.FindServersAsync()
                    .AsTask()
                    .ContinueWith(
                        t =>
                        {
                            if (t.IsFaulted)
                            {
                                tcs.TrySetException(t.Exception!.InnerExceptions);
                            }
                            else if (t.IsCanceled)
                            {
                                tcs.TrySetCanceled();
                            }
                            else
                            {
                                tcs.TrySetResult(t.Result);
                            }
                            callback?.Invoke(tcs.Task);
                        },
                        TaskScheduler.Default);
                return tcs.Task;
            }

            /// <summary>
            /// Completes the asynchronous <c>FindServers</c> call.
            /// </summary>
            [Obsolete("BeginFindServers/EndFindServers were removed in 1.6. Use FindServersAsync. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0015")]
            [OpcUaShim("UA0015")]
            public ArrayOf<ApplicationDescription> EndFindServers(IAsyncResult result)
            {
                return ((Task<ArrayOf<ApplicationDescription>>)result)
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }
}
