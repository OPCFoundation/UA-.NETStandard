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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    internal static class PendingCertificateKeyStoreOperations
    {
        public static async ValueTask<T> RunAsync<T>(
            PendingCertificateKeyContext context,
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            var key = new ScopeKey(
                context.BaseStore?.StoreType ?? string.Empty,
                (context.BaseStore?.StorePath ?? string.Empty).ToUpperInvariant(),
                context.CertificateGroupId,
                context.CertificateTypeId);
            OperationQueue queue;
            lock (s_lock)
            {
                if (!s_queues.TryGetValue(key, out queue!))
                {
                    queue = new OperationQueue();
                    s_queues.Add(key, queue);
                }
                queue.Users++;
            }
            try
            {
                await queue.EnterAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    return await operation(ct).ConfigureAwait(false);
                }
                finally
                {
                    queue.Leave();
                }
            }
            finally
            {
                lock (s_lock)
                {
                    if (--queue.Users == 0)
                    {
                        s_queues.Remove(key);
                        queue.Dispose();
                    }
                }
            }
        }

        private readonly record struct ScopeKey(string StoreType, string StorePath, NodeId Group, NodeId Type);

        private sealed class OperationQueue : IDisposable
        {
            public int Users { get; set; }

            public Task EnterAsync(CancellationToken ct)
            {
                return m_semaphore.WaitAsync(ct);
            }

            public void Leave()
            {
                m_semaphore.Release();
            }

            public void Dispose()
            {
                m_semaphore.Dispose();
            }

            private readonly SemaphoreSlim m_semaphore = new(1, 1);
        }

        private static readonly Lock s_lock = new();
        private static readonly Dictionary<ScopeKey, OperationQueue> s_queues = [];
    }
}
