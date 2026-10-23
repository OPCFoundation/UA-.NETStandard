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
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Optional session capability for handing a server session over between owners.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raw and managed SDK sessions implement this capability; <see cref="ISession"/> itself is
    /// unchanged, so other implementations need not. Event handlers must be short and must not
    /// wait for session or subscription operations.
    /// </para>
    /// <para>
    /// A takeover runs in two stages. <see cref="PrepareTakeoverAsync"/> closes this session's
    /// own (helper) server session on its channel and loads the saved state of the session to
    /// take over without activating it. A later
    /// <see cref="ISession.ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>
    /// activates exactly that saved session by reusing its authentication token; it never
    /// creates another server session. The previous owner releases the session with
    /// <see cref="RetireAndPreserveAsync"/>.
    /// </para>
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
        /// Gets whether saved session state was loaded by <see cref="PrepareTakeoverAsync"/>
        /// and is waiting for its activation. While prepared, no keep-alive, publishing,
        /// recovery or service request runs, and a close or dispose leaves the saved server
        /// session alive because its ownership was never committed.
        /// </summary>
        bool IsPrepared { get; }

        /// <summary>
        /// Raised after <see cref="SessionGeneration"/> changed. Raised outside the session lock.
        /// </summary>
        event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <summary>
        /// Prepares this session to take over a saved server session. The saved state must
        /// belong to the endpoint, security mode, security policy and server certificate of
        /// this session; it is validated before anything changes. Keep-alive, publishing and
        /// recovery stop, this session's own server session is closed on its channel, which is
        /// kept, and the saved state is loaded without activating it. The server-revised
        /// session timeout of this session is kept. Activate the saved session with
        /// <see cref="ISession.ReconnectAsync(ITransportWaitingConnection, ITransportChannel, CancellationToken)"/>;
        /// a failed or cancelled activation leaves the session prepared, so it may be retried.
        /// </summary>
        /// <param name="savedSession">The saved session state, for example from
        /// <see cref="ISession.SaveSessionConfiguration"/> of the previous owner.</param>
        /// <param name="options">The takeover options, or <c>null</c> for the defaults.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentNullException"><paramref name="savedSession"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">The saved state is invalid or does not match
        /// this session (nothing changed), the session is recovering, retired or not connected,
        /// or closing this session's own server session failed (the session stays usable).</exception>
        ValueTask PrepareTakeoverAsync(
            SessionConfiguration savedSession,
            SessionTakeoverOptions? options = null,
            CancellationToken ct = default);

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
    /// Options of <see cref="ISessionTakeover.PrepareTakeoverAsync"/>.
    /// </summary>
    public sealed record SessionTakeoverOptions
    {
        /// <summary>
        /// Gets the identity the saved session is activated with. Saved state does not carry
        /// secrets; <c>null</c> keeps the identity of this session.
        /// </summary>
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Gets whether closing this session's own server session also deletes its
        /// subscriptions on the server. Defaults to <c>true</c>.
        /// </summary>
        public bool DeleteHelperSubscriptions { get; init; } = true;
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
        /// <remarks>
        /// Unlike a managed session, a raw session keeps reporting <see cref="SessionClient.Connected"/>
        /// while prepared, as it does after <see cref="ApplySessionConfiguration"/>.
        /// </remarks>
        public bool IsPrepared => Volatile.Read(ref m_takeoverState) == kTakeoverPrepared;

        /// <inheritdoc/>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <summary>
        /// Gets whether a takeover is being prepared or waits for its activation, so the
        /// subscription engines must not publish.
        /// </summary>
        internal bool IsTakeoverStaged => Volatile.Read(ref m_takeoverState) != kTakeoverNone;

        /// <summary>
        /// Gets whether the server session is retained for another owner.
        /// </summary>
        internal bool IsServerSessionRetained => Volatile.Read(ref m_serverSessionFate) == kServerSessionRetained;

        /// <inheritdoc/>
        public ValueTask PrepareTakeoverAsync(
            SessionConfiguration savedSession,
            SessionTakeoverOptions? options = null,
            CancellationToken ct = default)
        {
            if (savedSession == null)
            {
                throw new ArgumentNullException(nameof(savedSession));
            }
            ThrowIfDisposed();
            ValidateTakeoverState(savedSession);
            return new ValueTask(PrepareTakeoverCoreAsync(savedSession, options ?? new SessionTakeoverOptions(), ct));
        }

        /// <inheritdoc/>
        public async ValueTask RetireAndPreserveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(
                ref m_serverSessionFate, kServerSessionRetained, kServerSessionOpen) == kServerSessionClosing)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The session is already closing; its server session was not retained.");
            }
            AbandonServerSession();
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Validates saved session state against the endpoint of this session before a
        /// takeover changes anything.
        /// </summary>
        /// <exception cref="ServiceResultException">The state is incomplete or belongs to
        /// another endpoint, security configuration or server certificate.</exception>
        internal void ValidateTakeoverState(SessionConfiguration savedSession)
        {
            if (savedSession.SessionId.IsNull || savedSession.AuthenticationToken.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The saved session state has no session id or authentication token.");
            }
            EndpointDescription current = m_endpoint.Description;
            EndpointDescription? saved = savedSession.ConfiguredEndpoint?.Description;
            if (saved == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The saved session state has no endpoint.");
            }
            if (!Uri.TryCreate(current.EndpointUrl, UriKind.Absolute, out Uri? currentUrl) ||
                !Uri.TryCreate(saved.EndpointUrl, UriKind.Absolute, out Uri? savedUrl) ||
                currentUrl != savedUrl)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The saved session belongs to another endpoint.");
            }
            ServiceResultException? mismatch = ManagedSession.GetSecurityConfigurationMismatch(current, saved);
            if (mismatch != null)
            {
                throw mismatch;
            }
            if (!IsSameServerCertificate(current.ServerCertificate, saved.ServerCertificate))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The saved session belongs to another server certificate.");
            }
            if (current.SecurityMode != MessageSecurityMode.None && savedSession.ServerNonce.IsEmpty)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The saved session state has no server nonce.");
            }
        }

        /// <summary>
        /// Quiesces the session, closes its own server session (unless a previous
        /// preparation already did) and loads the saved state without activating it.
        /// </summary>
        /// <exception cref="ServiceResultException">The session is retired or recovering, or
        /// closing its own server session failed.</exception>
        internal async Task PrepareTakeoverCoreAsync(
            SessionConfiguration savedSession,
            SessionTakeoverOptions options,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            if (IsServerSessionRetained)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSessionClosed,
                    "The server session was retained for another owner.");
            }

            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            if (Reconnecting || ChannelRecoveryInProgress)
            {
                m_reconnectLock.Release();
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The session is reconnecting and cannot be prepared for a takeover.");
            }
            Reconnecting = true;
            m_reconnectLock.Release();
            try
            {
                if (!IsPrepared)
                {
                    await CloseOwnSessionForTakeoverAsync(options.DeleteHelperSubscriptions, ct)
                        .ConfigureAwait(false);
                }
                IUserIdentity identity = options.Identity ?? m_identity;
                Restore(savedSession);
                m_identity = identity;
            }
            finally
            {
                await m_reconnectLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                Reconnecting = false;
                m_reconnectLock.Release();
            }
        }

        /// <summary>
        /// Activates the prepared session by reusing its authentication token, on the supplied
        /// channel or connection, or on the retained channel. A retained channel that lost its
        /// transport is reconnected once. Never creates another server session.
        /// </summary>
        /// <exception cref="ServiceResultException">The session is not prepared, or the
        /// activation failed; the session then stays prepared.</exception>
        internal async Task ActivatePreparedSessionAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            if (!IsPrepared)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The session is not prepared for a takeover.");
            }
            try
            {
                Volatile.Write(ref m_activatingTakeover, true);
                try
                {
                    await ActivatePreparedSessionOnChannelAsync(connection, channel, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException sre) when (
                    connection == null &&
                    channel == null &&
                    IsTransportFailure(sre.StatusCode) &&
                    NullableTransportChannel is ITransportChannel retained &&
                    (retained.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                {
                    await retained.ReconnectAsync(ct: ct).ConfigureAwait(false);
                    await ActivatePreparedSessionOnChannelAsync(null, null, ct).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!IsPrepared && !Disposed)
            {
                // The activation committed, but a later step (for example the caller's cancellation)
                // ended the call before the session was started. Ownership moved, so finish starting it.
                await StartKeepAliveTimerAsync().ConfigureAwait(false);
            }
            catch
            {
                if (IsPrepared && !Disposed)
                {
                    // The failed activation may have resumed publishing; nothing may run until it succeeds.
                    await QuiesceForTakeoverAsync(CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                Volatile.Write(ref m_activatingTakeover, false);
            }
            if (m_engine is ClassicSubscriptionEngine)
            {
                StartPublishing(OperationTimeout, false);
            }
        }

        /// <summary>
        /// Claims the close of the bound server session.
        /// </summary>
        /// <returns><c>false</c> when the server session must not be closed: it is retained
        /// for another owner, or it is prepared and its ownership was never committed.</returns>
        private bool TryClaimServerSessionClose()
        {
            if (IsPrepared)
            {
                return false;
            }
            return Interlocked.CompareExchange(
                ref m_serverSessionFate, kServerSessionClosing, kServerSessionOpen) != kServerSessionRetained;
        }

        /// <summary>
        /// Ends a takeover stage after the session released its server session locally.
        /// </summary>
        private void ReleaseTakeoverStage()
        {
            Volatile.Write(ref m_takeoverState, kTakeoverNone);
        }

        /// <summary>
        /// Marks a successful activation of the bound server session, which commits a
        /// prepared takeover.
        /// </summary>
        private void CommitTakeoverActivation()
        {
            Interlocked.CompareExchange(ref m_takeoverState, kTakeoverNone, kTakeoverPrepared);
        }

        /// <summary>
        /// Throws when the session is prepared for a takeover, unless the takeover activation itself
        /// runs: only that activation may reach the saved session.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ThrowIfPreparedForTakeover()
        {
            if (IsPrepared && !Volatile.Read(ref m_activatingTakeover))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSessionNotActivated,
                    "The session is prepared for a takeover; activate it with ReconnectAsync first.");
            }
        }

        private Task ActivatePreparedSessionOnChannelAsync(
            ITransportWaitingConnection? connection,
            ITransportChannel? channel,
            CancellationToken ct)
        {
            ITransportChannel? target = channel ?? (connection == null ? TransportChannel : null);
            return RecreateInPlaceCoreAsync(
                endpoint: null,
                connection,
                target,
                budget: null,
                ct,
                recreateSubscriptions: false,
                requireTokenReuse: true,
                bindSuppliedChannel: channel != null);
        }

        private async Task CloseOwnSessionForTakeoverAsync(bool deleteSubscriptions, CancellationToken ct)
        {
            bool bound = Connected;
            Volatile.Write(ref m_takeoverState, kTakeoverQuiesced);
            try
            {
                await QuiesceForTakeoverAsync(ct).ConfigureAwait(false);
                if (bound)
                {
                    await CloseSessionAsync(null, deleteSubscriptions, ct).ConfigureAwait(false);
                    lock (m_lock)
                    {
                        SessionCreated(default, default);
                    }
                    RaisePendingLifecycleEvents();
                }
            }
            catch
            {
                ReleaseTakeoverStage();
                await ResumeAfterFailedTakeoverAsync(bound).ConfigureAwait(false);
                throw;
            }
            Volatile.Write(ref m_takeoverState, kTakeoverPrepared);
        }

        private async Task QuiesceForTakeoverAsync(CancellationToken ct)
        {
            await StopKeepAliveTimerAsync().ConfigureAwait(false);
            if (m_engine is DefaultSubscriptionEngine engine &&
                engine.SubscriptionManager is Subscriptions.SubscriptionManager manager)
            {
                manager.SetSessionRecoveryPaused(true);
                await manager.DrainAsync(ct).ConfigureAwait(false);
            }
            else
            {
                m_engine.PausePublishing();
            }
        }

        private async Task ResumeAfterFailedTakeoverAsync(bool bound)
        {
            try
            {
                await ResumePublishingAfterRecoveryAsync().ConfigureAwait(false);
                if (bound && Connected && !Disposed)
                {
                    await StartKeepAliveTimerAsync().ConfigureAwait(false);
                    if (m_engine is ClassicSubscriptionEngine)
                    {
                        StartPublishing(OperationTimeout, false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_logger.SessionTakeoverResumeFailed(ex);
            }
        }

        private bool IsSameServerCertificate(ByteString current, ByteString saved)
        {
            if (current.IsEmpty || saved.IsEmpty || current == saved)
            {
                return true;
            }
            using CertificateCollection currentChain = Utils.ParseCertificateChainBlob(current, m_telemetry);
            using CertificateCollection savedChain = Utils.ParseCertificateChainBlob(saved, m_telemetry);
            return currentChain.Count > 0 &&
                savedChain.Count > 0 &&
                currentChain[0].RawData.AsSpan().SequenceEqual(savedChain[0].RawData);
        }

        private static bool IsTransportFailure(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadSecureChannelClosed ||
                statusCode == StatusCodes.BadConnectionClosed ||
                statusCode == StatusCodes.BadNotConnected ||
                statusCode == StatusCodes.BadCommunicationError ||
                statusCode == StatusCodes.BadTcpInternalError ||
                statusCode == StatusCodes.BadNoCommunication;
        }

        /// <summary>
        /// Records the server session this client is now bound to, queuing a generation change
        /// when it differs from the last one. Called with the request context identity lock held.
        /// </summary>
        private void ObserveServerSession(NodeId sessionId)
        {
            if (sessionId.IsNull)
            {
                return;
            }

            // A newly bound server session may be closed or retained again.
            Interlocked.CompareExchange(ref m_serverSessionFate, kServerSessionOpen, kServerSessionClosing);
            if (sessionId == m_lastServerSessionId)
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

        /// <summary>
        /// No takeover is staged.
        /// </summary>
        private const int kTakeoverNone = 0;

        /// <summary>
        /// Keep-alive and publishing stopped while the own server session is being closed.
        /// </summary>
        private const int kTakeoverQuiesced = 1;

        /// <summary>
        /// Saved state is loaded and waits for its activation.
        /// </summary>
        private const int kTakeoverPrepared = 2;

        /// <summary>
        /// The bound server session may be closed or retained.
        /// </summary>
        private const int kServerSessionOpen = 0;

        /// <summary>
        /// A close of the bound server session was committed.
        /// </summary>
        private const int kServerSessionClosing = 1;

        /// <summary>
        /// The server session is retained for another owner; no request is sent afterwards.
        /// </summary>
        private const int kServerSessionRetained = 2;

        private long m_sessionGeneration;
        private NodeId m_lastServerSessionId;
        private int m_takeoverState;
        private int m_serverSessionFate;

        /// <summary>
        /// Set while the takeover activation runs, the only path allowed to reach a prepared session.
        /// </summary>
        private bool m_activatingTakeover;
        private readonly ConcurrentQueue<SessionGenerationChangedEventArgs> m_pendingSessionGenerationChanges = new();
    }

    /// <summary>
    /// Source-generated log messages for <see cref="Session"/> takeovers.
    /// </summary>
    internal static partial class SessionLog
    {
        [LoggerMessage(EventId = ClientEventIds.Session + 74, Level = LogLevel.Warning,
            Message = "Resuming the session after a failed takeover preparation failed.")]
        public static partial void SessionTakeoverResumeFailed(this ILogger logger, Exception exception);
    }
}
