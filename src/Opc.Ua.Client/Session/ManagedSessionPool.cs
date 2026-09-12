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
using System.Collections.Generic;
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

                if (!entry.IsConnectStarted)
                {
                    // Nothing ever connected under this key, and reading
                    // Connect would start one only to abort it again.
                    entry.Dispose();
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
            bool started = entry.IsConnectStarted;
            entry.Dispose();
            if (!started)
            {
                return;
            }

            ManagedSession session;
            try
            {
                // Deliberately not the caller's token: the abort above already
                // bounds this wait, and abandoning it would strand a session
                // that did connect - the pool has already forgotten the key,
                // so nothing else would ever close it.
                session = await entry.Connect.ConfigureAwait(false);
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
            Entry entry,
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
                // Evict this entry, not whatever sits under the key now: a
                // removal plus a fresh GetOrConnectAsync can install a healthy
                // replacement before this connect observes its cancellation,
                // and removing by key alone would throw that one away.
                ((ICollection<KeyValuePair<string, Entry>>)m_sessions)
                    .Remove(new KeyValuePair<string, Entry>(key, entry));
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
                // Capture the token here rather than reading it from the
                // source inside the factory: Dispose may release the source
                // before a racing caller starts the connect, and a captured
                // token of an already cancelled source stays usable.
                CancellationToken abort = m_abort.Token;
                m_connect = new Lazy<Task<ManagedSession>>(
                    () => pool.ConnectAndEvictOnFailureAsync(
                        key,
                        this,
                        endpoint,
                        configure,
                        abort),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            /// <summary>
            /// Whether the connect has been started. Reading
            /// <see cref="Connect"/> starts it, so a caller that only wants
            /// to tear the entry down has to ask first.
            /// </summary>
            public bool IsConnectStarted => m_connect.IsValueCreated;

            public Task<ManagedSession> Connect => m_connect.Value;

            /// <summary>
            /// Aborts a connect still in flight. The token source is released
            /// once the connect can no longer observe it, or right away when
            /// no connect was ever started.
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
                else
                {
                    m_abort.Dispose();
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
