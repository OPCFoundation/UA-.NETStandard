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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default keyed managed-session pool.
    /// </summary>
    public sealed class ManagedSessionPool : IManagedSessionPool
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public ManagedSessionPool(IManagedSessionFactory factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <inheritdoc/>
        public Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default)
        {
            return GetOrConnectAsync(key, endpoint, _ => { }, ct);
        }

        /// <inheritdoc/>
        public Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A non-empty key is required.", nameof(key));
            }
            if (endpoint is null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            // The connect is shared by every caller for this key, so it must
            // not run under the token of whichever caller happened to be first;
            // each caller observes its own token while awaiting the result.
            Lazy<Task<ManagedSession>> created = new(
                () => ConnectAndEvictOnFailureAsync(key, endpoint, configure),
                LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<Task<ManagedSession>> lazy = m_sessions.GetOrAdd(key, created);
            return AwaitWithCancellationAsync(lazy.Value, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A non-empty key is required.", nameof(key));
            }

            if (!m_sessions.TryRemove(key, out Lazy<Task<ManagedSession>>? lazy))
            {
                return false;
            }

            await CloseAndDisposeAsync(lazy, ct).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (string key in m_sessions.Keys)
            {
                if (!m_sessions.TryRemove(key, out Lazy<Task<ManagedSession>>? lazy))
                {
                    continue;
                }

                if (!lazy.IsValueCreated)
                {
                    continue;
                }

                Task<ManagedSession> connect = lazy.Value;
                if (connect.Status == TaskStatus.RanToCompletion)
                {
                    connect.GetAwaiter().GetResult().Dispose();
                    continue;
                }

                // A connect that is still running (or already failed) must not
                // be orphaned: dispose the session as soon as it materialises.
                _ = connect.ContinueWith(
                    static t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                        {
                            t.Result.Dispose();
                        }
                        else
                        {
                            // Observe the fault so it does not resurface as an
                            // unobserved task exception.
                            _ = t.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            foreach (string key in m_sessions.Keys)
            {
                if (m_sessions.TryRemove(key, out Lazy<Task<ManagedSession>>? lazy))
                {
                    await CloseAndDisposeAsync(lazy, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Closes and disposes a pooled session. A connect that never completed
        /// leaves nothing to dispose, so its failure is swallowed here - the
        /// caller that started it already observed the exception.
        /// </summary>
        private static async ValueTask CloseAndDisposeAsync(
            Lazy<Task<ManagedSession>> lazy,
            CancellationToken ct)
        {
            ManagedSession session;
            try
            {
                session = await lazy.Value.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return;
            }

            try
            {
                await session.CloseAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ManagedSession> AwaitWithCancellationAsync(
            Task<ManagedSession> task,
            CancellationToken ct)
        {
            if (task.IsCompleted || !ct.CanBeCanceled)
            {
                return await task.ConfigureAwait(false);
            }

            var cancellation = new TaskCompletionSource<ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(
                static state => ((TaskCompletionSource<ManagedSession>)state!).TrySetCanceled(),
                cancellation))
            {
                Task<ManagedSession> completed = await Task
                    .WhenAny(task, cancellation.Task)
                    .ConfigureAwait(false);
                return await completed.ConfigureAwait(false);
            }
        }

        private async Task<ManagedSession> ConnectAndEvictOnFailureAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure)
        {
            try
            {
                return await m_factory
                    .ConnectAsync(endpoint, configure, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                m_sessions.TryRemove(key, out _);
                throw;
            }
        }

        private readonly IManagedSessionFactory m_factory;

        private readonly ConcurrentDictionary<string, Lazy<Task<ManagedSession>>> m_sessions =
            new(StringComparer.Ordinal);
    }
}
