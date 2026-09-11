/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Bridge.Sync
{
    internal sealed class XRegistrySyncDeadline : IAsyncDisposable
    {
        public XRegistrySyncDeadline(TimeProvider timeProvider, TimeSpan timeout, CancellationToken cancellationToken)
        {
            m_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            m_timer = timeProvider.CreateTimer(
                static state => ((CancellationTokenSource)state!).Cancel(),
                m_cancellation, timeout, Timeout.InfiniteTimeSpan);
        }

        public CancellationToken Token => m_cancellation.Token;

        public async ValueTask DisposeAsync()
        {
            await m_timer.DisposeAsync().ConfigureAwait(false);
            m_cancellation.Dispose();
        }

        public static bool IsEndpointFailure(Exception exception)
        {
            return exception is IOException or InvalidDataException or HttpRequestException or
                UnauthorizedAccessException or ServiceResultException or TimeoutException or
                OperationCanceledException or JsonException;
        }

        private readonly CancellationTokenSource m_cancellation;
        private readonly ITimer m_timer;
    }
}
