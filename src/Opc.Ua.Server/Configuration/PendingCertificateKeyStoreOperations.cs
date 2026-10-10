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
    /// <summary>
    /// Serializes pending-key operations for each certificate store, group and type.
    /// </summary>
    internal static class PendingCertificateKeyStoreOperations
    {
        /// <summary>
        /// Runs an operation exclusively within its certificate scope and releases the idle queue afterward.
        /// </summary>
        /// <typeparam name="T">The result produced by the serialized pending-key operation.</typeparam>
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

        /// <summary>
        /// Identifies the certificate store, group and type whose pending-key operations share a queue.
        /// </summary>
        /// <param name="StoreType">The certificate store implementation.</param>
        /// <param name="StorePath">The normalized certificate store path.</param>
        /// <param name="Group">The certificate group identifier.</param>
        /// <param name="Type">The certificate type identifier.</param>
        private readonly record struct ScopeKey(string StoreType, string StorePath, NodeId Group, NodeId Type);

        /// <summary>
        /// Tracks callers and serializes their access to one pending-key scope.
        /// </summary>
        private sealed class OperationQueue : IDisposable
        {
            /// <summary>
            /// Gets or sets the number of callers holding or waiting for this queue.
            /// </summary>
            public int Users { get; set; }

            /// <summary>
            /// Waits for exclusive access to the pending-key scope.
            /// </summary>
            public Task EnterAsync(CancellationToken ct)
            {
                return m_semaphore.WaitAsync(ct);
            }

            /// <summary>
            /// Releases the scope so the next waiting operation can proceed.
            /// </summary>
            public void Leave()
            {
                m_semaphore.Release();
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_semaphore.Dispose();
            }

            /// <summary>
            /// Permits one pending-key operation at a time.
            /// </summary>
            private readonly SemaphoreSlim m_semaphore = new(1, 1);
        }

        /// <summary>
        /// Protects queue lookup and caller counts.
        /// </summary>
        private static readonly Lock s_lock = new();

        /// <summary>
        /// Holds the queues that still have active or waiting callers.
        /// </summary>
        private static readonly Dictionary<ScopeKey, OperationQueue> s_queues = [];
    }
}
