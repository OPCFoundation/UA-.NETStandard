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
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server.Locking
{
    /// <summary>
    /// Default in-memory <see cref="ILockService"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lock records are kept in a <see cref="Dictionary{TKey, TValue}"/>
    /// keyed by topology-element <see cref="NodeId"/>. Each record
    /// stores the owning <c>SessionId</c>, the client-supplied
    /// context string, the owning user identity (from
    /// <see cref="ISystemContext.UserId"/>), and an expiry
    /// timestamp.
    /// </para>
    /// <para>
    /// <see cref="AttachToSessionManager"/> hooks the hosting <see cref="ISessionManager.SessionClosing"/>
    /// event so that locks held by a disconnecting session are released
    /// automatically.
    /// </para>
    /// </remarks>
    public sealed class DefaultLockService : ILockService, ILockOwnershipService, IDisposable
    {
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
            lock (m_lock)
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
        }

        /// <inheritdoc/>
        public LockState GetState(NodeId elementId)
        {
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            lock (m_lock)
            {
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

                    m_records.Remove(elementId);
                }
            }

            return new LockState(false, string.Empty, string.Empty, 0.0);
        }

        /// <inheritdoc/>
        public bool IsLockOwner(ISystemContext context, NodeId elementId)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }

            NodeId sessionId = ResolveSessionId(context);
            if (sessionId.IsNull)
            {
                return false;
            }

            lock (m_lock)
            {
                return m_records.TryGetValue(elementId, out Record? record) &&
                    record.SessionId == sessionId &&
                    record.ExpiresAt > m_timeProvider.GetUtcNow();
            }
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

            NodeId sessionId = ResolveSessionId(context);
            if (sessionId.IsNull)
            {
                return LockStatus.CouldNotLock;
            }
            string user = context.UserId ?? string.Empty;
            lock (m_lock)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (m_records.TryGetValue(elementId, out Record? existing) && existing.ExpiresAt > now)
                {
                    return LockStatus.AlreadyLocked;
                }
                m_records[elementId] = new Record(sessionId, clientContext, user, now + LockDuration);
            }

            m_logger?.LockAcquired(elementId, sessionId, user, clientContext);
            return LockStatus.Ok;
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
            NodeId sessionId = ResolveSessionId(context);
            lock (m_lock)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (!m_records.TryGetValue(elementId, out Record? existing) ||
                    existing.ExpiresAt <= now)
                {
                    m_records.Remove(elementId);
                    return LockStatus.NotLocked;
                }

                if (sessionId.IsNull || existing.SessionId != sessionId)
                {
                    return LockStatus.WrongClient;
                }

                existing.ExpiresAt = now + LockDuration;
                return LockStatus.Ok;
            }
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
            NodeId sessionId = ResolveSessionId(context);
            lock (m_lock)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (!m_records.TryGetValue(elementId, out Record? existing) ||
                    existing.ExpiresAt <= now)
                {
                    m_records.Remove(elementId);
                    return LockStatus.NotLocked;
                }

                if (sessionId.IsNull || existing.SessionId != sessionId)
                {
                    return LockStatus.WrongClient;
                }

                m_records.Remove(elementId);
                return LockStatus.Ok;
            }
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
            ServiceResult authorization = RoleAuthorizationGate.CheckAdmin(context);
            if (ServiceResult.IsBad(authorization))
            {
                throw new ServiceResultException(authorization);
            }

            Record existing;
            lock (m_lock)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (!m_records.TryGetValue(elementId, out Record? record) ||
                    record.ExpiresAt <= now)
                {
                    m_records.Remove(elementId);
                    return LockStatus.NotLocked;
                }

                existing = record;
                m_records.Remove(elementId);
            }
            m_logger?.LockForciblyBroken(elementId, existing.SessionId);
            return LockStatus.Ok;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                if (m_sessionManager != null && m_sessionClosingHandler != null)
                {
                    m_sessionManager.SessionClosing -= m_sessionClosingHandler;
                    m_sessionManager = null;
                    m_sessionClosingHandler = null;
                }
                m_records.Clear();
            }
        }

        /// <summary>
        /// Releases the closing session's locks using the same session identifier
        /// exposed by server request contexts.
        /// </summary>
        private void OnSessionClosing(ISession session, SessionEventReason reason)
        {
            NodeId sessionId = session.Id;
            var released = new List<NodeId>();
            lock (m_lock)
            {
                foreach (KeyValuePair<NodeId, Record> kv in m_records)
                {
                    if (kv.Value.SessionId == sessionId)
                    {
                        released.Add(kv.Key);
                    }
                }
                foreach (NodeId elementId in released)
                {
                    m_records.Remove(elementId);
                }
            }
            foreach (NodeId elementId in released)
            {
                m_logger?.LockReleasedAfterSessionClosed(elementId, sessionId);
            }
        }

        /// <summary>
        /// Resolves the session identifier without deriving identity from a user name.
        /// </summary>
        private static NodeId ResolveSessionId(ISystemContext context)
        {
            return context is ISessionSystemContext { SessionId: NodeId sessionId } ? sessionId : NodeId.Null;
        }

        /// <summary>Default duration for a newly acquired or renewed lock.</summary>
        private static readonly TimeSpan s_defaultLockDuration = TimeSpan.FromMinutes(5);

        /// <summary>Serializes lock transitions with ownership checks and session cleanup.</summary>
        private readonly Lock m_lock = new();

        /// <summary>Lock records keyed by topology element.</summary>
        private readonly Dictionary<NodeId, Record> m_records = [];

        /// <summary>Optional lock lifecycle logger.</summary>
        private readonly ILogger<DefaultLockService>? m_logger;

        /// <summary>Clock used for expiry decisions.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>Session manager supplying close notifications.</summary>
        private ISessionManager? m_sessionManager;

        /// <summary>Registered close notification handler.</summary>
        private SessionEventHandler? m_sessionClosingHandler;

        /// <summary>
        /// Internal lock-record. <see cref="ExpiresAt"/> is mutable so
        /// <see cref="RenewLock"/> can update it while lock transitions are serialized.
        /// </summary>
        private sealed class Record
        {
            /// <summary>
            /// Creates an owned lock with an absolute expiry.
            /// </summary>
            public Record(NodeId sessionId, string clientContext, string user, DateTimeOffset expiresAt)
            {
                SessionId = sessionId;
                ClientContext = clientContext;
                User = user;
                ExpiresAt = expiresAt;
            }

            /// <summary>
            /// Owning server session identifier.
            /// </summary>
            public NodeId SessionId { get; }

            /// <summary>
            /// Client-supplied descriptive context.
            /// </summary>
            public string ClientContext { get; }

            /// <summary>
            /// User display name, used only for diagnostics.
            /// </summary>
            public string User { get; }

            /// <summary>
            /// Absolute expiry of the lock.
            /// </summary>
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
            [UserIdentity] string? user,
            [SensitiveText] string? context);

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
