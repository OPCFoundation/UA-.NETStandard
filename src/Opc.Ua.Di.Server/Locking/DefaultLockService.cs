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
    /// Lock records are kept in a dictionary
    /// keyed by topology-element <see cref="NodeId"/>. Each record
    /// stores the owning <c>SessionId</c>, the client application URI,
    /// the owning user identity (from
    /// <see cref="ISystemContext.UserId"/>), and an expiry
    /// timestamp.
    /// </para>
    /// <para>
    /// <see cref="DiNodeManager.RegisterLockableElementAsync"/> retains admitted requests and
    /// releases session ownership after those requests finish. Applications using the service
    /// without subtree admission can use <see cref="AttachToSessionManager"/> for session cleanup.
    /// </para>
    /// </remarks>
    public sealed class DefaultLockService : ILockActivityService, IDisposable
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
            if (LockDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lockDuration));
            }
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
            lock (m_gate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (TryGetActiveRecord(elementId, now, out Record? record))
                {
                    return new LockState(
                        Locked: true,
                        LockingClient: record.ClientContext,
                        LockingUser: record.User,
                        RemainingLockTimeSeconds: record.Activities > 0
                            ? LockDuration.TotalSeconds
                            : (record.ExpiresAt - now).TotalSeconds);
                }
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
            lock (m_gate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (TryGetActiveRecord(elementId, now, out _))
                {
                    return LockStatus.AlreadyLocked;
                }
                int activities = 0;
                if (m_activities.TryGetValue(elementId, out Dictionary<NodeId, int>? active))
                {
                    foreach (KeyValuePair<NodeId, int> activity in active)
                    {
                        if (activity.Key != sessionId)
                        {
                            return LockStatus.CouldNotLock;
                        }
                        activities = activity.Value;
                    }
                }
                string client = (context as ServerSystemContext)?.OperationContext?.Session?.ClientApplicationUri ??
                    clientContext;
                m_records[elementId] = new Record(sessionId, client, user, now + LockDuration)
                {
                    Activities = activities
                };
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
            (NodeId sessionId, _) = ResolveCallerIdentity(context);
            lock (m_gate)
            {
                DateTimeOffset now = m_timeProvider.GetUtcNow();
                if (!TryGetActiveRecord(elementId, now, out Record? existing))
                {
                    return LockStatus.NotLocked;
                }
                if (existing.SessionId != sessionId)
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
            (NodeId sessionId, _) = ResolveCallerIdentity(context);
            lock (m_gate)
            {
                if (!TryGetActiveRecord(elementId, m_timeProvider.GetUtcNow(), out Record? existing))
                {
                    return LockStatus.NotLocked;
                }
                if (existing.SessionId != sessionId)
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
            Record existing;
            lock (m_gate)
            {
                if (!TryGetActiveRecord(elementId, m_timeProvider.GetUtcNow(), out Record? current))
                {
                    return LockStatus.NotLocked;
                }
                existing = current;
                m_records.Remove(elementId);
            }
            m_logger?.LockForciblyBroken(elementId, existing.SessionId);
            return LockStatus.Ok;
        }

        /// <inheritdoc/>
        public IDisposable? EnterActivity(ISystemContext context, NodeId elementId, bool modifying = true)
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
            lock (m_gate)
            {
                if (TryGetActiveRecord(elementId, m_timeProvider.GetUtcNow(), out Record? record))
                {
                    if (record.SessionId != sessionId)
                    {
                        if (!modifying)
                        {
                            return null;
                        }
                        throw new ServiceResultException(StatusCodes.BadLocked);
                    }
                    record.Activities++;
                }
                else if (!modifying)
                {
                    return null;
                }
                if (!m_activities.TryGetValue(elementId, out Dictionary<NodeId, int>? active))
                {
                    active = [];
                    m_activities.Add(elementId, active);
                }
                active.TryGetValue(sessionId, out int count);
                active[sessionId] = count + 1;
                return new Activity(() => CompleteActivity(elementId, sessionId));
            }
        }

        /// <inheritdoc/>
        public void ReleaseSession(NodeId elementId, NodeId sessionId)
        {
            if (elementId.IsNull)
            {
                throw new ArgumentNullException(nameof(elementId));
            }
            if (sessionId.IsNull)
            {
                throw new ArgumentNullException(nameof(sessionId));
            }
            Record? released = null;
            lock (m_gate)
            {
                if (m_records.TryGetValue(elementId, out Record? record) && record.SessionId == sessionId)
                {
                    if (record.Activities > 0)
                    {
                        record.SessionClosed = true;
                    }
                    else
                    {
                        m_records.Remove(elementId);
                        released = record;
                    }
                }
            }
            if (released != null)
            {
                m_logger?.LockReleasedAfterSessionClosed(elementId, released.SessionId);
            }
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
            lock (m_gate)
            {
                m_records.Clear();
                m_activities.Clear();
            }
        }

        /// <summary>
        /// Releases this session's ownership on every registered element.
        /// </summary>
        private void OnSessionClosing(ISession session, SessionEventReason reason)
        {
            NodeId sessionId = session.Id;
            var toRelease = new List<NodeId>();
            lock (m_gate)
            {
                foreach (KeyValuePair<NodeId, Record> kv in m_records)
                {
                    if (kv.Value.SessionId == sessionId)
                    {
                        toRelease.Add(kv.Key);
                    }
                }
            }
            foreach (NodeId elementId in toRelease)
            {
                ReleaseSession(elementId, sessionId);
            }
        }

        /// <summary>
        /// Finds an unexpired or active record while the service gate is held.
        /// </summary>
        private bool TryGetActiveRecord(
            NodeId elementId,
            DateTimeOffset now,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Record? record)
        {
            if (m_records.TryGetValue(elementId, out record) &&
                (record.Activities > 0 || record.ExpiresAt > now))
            {
                return true;
            }
            m_records.Remove(elementId);
            record = null;
            return false;
        }

        /// <summary>
        /// Starts inactivity timing after the last admitted activity without restoring released records.
        /// </summary>
        private void CompleteActivity(NodeId elementId, NodeId sessionId)
        {
            bool released = false;
            lock (m_gate)
            {
                if (m_activities.TryGetValue(elementId, out Dictionary<NodeId, int>? active) &&
                    active.TryGetValue(sessionId, out int count))
                {
                    if (count == 1)
                    {
                        active.Remove(sessionId);
                        if (active.Count == 0)
                        {
                            m_activities.Remove(elementId);
                        }
                    }
                    else
                    {
                        active[sessionId] = count - 1;
                    }
                }
                if (m_records.TryGetValue(elementId, out Record? record) && record.SessionId == sessionId)
                {
                    record.Activities--;
                    if (record.Activities == 0)
                    {
                        if (record.SessionClosed)
                        {
                            m_records.Remove(elementId);
                            released = true;
                        }
                        else
                        {
                            record.ExpiresAt = m_timeProvider.GetUtcNow() + LockDuration;
                        }
                    }
                }
            }
            if (released)
            {
                m_logger?.LockReleasedAfterSessionClosed(elementId, sessionId);
            }
        }

        /// <summary>
        /// Uses session identity for UA requests and retains the direct-context compatibility fallback.
        /// </summary>
        private static (NodeId SessionId, string User) ResolveCallerIdentity(ISystemContext context)
        {
            // The standard server context carries the session id;
            // fall back to a deterministic synthetic id derived from
            // the user when the context is a non-server SystemContext
            // (e.g. when invoked from tests).
            if (context is ISessionSystemContext server &&
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

        /// <summary>The default inactivity timeout.</summary>
        private static readonly TimeSpan s_defaultLockDuration = TimeSpan.FromMinutes(5);

        /// <summary>Ownership records protected by the service gate.</summary>
        private readonly Dictionary<NodeId, Record> m_records = [];

        /// <summary>Admitted requests, including requests that began without ownership.</summary>
        private readonly Dictionary<NodeId, Dictionary<NodeId, int>> m_activities = [];

        /// <summary>Protects ownership and activity transitions.</summary>
        private readonly Lock m_gate = new();

        /// <summary>The optional service logger.</summary>
        private readonly ILogger<DefaultLockService>? m_logger;

        /// <summary>The inactivity clock.</summary>
        private readonly TimeProvider m_timeProvider;

        /// <summary>The optional session source used by direct consumers.</summary>
        private ISessionManager? m_sessionManager;

        /// <summary>The subscription owned by the direct-consumer session attachment.</summary>
        private SessionEventHandler? m_sessionClosingHandler;

        /// <summary>
        /// Session ownership, inactivity expiry and admitted requests, protected by the service gate.
        /// </summary>
        private sealed class Record
        {
            /// <summary>
            /// Creates an ownership record before any request is admitted.
            /// </summary>
            public Record(NodeId sessionId, string clientContext, string user, DateTimeOffset expiresAt)
            {
                SessionId = sessionId;
                ClientContext = clientContext;
                User = user;
                ExpiresAt = expiresAt;
            }

            /// <summary>
            /// Gets the owning session.
            /// </summary>
            public NodeId SessionId { get; }

            /// <summary>
            /// Gets the client application URI, or the direct consumer's context string.
            /// </summary>
            public string ClientContext { get; }

            /// <summary>
            /// Gets the owning user's identifier.
            /// </summary>
            public string User { get; }

            /// <summary>
            /// Gets or sets the end of the inactivity period.
            /// </summary>
            public DateTimeOffset ExpiresAt { get; set; }

            /// <summary>
            /// Gets or sets the number of admitted owner requests.
            /// </summary>
            public int Activities { get; set; }

            /// <summary>
            /// Gets or sets whether ownership must be released after request drain.
            /// </summary>
            public bool SessionClosed { get; set; }
        }

        /// <summary>
        /// Completes one admitted owner request exactly once.
        /// </summary>
        private sealed class Activity(Action complete) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_complete, null)?.Invoke();
            }

            /// <summary>The completion callback, cleared on first disposal; the service remains borrowed.</summary>
            private Action? m_complete = complete;
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
