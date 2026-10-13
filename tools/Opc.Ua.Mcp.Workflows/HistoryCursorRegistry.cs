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
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Owns bounded HistoryClient enumerators, not protocol runtimes or raw server continuation points.
    /// </summary>
    public sealed class HistoryCursorRegistry : IAsyncDisposable
    {
        /// <summary>
        /// Creates an owner/session-bound registry with deterministic host-supplied time.
        /// </summary>
        public HistoryCursorRegistry(
            OpcUaSessionManager sessions, ClientWorkflowOptions options, TimeProvider? timeProvider = null,
            IClientWorkflowSessionFactory? scopes = null)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            m_time = timeProvider ?? TimeProvider.System;
            m_scopes = scopes ?? new ClientWorkflowSessionFactory();
            m_timer = new PeriodicTimer(TimeSpan.FromSeconds(1), m_time);
            m_sweeper = SweepLoopAsync();
        }

        /// <summary>
        /// Reads the first bounded page. Retention requires trusted identity before source creation.
        /// </summary>
        public ValueTask<JsonObject> ReadAsync(
            OpcUaSessionManager.SessionInfo session,
            Func<CancellationToken, IAsyncEnumerable<JsonNode?>> source,
            int maxResults = 100,
            bool retainCursor = false,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            return ReadAsync(session, (_, token) => source(token), maxResults, retainCursor, ct);
        }

        /// <summary>
        /// Creates a source over the guarded service session and retains that context with its enumerator.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<JsonObject> ReadAsync(
            OpcUaSessionManager.SessionInfo session,
            Func<ISession, CancellationToken, IAsyncEnumerable<JsonNode?>> source,
            int maxResults = 100,
            bool retainCursor = false,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(source);
            WorkflowJson.Page(m_options, maxResults);
            McpCallerIdentity? owner = retainCursor ? m_sessions.OperationPolicy.RequireCaller() : null;
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                await SweepCoreAsync().ConfigureAwait(false);
                if (retainCursor &&
                    (m_cursors.Count >= m_options.MaxHistoryCursors ||
                        m_cursors.Values.Count(cursor => cursor.Owner == owner) >=
                            m_options.MaxHistoryCursorsPerCaller))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The host's historical cursor quota is full; release a cursor before opening another.");
                }
                IClientWorkflowSessionScope scope = await m_scopes.CaptureAsync(session, ct).ConfigureAwait(false);
                var lifetime = new CancellationTokenSource();
                Cursor cursor;
                try
                {
                    cursor = new Cursor(session, owner, m_time.GetUtcNow(), lifetime,
                        source(scope.Access.Session, lifetime.Token).GetAsyncEnumerator(lifetime.Token), scope);
                }
                catch
                {
                    lifetime.Dispose();
                    await scope.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                string? handle = retainCursor ? "history:" + Guid.NewGuid().ToString("N") : null;
                try
                {
                    JsonObject result = await PageAsync(cursor, maxResults, ct).ConfigureAwait(false);
                    if (cursor.HasBuffered && handle is not null)
                    {
                        m_cursors.Add(handle, cursor);
                        result["cursor"] = handle;
                        result["expiresAt"] = ExpiresAt(cursor).ToString("O");
                    }
                    else
                    {
                        await CloseAsync(cursor).ConfigureAwait(false);
                        result["cursor"] = null;
                    }
                    result["continuationRetained"] = cursor.HasBuffered && handle is not null;
                    return result;
                }
                catch
                {
                    await CloseAsync(cursor).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Continues the exact retained enumeration without offset replay.
        /// </summary>
        public async ValueTask<JsonObject> NextAsync(
            string cursor, int maxResults = 100, CancellationToken ct = default)
        {
            WorkflowJson.Text(cursor, 128);
            WorkflowJson.Page(m_options, maxResults);
            McpCallerIdentity owner = m_sessions.OperationPolicy.RequireCaller();
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                await SweepCoreAsync().ConfigureAwait(false);
                Cursor state = GetOwned(cursor, owner);
                try
                {
                    JsonObject result = await PageAsync(state, maxResults, ct).ConfigureAwait(false);
                    result["cursor"] = state.HasBuffered ? cursor : null;
                    result["continuationRetained"] = state.HasBuffered;
                    if (state.HasBuffered)
                    {
                        result["expiresAt"] = ExpiresAt(state).ToString("O");
                    }
                    else
                    {
                        m_cursors.Remove(cursor);
                        await CloseAsync(state).ConfigureAwait(false);
                    }
                    return result;
                }
                catch
                {
                    m_cursors.Remove(cursor);
                    await CloseAsync(state).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Releases only this caller's cursor and the SDK enumerator's own continuation point.
        /// </summary>
        public async ValueTask<JsonObject> ReleaseAsync(string cursor, CancellationToken ct = default)
        {
            WorkflowJson.Text(cursor, 128);
            McpCallerIdentity owner = m_sessions.OperationPolicy.RequireCaller();
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                await SweepCoreAsync().ConfigureAwait(false);
                Cursor state = GetOwned(cursor, owner);
                m_cursors.Remove(cursor);
                await CloseAsync(state).ConfigureAwait(false);
                return new JsonObject { ["released"] = true };
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Releases abandoned iterators and waits for background expiry work during host shutdown.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Task drained;
            bool first;
            lock (m_lifecycle)
            {
                if (m_disposeRequested)
                {
                    drained = m_disposal.Task;
                    first = false;
                }
                else
                {
                    m_disposeRequested = true;
                    drained = m_users == 0 ? Task.CompletedTask : m_drained.Task;
                    first = true;
                }
            }
            if (!first)
            {
                await drained.ConfigureAwait(false);
                return;
            }
            Exception? failure = null;
            try
            {
                await m_shutdown.CancelAsync().ConfigureAwait(false);
                m_timer.Dispose();
                await m_sweeper.ConfigureAwait(false);
                await drained.ConfigureAwait(false);
                foreach (Cursor cursor in m_cursors.Values)
                {
                    await CloseAsync(cursor).ConfigureAwait(false);
                }
                m_cursors.Clear();
                m_disposed = true;
            }
            catch (Exception exception)
            {
                failure = exception;
                throw;
            }
            finally
            {
                m_gate.Dispose();
                m_shutdown.Dispose();
                if (failure is null)
                {
                    m_disposal.TrySetResult();
                }
                else
                {
                    m_disposal.TrySetException(failure);
                }
            }
        }

        /// <summary>
        /// Runs deterministic expiry without relying on a future client request.
        /// </summary>
        internal async ValueTask SweepAsync(CancellationToken ct = default)
        {
            await EnterGateAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_disposed)
                {
                    await SweepCoreAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                ExitGate();
            }
        }

        /// <summary>
        /// Reads a page and one look-ahead item while bounding each request's active wait.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<JsonObject> PageAsync(Cursor cursor, int maximum, CancellationToken ct)
        {
            using var window = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(m_options.MaxDurationMilliseconds), m_time);
            using CancellationTokenRegistration cancellation = ct.Register(cursor.Lifetime.Cancel);
            using CancellationTokenRegistration timeout = window.Token.Register(cursor.Lifetime.Cancel);
            using CancellationTokenRegistration shutdown = m_shutdown.Token.Register(cursor.Lifetime.Cancel);
            try
            {
                var values = new JsonArray();
                if (cursor.HasBuffered)
                {
                    values.Add(cursor.Buffered);
                    cursor.Buffered = null;
                    cursor.HasBuffered = false;
                }
                while (values.Count < maximum && await cursor.Enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    values.Add(cursor.Enumerator.Current);
                }
                if (values.Count == maximum && await cursor.Enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    cursor.Buffered = cursor.Enumerator.Current;
                    cursor.HasBuffered = true;
                }
                ct.ThrowIfCancellationRequested();
                window.Token.ThrowIfCancellationRequested();
                cursor.Lifetime.Token.ThrowIfCancellationRequested();
                cursor.LastUsed = m_time.GetUtcNow();
                return new JsonObject
                {
                    ["items"] = values,
                    ["hasMore"] = cursor.HasBuffered,
                    ["complete"] = !cursor.HasBuffered,
                    ["consistency"] = "serverContinuation"
                };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && window.IsCancellationRequested)
            {
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "The host's bounded historical page deadline expired.");
            }
        }

        /// <summary>
        /// Never reveals whether a handle belongs to a different caller.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private Cursor GetOwned(string handle, McpCallerIdentity owner)
        {
            if (!m_cursors.TryGetValue(handle, out Cursor? cursor) || cursor.Owner != owner)
            {
                throw new ServiceResultException(StatusCodes.BadContinuationPointInvalid,
                    "The historical cursor is unavailable for this caller and session generation.");
            }
            return cursor;
        }

        /// <summary>
        /// Drops entries whose host lifetime or exact registered UA session has changed.
        /// </summary>
        private async ValueTask SweepCoreAsync()
        {
            DateTimeOffset now = m_time.GetUtcNow();
            foreach ((string handle, Cursor cursor) in m_cursors.ToArray())
            {
                OpcUaSessionManager.SessionInfo? actual = m_sessions.GetSessionInfo(cursor.Session.Name);
                if (now >= ExpiresAt(cursor) ||
                    !ReferenceEquals(actual, cursor.Session) ||
                    !cursor.Session.IsConnected ||
                    cursor.Scope.Invalidated.IsCompleted ||
                    cursor.Session.Session.SessionId != cursor.SessionId)
                {
                    m_cursors.Remove(handle);
                    await CloseAsync(cursor).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Applies both idle expiry and the non-extendable absolute lease.
        /// </summary>
        private DateTimeOffset ExpiresAt(Cursor cursor)
        {
            DateTimeOffset idle = cursor.LastUsed + m_options.HistoryCursorIdleTimeout;
            DateTimeOffset absolute = cursor.Created + m_options.HistoryCursorLifetime;
            return idle < absolute ? idle : absolute;
        }

        /// <summary>
        /// Closes an iterator once and preserves the original operation failure over cleanup failure.
        /// </summary>
        private static async ValueTask CloseAsync(Cursor cursor)
        {
            if (cursor.Closed)
            {
                return;
            }
            cursor.Closed = true;
            try
            {
                await cursor.Enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ServiceResultException or
                OperationCanceledException or ObjectDisposedException)
            {
                // HistoryClient already performs best-effort release; an unavailable session cannot be repaired here.
            }
            finally
            {
                cursor.Lifetime.Dispose();
                await cursor.Scope.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps expiry work owned by this registry and joined by asynchronous disposal.
        /// </summary>
        private async Task SweepLoopAsync()
        {
            try
            {
                while (await m_timer.WaitForNextTickAsync().ConfigureAwait(false))
                {
                    await SweepAsync().ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) when (m_disposeRequested)
            {
                // Shutdown prevents new gate entrants before joining the timer and in-flight operations.
            }
        }

        /// <summary>
        /// Counts admitted operations before waiting, so asynchronous disposal can drain every semaphore waiter.
        /// </summary>
        private async ValueTask EnterGateAsync(CancellationToken ct)
        {
            lock (m_lifecycle)
            {
                ObjectDisposedException.ThrowIf(m_disposeRequested, this);
                m_users++;
            }
            try
            {
                await m_gate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                ReleaseUser();
                throw;
            }
        }

        /// <summary>
        /// Releases a completed gate operation before signalling that disposal may destroy the semaphore.
        /// </summary>
        private void ExitGate()
        {
            m_gate.Release();
            ReleaseUser();
        }

        /// <summary>
        /// Completes the drain signal only after the last admitted operation has left.
        /// </summary>
        private void ReleaseUser()
        {
            lock (m_lifecycle)
            {
                if (--m_users == 0 && m_disposeRequested)
                {
                    m_drained.TrySetResult();
                }
            }
        }

        /// <summary>
        /// The minimal private state of one retained SDK enumeration.
        /// </summary>
        private sealed class Cursor
        {
            /// <summary>
            /// Captures the original owner and session generation before reading any history.
            /// </summary>
            internal Cursor(
                OpcUaSessionManager.SessionInfo session, McpCallerIdentity? owner, DateTimeOffset created,
                CancellationTokenSource lifetime, IAsyncEnumerator<JsonNode?> enumerator,
                IClientWorkflowSessionScope scope)
            {
                Session = session;
                SessionId = scope.SessionId;
                Owner = owner;
                Created = created;
                LastUsed = created;
                Lifetime = lifetime;
                Enumerator = enumerator;
                Scope = scope;
            }

            /// <summary>
            /// Gets the exact registration that owns this continuation.
            /// </summary>
            internal OpcUaSessionManager.SessionInfo Session { get; }

            /// <summary>
            /// Gets the original server session id, including before a managed-session reconnect.
            /// </summary>
            internal NodeId SessionId { get; }

            /// <summary>
            /// Gets the trusted caller; stateless enumerations have none.
            /// </summary>
            internal McpCallerIdentity? Owner { get; }

            /// <summary>
            /// Gets the absolute lease origin.
            /// </summary>
            internal DateTimeOffset Created { get; }

            /// <summary>
            /// Gets or sets the last completed read time.
            /// </summary>
            internal DateTimeOffset LastUsed { get; set; }

            /// <summary>
            /// Gets the cancellation source owned by this cursor, never by a departed request.
            /// </summary>
            internal CancellationTokenSource Lifetime { get; }

            /// <summary>
            /// Gets the SDK enumerator whose disposal releases remote resources.
            /// </summary>
            internal IAsyncEnumerator<JsonNode?> Enumerator { get; }

            /// <summary>
            /// Gets the guarded SDK session context retained across every resumed read and cleanup.
            /// </summary>
            internal IClientWorkflowSessionScope Scope { get; }

            /// <summary>
            /// Gets or sets a single look-ahead value without replaying a server query.
            /// </summary>
            internal JsonNode? Buffered { get; set; }

            /// <summary>
            /// Gets or sets whether look-ahead succeeded, independently of a null JSON value.
            /// </summary>
            internal bool HasBuffered { get; set; }

            /// <summary>
            /// Gets or sets the terminal cleanup marker.
            /// </summary>
            internal bool Closed { get; set; }
        }

        private readonly OpcUaSessionManager m_sessions;
        private readonly ClientWorkflowOptions m_options;
        private readonly TimeProvider m_time;
        private readonly IClientWorkflowSessionFactory m_scopes;
        private readonly PeriodicTimer m_timer;
        private readonly Task m_sweeper;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_lifecycle = new();
        private readonly CancellationTokenSource m_shutdown = new();
        private readonly TaskCompletionSource m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource m_disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, Cursor> m_cursors = new(StringComparer.Ordinal);
        private int m_users;
        private bool m_disposeRequested;
        private bool m_disposed;
    }
}
