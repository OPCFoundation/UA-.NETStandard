// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
