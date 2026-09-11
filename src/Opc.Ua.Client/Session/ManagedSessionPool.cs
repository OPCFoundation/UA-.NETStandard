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
            // The pool keeps its own token so removal and disposal can still
            // abort a connect nobody is waiting for any more.
            var created = new Entry(this, key, endpoint, configure);
            Entry entry = m_sessions.GetOrAdd(key, created);
            if (!ReferenceEquals(entry, created))
            {
                created.Dispose();
            }
            return entry.Connect.WaitAsync(ct);
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A non-empty key is required.", nameof(key));
            }

            if (!m_sessions.TryRemove(key, out Entry? entry))
            {
                return false;
            }

            await CloseAndDisposeAsync(entry, ct).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (string key in m_sessions.Keys)
            {
                if (!m_sessions.TryRemove(key, out Entry? entry))
                {
                    continue;
                }

                Task<ManagedSession> connect = entry.Connect;
                if (connect.Status == TaskStatus.RanToCompletion)
                {
                    entry.Dispose();
                    connect.GetAwaiter().GetResult().Dispose();
                    continue;
                }

                // A connect that is still running (or already failed) must not
                // be orphaned: abort it, and dispose the session if it
                // materialises anyway.
                entry.Dispose();
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
                if (m_sessions.TryRemove(key, out Entry? entry))
                {
                    await CloseAndDisposeAsync(entry, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Closes and disposes a pooled session. A connect still in flight is
        /// aborted first; one that never completed leaves nothing to dispose,
        /// so its failure is swallowed here - the caller that started it
        /// already observed the exception.
        /// </summary>
        private static async ValueTask CloseAndDisposeAsync(
            Entry entry,
            CancellationToken ct)
        {
            entry.Dispose();

            ManagedSession session;
            try
            {
                session = await entry.Connect.WaitAsync(ct).ConfigureAwait(false);
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

        private async Task<ManagedSession> ConnectAndEvictOnFailureAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct)
        {
            try
            {
                return await m_factory
                    .ConnectAsync(endpoint, configure, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                m_sessions.TryRemove(key, out _);
                throw;
            }
        }

        /// <summary>
        /// A pooled connect together with the token that can abort it. The
        /// connect starts on first use of <see cref="Connect"/>, so a losing
        /// entry of the add race never connects at all.
        /// </summary>
        private sealed class Entry : IDisposable
        {
            public Entry(
                ManagedSessionPool pool,
                string key,
                ConfiguredEndpoint endpoint,
                Action<ManagedSessionBuilder> configure)
            {
                m_connect = new Lazy<Task<ManagedSession>>(
                    () => pool.ConnectAndEvictOnFailureAsync(
                        key,
                        endpoint,
                        configure,
                        m_abort.Token),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public Task<ManagedSession> Connect => m_connect.Value;

            /// <summary>
            /// Aborts a connect still in flight. The token source is only
            /// released once the connect can no longer observe it; a connect
            /// that has not started yet finds the token already cancelled
            /// and fails cleanly instead of tripping over a disposed source.
            /// </summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                m_abort.Cancel();
                if (m_connect.IsValueCreated)
                {
                    _ = m_connect.Value.ContinueWith(
                        static (_, s) => ((CancellationTokenSource)s!).Dispose(),
                        m_abort,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            private readonly Lazy<Task<ManagedSession>> m_connect;
            private readonly CancellationTokenSource m_abort = new();
            private int m_disposed;
        }

        private readonly IManagedSessionFactory m_factory;

        private readonly ConcurrentDictionary<string, Entry> m_sessions =
            new(StringComparer.Ordinal);
    }
}
