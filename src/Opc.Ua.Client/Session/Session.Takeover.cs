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
 *
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
    /// Optional session capability for handing a server session over to another owner.
    /// </summary>
    /// <remarks>
    /// Raw and managed SDK sessions implement this capability; <see cref="ISession"/> itself is
    /// unchanged, so other implementations need not. Event handlers must be short and must not
    /// wait for session or subscription operations.
    /// </remarks>
    public interface ISessionTakeover
    {
        /// <summary>
        /// Gets a counter that increments whenever the session becomes bound to a different
        /// server session: on creation, on an in-place recreation and when saved session
        /// state for another server session is loaded. It does not change when a reconnect
        /// reactivates the same server session, and it is unrelated to subscription
        /// generations: a successful subscription transfer keeps subscription state valid
        /// even though the session generation changes.
        /// </summary>
        long SessionGeneration { get; }

        /// <summary>
        /// Raised after <see cref="SessionGeneration"/> changed. Raised outside the session lock.
        /// </summary>
        event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <summary>
        /// Retires this owner but keeps the server session and its subscriptions alive, so
        /// another client can take them over by reactivating the saved session. No further
        /// service request is sent from this session, including CloseSession,
        /// DeleteSubscriptions, CreateSession and ActivateSession; recovery and publishing stop
        /// and local resources are released. Capture the session and subscription state
        /// before calling this method. Graceful close and dispose keep their deletion semantics.
        /// </summary>
        /// <param name="ct">Cancels only before retirement starts; once started it completes.</param>
        /// <exception cref="ServiceResultException"><see cref="StatusCodes.BadInvalidState"/> when
        /// a close or dispose that does not retain the server session started first.</exception>
        ValueTask RetireAndPreserveAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Describes a change of the server session a client session is bound to.
    /// </summary>
    public sealed class SessionGenerationChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the event arguments.
        /// </summary>
        /// <param name="generation">The new session generation.</param>
        /// <param name="previousSessionId">The previously bound server session id, or null.</param>
        /// <param name="sessionId">The newly bound server session id.</param>
        public SessionGenerationChangedEventArgs(long generation, NodeId previousSessionId, NodeId sessionId)
        {
            Generation = generation;
            PreviousSessionId = previousSessionId;
            SessionId = sessionId;
        }

        /// <summary>
        /// Gets the new session generation.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the previously bound server session id, or a null node id for the first one.
        /// </summary>
        public NodeId PreviousSessionId { get; }

        /// <summary>
        /// Gets the newly bound server session id.
        /// </summary>
        public NodeId SessionId { get; }
    }

    public partial class Session : ISessionTakeover
    {
        /// <inheritdoc/>
        /// <remarks>
        /// A session created through <see cref="RecreateAsync(CancellationToken)"/> continues
        /// the generation of the session it was cloned from.
        /// </remarks>
        public long SessionGeneration => Interlocked.Read(ref m_sessionGeneration);

        /// <inheritdoc/>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <inheritdoc/>
        public async ValueTask RetireAndPreserveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            AbandonServerSession();
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Records the server session this client is now bound to, queuing a generation change
        /// when it differs from the last one. Called with the request context identity lock held.
        /// </summary>
        private void ObserveServerSession(NodeId sessionId)
        {
            if (sessionId.IsNull || sessionId == m_lastServerSessionId)
            {
                return;
            }
            long generation = Interlocked.Increment(ref m_sessionGeneration);
            m_pendingSessionGenerationChanges.Enqueue(
                new SessionGenerationChangedEventArgs(generation, m_lastServerSessionId, sessionId));
            m_lastServerSessionId = sessionId;
        }

        /// <summary>
        /// Raises queued generation changes. Called through <see cref="RaisePendingLifecycleEvents"/>.
        /// </summary>
        private void RaisePendingSessionGenerationChanges()
        {
            while (m_pendingSessionGenerationChanges.TryDequeue(out SessionGenerationChangedEventArgs? args))
            {
                RaiseAcknowledgementEvent(SessionGenerationChanged, args, this, m_logger);
            }
        }

        /// <summary>
        /// Continues the generation of a session this one is cloned from.
        /// </summary>
        private void InheritSessionGeneration(Session template)
        {
            Interlocked.Exchange(ref m_sessionGeneration, template.SessionGeneration);
            m_lastServerSessionId = template.m_lastServerSessionId;
        }

        private long m_sessionGeneration;
        private NodeId m_lastServerSessionId;

        /// <summary>
        /// Set once the server session is retained for another owner; no request is sent afterwards.
        /// </summary>
        private volatile bool m_serverSessionRetained;
        private readonly ConcurrentQueue<SessionGenerationChangedEventArgs> m_pendingSessionGenerationChanges = new();
    }
}
