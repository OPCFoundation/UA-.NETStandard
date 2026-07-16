/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
