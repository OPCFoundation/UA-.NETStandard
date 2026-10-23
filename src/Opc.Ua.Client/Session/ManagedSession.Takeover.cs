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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    public partial class ManagedSession : ISessionTakeover
    {
        /// <inheritdoc/>
        /// <remarks>
        /// The managed generation is monotonic for the lifetime of this facade, also when the
        /// inner session object is replaced.
        /// </remarks>
        public long SessionGeneration => Interlocked.Read(ref m_sessionGeneration);

        /// <inheritdoc/>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <inheritdoc/>
        public bool IsPrepared => m_session?.IsPrepared ?? false;

        /// <inheritdoc/>
        /// <remarks>
        /// While prepared, the connection state is <see cref="ConnectionState.Disconnected"/>,
        /// <see cref="Connected"/> is <c>false</c> and service calls fail fast. No keep-alive,
        /// publishing, reconnect or failover runs until
        /// <see cref="ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>
        /// activates the saved session; then the configured recovery policy applies again. A
        /// failed or cancelled activation keeps the session prepared. Dispose waits for a
        /// preparation or activation in flight and cancels it.
        /// </remarks>
        public async ValueTask PrepareTakeoverAsync(
            SessionConfiguration savedSession,
            SessionTakeoverOptions? options = null,
            CancellationToken ct = default)
        {
            if (savedSession == null)
            {
                throw new ArgumentNullException(nameof(savedSession));
            }
            ThrowIfTakeoverUnavailable();
            options ??= new SessionTakeoverOptions();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_takeoverAbort.Token);
            await m_takeoverGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfTakeoverUnavailable();
                Session session = m_session ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotConnected,
                    "The managed session is not connected.");
                session.ValidateTakeoverState(savedSession);

                // The refresh loop takes the writer lock and would activate an identity; stop it first.
                await StopIdentityRefreshLoopAsync().ConfigureAwait(false);
                bool prepared = false;
                try
                {
                    using (await m_serviceLock.WriterLockAsync(linked.Token).ConfigureAwait(false))
                    {
                        if (!StateMachine.TryPark(s_preparedForTakeover))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadInvalidState,
                                "Only a connected managed session can be prepared for a takeover.");
                        }
                        try
                        {
                            await session.PrepareTakeoverCoreAsync(savedSession, options, linked.Token)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            prepared = session.IsPrepared;
                            if (!prepared)
                            {
                                // The own server session is unchanged and usable again.
                                StateMachine.Unpark(connected: true);
                            }
                        }
                    }
                }
                finally
                {
                    if (!prepared)
                    {
                        StartIdentityRefreshLoop();
                    }
                }
            }
            finally
            {
                m_takeoverGate.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask RetireAndPreserveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return AbandonServerSessionAsync();
        }

        /// <summary>
        /// Parks a connected session while a replica coordinator adopts a mirrored session into it,
        /// so a keep-alive or channel failure in between cannot start a recovery that creates
        /// another server session. A session that is not connected is not parked and keeps its
        /// current behavior.
        /// </summary>
        /// <returns><c>true</c> when the session was parked; end it with
        /// <see cref="EndMirroredSessionAdoption"/>.</returns>
        internal bool TryBeginMirroredSessionAdoption()
        {
            return StateMachine.TryPark(s_adoptingMirroredSession);
        }

        /// <summary>
        /// Ends a park started by <see cref="TryBeginMirroredSessionAdoption"/>.
        /// </summary>
        /// <param name="connected">Whether the session is usable, which reports it connected again.</param>
        internal void EndMirroredSessionAdoption(bool connected)
        {
            StateMachine.Unpark(connected);
        }

        /// <summary>
        /// Activates a prepared takeover and resumes normal operation and recovery.
        /// </summary>
        /// <exception cref="ServiceResultException">The activation failed; the session stays
        /// prepared.</exception>
        private async Task ActivatePreparedSessionAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct)
        {
            ThrowIfTakeoverUnavailable();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_takeoverAbort.Token);
            await m_takeoverGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfTakeoverUnavailable();
                Session session = InnerSession;
                if (!session.IsPrepared)
                {
                    if (StateMachine.IsConnected)
                    {
                        // A concurrent activation committed the takeover already.
                        return;
                    }
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The session is not prepared for a takeover.");
                }
                try
                {
                    using (await m_serviceLock.WriterLockAsync(linked.Token).ConfigureAwait(false))
                    {
                        if (connection == null && channel == null)
                        {
                            connection = await WaitForRecoveryConnectionAsync(
                                session.ConfiguredEndpoint, null, linked.Token).ConfigureAwait(false);
                        }
                        IManagedTransportChannel? previousChannel = session.ManagedChannel;
                        try
                        {
                            await session.ActivatePreparedSessionAsync(connection, channel, linked.Token)
                                .ConfigureAwait(false);
                        }
                        finally
                        {
                            RebindManagedChannelEvents(session, previousChannel);
                        }
                    }
                }
                finally
                {
                    if (!session.IsPrepared && !session.Disposed)
                    {
                        // The takeover committed, even when the call ended with an error afterwards.
                        StateMachine.Unpark(connected: true);
                        StartIdentityRefreshLoop();
                    }
                }
            }
            finally
            {
                m_takeoverGate.Release();
            }
        }

        /// <summary>
        /// Cancels a preparation or activation in flight and waits until it ended.
        /// </summary>
        private async Task AbortTakeoverAsync()
        {
            try
            {
                m_takeoverAbort.Cancel();
                await m_takeoverGate.WaitAsync().ConfigureAwait(false);
                m_takeoverGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed: nothing can be in flight.
            }
        }

        /// <summary>
        /// Claims the close of the server session the closing worker found.
        /// </summary>
        /// <returns><c>true</c> when the server session must be kept instead: it is retained
        /// for another owner, or it is a prepared takeover whose ownership never committed.</returns>
        private bool ClaimServerSessionCloseOrRetain(Session session)
        {
            if (session.IsPrepared)
            {
                Interlocked.CompareExchange(ref m_serverSessionFate, kServerSessionRetained, kServerSessionOpen);
                return true;
            }
            return Interlocked.CompareExchange(
                ref m_serverSessionFate, kServerSessionClosing, kServerSessionOpen) == kServerSessionRetained;
        }

        /// <summary>
        /// Throws when this session can no longer prepare or activate a takeover.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
        /// <exception cref="ServiceResultException">The session is retired.</exception>
        private void ThrowIfTakeoverUnavailable()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(ManagedSession));
            }
            if (GetRetirementFailure() is ServiceResult retired)
            {
                throw new ServiceResultException(retired);
            }
        }

        /// <summary>
        /// Forwards a server session change of the current inner session.
        /// </summary>
        private void OnInnerSessionGenerationChanged(object? sender, SessionGenerationChangedEventArgs e)
        {
            if (ReferenceEquals(sender, m_session))
            {
                ObserveServerSession(e.SessionId);
            }
        }

        /// <summary>
        /// Records the server session the facade is now bound to and raises
        /// <see cref="SessionGenerationChanged"/> when it differs from the previous one.
        /// </summary>
        private void ObserveServerSession(NodeId sessionId)
        {
            SessionGenerationChangedEventArgs args;
            lock (m_sessionGenerationLock)
            {
                if (sessionId.IsNull || sessionId == m_observedServerSessionId)
                {
                    return;
                }
                args = new SessionGenerationChangedEventArgs(
                    Interlocked.Increment(ref m_sessionGeneration), m_observedServerSessionId, sessionId);
                m_observedServerSessionId = sessionId;
            }
            Session.RaiseSessionGenerationChanged(SessionGenerationChanged, args, this, m_logger);
        }

        /// <summary>
        /// The failure a connect, reconnect or failover attempt reports once this session is
        /// retiring: it must not create or reactivate any server session.
        /// </summary>
        private ServiceResult? GetRetirementFailure()
        {
            return Volatile.Read(ref m_serverSessionFate) == kServerSessionRetained
                ? new ServiceResult(StatusCodes.BadSessionClosed, new LocalizedText(
                    "The server session was retained for another owner."))
                : null;
        }

        /// <summary>
        /// The server session may be closed or retained.
        /// </summary>
        private const int kServerSessionOpen = 0;

        /// <summary>
        /// A close was committed; the server session can no longer be retained.
        /// </summary>
        private const int kServerSessionClosing = 1;

        /// <summary>
        /// The server session is retained for another owner; nothing may close it.
        /// </summary>
        private const int kServerSessionRetained = 2;

        /// <summary>
        /// Reported to service calls while a takeover is prepared.
        /// </summary>
        private static readonly ServiceResult s_preparedForTakeover = new(
            StatusCodes.BadSessionNotActivated,
            new LocalizedText("The session is prepared for a takeover; activate it with ReconnectAsync first."));

        /// <summary>
        /// Reported to service calls while a replica coordinator adopts a mirrored session.
        /// </summary>
        private static readonly ServiceResult s_adoptingMirroredSession = new(
            StatusCodes.BadSessionNotActivated,
            new LocalizedText("The session is adopting a mirrored session."));

        private readonly Lock m_sessionGenerationLock = new();
        private readonly SemaphoreSlim m_takeoverGate = new(1, 1);
        private readonly CancellationTokenSource m_takeoverAbort = new();
        private long m_sessionGeneration;
        private NodeId m_observedServerSessionId;
    }
}
