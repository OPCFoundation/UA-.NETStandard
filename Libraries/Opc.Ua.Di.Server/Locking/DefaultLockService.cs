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
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Default in-memory <see cref="ILockService"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lock records are kept in a <see cref="ConcurrentDictionary{TKey, TValue}"/>
    /// keyed by topology-element <see cref="NodeId"/>. Each record
    /// stores the owning <c>SessionId</c>, the client-supplied
    /// context string, the owning user identity (from
    /// <see cref="ISystemContext.UserId"/>), and an expiry
    /// timestamp.
    /// </para>
    /// <para>
    /// When wired through <c>DiNodeManager.AttachLockService</c>, the
    /// service hooks the hosting <see cref="ISessionManager.SessionClosing"/>
    /// event so that locks held by a disconnecting session are released
    /// automatically.
    /// </para>
    /// </remarks>
    public sealed class DefaultLockService : ILockService, IDisposable
    {
        private static readonly TimeSpan s_defaultLockDuration = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<NodeId, Record> m_records = new();
        private readonly ILogger<DefaultLockService>? m_logger;
        private readonly TimeProvider m_timeProvider;
        private ISessionManager? m_sessionManager;
        private SessionEventHandler? m_sessionClosingHandler;

        /// <summary>
        /// Creates a new lock service. <paramref name="lockDuration"/>
        /// defaults to 5 minutes if omitted.
        /// </summary>
        public DefaultLockService(
            TimeSpan? lockDuration = null,
            TimeProvider? timeProvider = null,
            ILogger<DefaultLockService>? logger = null)
        {
            LockDuration = lockDuration ?? s_defaultLockDuration;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = logger;
        }

        /// <summary>
        /// How long a lock is held before auto-expiring.
        /// </summary>
        public TimeSpan LockDuration { get; }

        /// <summary>
        /// Attaches the service to a session manager so locks are
        /// released automatically when the owning session closes.
        /// Call once during DI node-manager startup; calling twice
        /// throws.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentNullException"></exception>
        public void AttachToSessionManager(ISessionManager sessionManager)
        {
            if (m_sessionManager != null)
            {
                throw new InvalidOperationException(
                    "DefaultLockService is already attached to a session manager.");
            }

            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_sessionClosingHandler = OnSessionClosing;
            sessionManager.SessionClosing += m_sessionClosingHandler;
        }

        /// <inheritdoc/>
        public LockState GetState(NodeId elementId)
        {
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (m_records.TryGetValue(elementId, out Record? record))
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (record.ExpiresAt > now)
                {
                    return new LockState(
                        Locked: true,
                        LockingClient: record.ClientContext,
                        LockingUser: record.User,
                        RemainingLockTimeSeconds: (record.ExpiresAt - now).TotalSeconds);
                }

                // Expired — clean up.
                m_records.TryRemove(elementId, out _);
            }

            return new LockState(false, string.Empty, string.Empty, 0.0);
        }

        /// <inheritdoc/>
        public int InitLock(
            ISystemContext context,
            NodeId elementId,
            string clientContext)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            clientContext ??= string.Empty;

            (NodeId sessionId, string user) = ResolveCallerIdentity(context);
            DateTimeOffset now = m_timeProvider.GetUtcNow();

            int outcome = LockStatus.Ok;
            m_records.AddOrUpdate(
                elementId,
                _ => new Record(sessionId, clientContext, user, now + LockDuration),
                (_, existing) =>
                {
                    if (existing.ExpiresAt > now)
                    {
                        outcome = LockStatus.AlreadyLocked;
                        return existing;
                    }
                    return new Record(sessionId, clientContext, user, now + LockDuration);
                });

            if (outcome == LockStatus.Ok)
            {
                m_logger?.LockAcquired(elementId, sessionId, user, clientContext);
            }
            return outcome;
        }

        /// <inheritdoc/>
        public int RenewLock(ISystemContext context, NodeId elementId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            (NodeId sessionId, _) = ResolveCallerIdentity(context);
            DateTimeOffset now = m_timeProvider.GetUtcNow();

            if (!m_records.TryGetValue(elementId, out Record? existing) ||
                existing.ExpiresAt <= now)
            {
                m_records.TryRemove(elementId, out _);
                return LockStatus.NotLocked;
            }

            if (!Equals(existing.SessionId, sessionId))
            {
                return LockStatus.WrongClient;
            }

            existing.ExpiresAt = now + LockDuration;
            return LockStatus.Ok;
        }

        /// <inheritdoc/>
        public int ExitLock(ISystemContext context, NodeId elementId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            (NodeId sessionId, _) = ResolveCallerIdentity(context);
            DateTimeOffset now = m_timeProvider.GetUtcNow();

            if (!m_records.TryGetValue(elementId, out Record? existing) ||
                existing.ExpiresAt <= now)
            {
                m_records.TryRemove(elementId, out _);
                return LockStatus.NotLocked;
            }

            if (!Equals(existing.SessionId, sessionId))
            {
                return LockStatus.WrongClient;
            }

            m_records.TryRemove(elementId, out _);
            return LockStatus.Ok;
        }

        /// <inheritdoc/>
        public int BreakLock(ISystemContext context, NodeId elementId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            if (!m_records.TryGetValue(elementId, out Record? existing) ||
                existing.ExpiresAt <= now)
            {
                m_records.TryRemove(elementId, out _);
                return LockStatus.NotLocked;
            }

            m_records.TryRemove(elementId, out _);
            m_logger?.LockForciblyBroken(elementId, existing.SessionId);
            return LockStatus.Ok;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_sessionManager != null && m_sessionClosingHandler != null)
            {
                m_sessionManager.SessionClosing -= m_sessionClosingHandler;
                m_sessionManager = null;
                m_sessionClosingHandler = null;
            }
            m_records.Clear();
        }

        private void OnSessionClosing(ISession session, SessionEventReason reason)
        {
            NodeId sessionId = session.Id;
            var toRelease = new List<NodeId>();
            foreach (KeyValuePair<NodeId, Record> kv in m_records)
            {
                if (Equals(kv.Value.SessionId, sessionId))
                {
                    toRelease.Add(kv.Key);
                }
            }
            foreach (NodeId elementId in toRelease)
            {
                if (m_records.TryRemove(elementId, out Record? released))
                {
                    m_logger?.LockReleasedAfterSessionClosed(elementId, released.SessionId);
                }
            }
        }

        private static (NodeId SessionId, string User) ResolveCallerIdentity(ISystemContext context)
        {
            // The standard server context carries the session id;
            // fall back to a deterministic synthetic id derived from
            // the user when the context is a non-server SystemContext
            // (e.g. when invoked from tests).
            if (context is ServerSystemContext server &&
                server.SessionId is { } sid &&
                !sid.IsNull)
            {
                return (sid, context.UserId ?? string.Empty);
            }
            // No session id available — synthesise one from the user
            // identity so tests can still distinguish callers.
            string userKey = context.UserId ?? "anonymous";
            return (new NodeId(userKey, 0), userKey);
        }

        /// <summary>
        /// Internal lock-record. <see cref="ExpiresAt"/> is mutable so
        /// <see cref="RenewLock"/> can update it in place under the
        /// concurrent dictionary's reader lock.
        /// </summary>
        private sealed class Record
        {
            public Record(NodeId sessionId, string clientContext, string user, DateTimeOffset expiresAt)
            {
                SessionId = sessionId;
                ClientContext = clientContext;
                User = user;
                ExpiresAt = expiresAt;
            }

            public NodeId SessionId { get; }
            public string ClientContext { get; }
            public string User { get; }
            public DateTimeOffset ExpiresAt { get; set; }
        }
    }

    internal static partial class DefaultLockServiceLog
    {
        [LoggerMessage(EventId = DiServerEventIds.DefaultLockService + 0, Level = LogLevel.Debug,
            Message = "Lock acquired on {ElementId} by {SessionId} ({User}, context={Context}).")]
        public static partial void LockAcquired(
            this ILogger logger,
            NodeId elementId,
            NodeId sessionId,
            string? user,
            string? context);

        [LoggerMessage(EventId = DiServerEventIds.DefaultLockService + 1, Level = LogLevel.Information,
            Message = "Lock on {ElementId} forcibly broken; previous owner {SessionId}.")]
        public static partial void LockForciblyBroken(this ILogger logger, NodeId elementId, NodeId sessionId);

        [LoggerMessage(EventId = DiServerEventIds.DefaultLockService + 2, Level = LogLevel.Debug,
            Message = "Lock on {ElementId} released after session {SessionId} closed.")]
        public static partial void LockReleasedAfterSessionClosed(
            this ILogger logger,
            NodeId elementId,
            NodeId sessionId);
    }
}
