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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Default keyed managed-session pool.
    /// </summary>
    /// <remarks>
    /// A disconnected session remains cached so callers retain its identity and subscriptions.
    /// A closed session may be replaced once per get; a factory returning a closed session fails.
    /// Removal bounds each connect/close/dispose wait to five seconds, logs incomplete cleanup,
    /// and observes late completion without abandoning ownership of a returned session.
    /// </remarks>
    public sealed class ManagedSessionPool : IManagedSessionPool
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public ManagedSessionPool(IManagedSessionFactory factory)
            : this(factory, AmbientMessageContext.Telemetry)
        {
        }

        /// <summary>
        /// Initializes a pool with telemetry for bounded, best-effort teardown.
        /// </summary>
        public ManagedSessionPool(IManagedSessionFactory factory, ITelemetryContext? telemetry)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            m_logger = telemetry.CreateLogger<ManagedSessionPool>();
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

            return GetOrConnectCore(key, endpoint, configure, allowReplacement: true, ct);
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
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            foreach (string key in m_sessions.Keys)
            {
                if (!m_sessions.TryRemove(key, out Entry? entry))
                {
                    continue;
                }

                entry.Dispose();
                if (entry.Session is ManagedSession session)
                {
                    _ = ObserveCleanupAsync(session.DisposeAsync().AsTask());
                    continue;
                }
                if (!entry.IsConnectStarted)
                {
                    // Reading Connect would start an unused entry merely to abort it.
                    continue;
                }
                _ = DisposeLateSessionAsync(entry.Connect);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            foreach (string key in m_sessions.Keys)
            {
                _ = await RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private Task<ManagedSession> GetOrConnectCore(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            bool allowReplacement,
            CancellationToken ct)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ManagedSessionPool));
            }
            ct.ThrowIfCancellationRequested();

            Entry entry = GetOrAddEntry(key, endpoint, configure);

            if (Volatile.Read(ref m_disposed) != 0)
            {
                return RejectDisposedPoolAsync(key, entry);
            }

            if (entry.Session is ManagedSession session && IsTerminal(session))
            {
                if (!allowReplacement)
                {
                    throw new ServiceResultException(StatusCodes.BadNotConnected, "The pooled session was closed.");
                }
                return ReplaceClosedEntryAsync(key, entry, endpoint, configure, ct);
            }

            // Preserve the shared task for callers that do not need their own cancellation.
            return ct.CanBeCanceled ? entry.Connect.WaitAsync(ct) : entry.Connect;
        }

        private Entry GetOrAddEntry(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure)
        {
            var created = new Entry(this, key, endpoint, configure);
            Entry entry = m_sessions.GetOrAdd(key, created);
            if (!ReferenceEquals(entry, created))
            {
                created.Dispose();
            }
            return entry;
        }

        private async Task<ManagedSession> ReplaceClosedEntryAsync(
            string key,
            Entry entry,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct)
        {
            if (RemoveEntry(key, entry))
            {
                await CloseAndDisposeAsync(entry, ct).ConfigureAwait(false);
            }
            return await GetOrConnectCore(key, endpoint, configure, allowReplacement: false, ct)
                .ConfigureAwait(false);
        }

        private async Task<ManagedSession> RejectDisposedPoolAsync(string key, Entry entry)
        {
            if (RemoveEntry(key, entry))
            {
                await CloseAndDisposeAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
            throw new ObjectDisposedException(nameof(ManagedSessionPool));
        }

        private bool RemoveEntry(string key, Entry entry)
        {
            return ((ICollection<KeyValuePair<string, Entry>>)m_sessions)
                .Remove(new KeyValuePair<string, Entry>(key, entry));
        }

        private static bool IsTerminal(ManagedSession session)
        {
            return session.Disposed || session.StateMachine.State is ConnectionState.Closed or ConnectionState.Closing;
        }

        /// <summary>
        /// Closes and disposes a pooled session. A connect still in flight is
        /// aborted first. An uncooperative factory is observed in the background
        /// after a bounded wait, so a late session is still disposed.
        /// </summary>
        private async ValueTask CloseAndDisposeAsync(
            Entry entry,
            CancellationToken ct)
        {
            bool started = entry.IsConnectStarted;
            entry.Dispose();
            if (!started)
            {
                if (entry.Session is ManagedSession connected)
                {
                    await CloseAndDisposeSessionAsync(connected, ct).ConfigureAwait(false);
                }
                return;
            }

            ManagedSession session;
            try
            {
                session = await entry.Connect.WaitAsync(s_teardownTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                m_logger.ManagedSessionPoolTeardownFailed(exception);
                _ = DisposeLateSessionAsync(entry.Connect);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _ = DisposeLateSessionAsync(entry.Connect);
                throw;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ManagedSessionPoolTeardownFailed(exception);
                return;
            }

            await CloseAndDisposeSessionAsync(session, ct).ConfigureAwait(false);
        }

        private async Task DisposeLateSessionAsync(Task<ManagedSession> connect)
        {
            try
            {
                ManagedSession session = await connect.ConfigureAwait(false);
                await CloseAndDisposeSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Removing the entry deliberately cancelled its shared connect.
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ManagedSessionPoolTeardownFailed(exception);
            }
        }

        private async ValueTask CloseAndDisposeSessionAsync(ManagedSession session, CancellationToken ct)
        {
            try
            {
                if (!IsTerminal(session) && session.StateMachine.State != ConnectionState.Disconnected)
                {
                    StatusCode result = await session.CloseAsync(
                        (int)s_teardownTimeout.TotalMilliseconds, closeChannel: true, ct)
                        .WaitAsync(s_teardownTimeout, ct).ConfigureAwait(false);
                    if (StatusCode.IsBad(result))
                    {
                        m_logger.ManagedSessionPoolTeardownFailed(new ServiceResultException(result));
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                m_logger.ManagedSessionPoolTeardownFailed(exception);
            }
            finally
            {
                Task dispose = session.DisposeAsync().AsTask();
                try
                {
                    await dispose.WaitAsync(s_teardownTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (TimeoutException exception)
                {
                    m_logger.ManagedSessionPoolTeardownFailed(exception);
                    _ = ObserveCleanupAsync(dispose);
                }
            }
        }

        private async Task ObserveCleanupAsync(Task cleanup)
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_logger.ManagedSessionPoolTeardownFailed(exception);
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
                ct.ThrowIfCancellationRequested();
                ManagedSession session = await m_factory
                    .ConnectAsync(endpoint, configure, ct)
                    .ConfigureAwait(false);
                entry.Session = session;
                if (ct.IsCancellationRequested || IsTerminal(session))
                {
                    await CloseAndDisposeSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    throw new ServiceResultException(
                        StatusCodes.BadNotConnected, "The session factory returned a closed or disposed session.");
                }
                return session;
            }
            catch
            {
                // Evict this entry, not whatever sits under the key now: a
                // removal plus a fresh GetOrConnectAsync can install a healthy
                // replacement before this connect observes its cancellation,
                // and removing by key alone would throw that one away.
                RemoveEntry(key, entry);
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

            public ManagedSession? Session
            {
                get => Volatile.Read(ref m_session);
                set => Volatile.Write(ref m_session, value);
            }

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
            private ManagedSession? m_session;
        }

        private readonly IManagedSessionFactory m_factory;
        private readonly ILogger m_logger;
        private int m_disposed;
        private static readonly TimeSpan s_teardownTimeout = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<string, Entry> m_sessions =
            new(StringComparer.Ordinal);
    }

    internal static partial class ManagedSessionPoolLog
    {
        [LoggerMessage(EventId = ClientEventIds.ManagedSessionPool + 0, Level = LogLevel.Warning,
            Message = "Managed session pool teardown did not complete cleanly; late cleanup remains observed.")]
        public static partial void ManagedSessionPoolTeardownFailed(this ILogger logger, Exception exception);
    }
}
