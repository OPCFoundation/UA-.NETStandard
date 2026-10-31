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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Capability of a session to hand its server session over to a new owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The previous owner calls <see cref="RetireAsync"/>, which returns a
    /// <see cref="SessionHandoff"/>. The new owner creates a managed session from it with
    /// <see cref="ManagedSessionOptions.TakeoverFrom"/> or
    /// <see cref="ManagedSessionBuilder.TakeOver(SessionHandoff, SessionTakeoverOptions?)"/>; that
    /// session reactivates the same server session on its own secure channel. The server session
    /// and its subscriptions are never closed in between.
    /// </para>
    /// <para>
    /// Event handlers must be short and must not wait for session or subscription operations.
    /// </para>
    /// </remarks>
    public interface ISessionTakeover
    {
        /// <summary>
        /// Gets the number of distinct server sessions this client session has been bound to.
        /// It is 0 before the first server session and increases whenever the bound server
        /// session changes, for example when recovery creates a new server session.
        /// </summary>
        long SessionGeneration { get; }

        /// <summary>
        /// Raised after the client session was bound to a different server session.
        /// </summary>
        event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <summary>
        /// Retires this session and hands its server session over to a new owner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In one operation, the session stops keep-alive, publishing and recovery, waits for
        /// subscription work in flight, captures the server session and its subscriptions,
        /// and fences the server session: no CloseSession or reconnect request is sent for it
        /// afterwards. Then the session releases its local resources and is disposed. Every
        /// later call on it fails.
        /// </para>
        /// <para>
        /// The handoff is valid until the server session times out, which is
        /// <see cref="SessionHandoff.RevisedSessionTimeout"/> after the last request the
        /// previous owner sent.
        /// </para>
        /// </remarks>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The state a new owner takes the server session over with.</returns>
        /// <exception cref="ServiceResultException">The session is not connected, is
        /// recovering, closing or already retired (<see cref="StatusCodes.BadNotConnected"/>
        /// or <see cref="StatusCodes.BadInvalidState"/>), or it uses the classic subscription
        /// engine (<see cref="StatusCodes.BadNotSupported"/>). When the operation fails before
        /// the fence was installed, the session is closed normally.</exception>
        ValueTask<SessionHandoff> RetireAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// The state a new owner takes a retired server session over with.
    /// </summary>
    /// <remarks>
    /// The handoff contains the authentication token of the server session and secrets of the
    /// secure session. Protect it like a credential when it is stored or sent to another process.
    /// User credentials are not part of the handoff; supply them with
    /// <see cref="SessionTakeoverOptions.Identity"/>.
    /// </remarks>
    public sealed record SessionHandoff
    {
        /// <summary>
        /// Gets the saved server session, including the endpoint it was created on. A handoff
        /// without a session is rejected.
        /// </summary>
        public SessionConfiguration Session { get; init; } = null!;

        /// <summary>
        /// Gets the subscriptions of the server session, one snapshot per partition. Only
        /// subscriptions of the default subscription engine are captured; a session using the
        /// classic engine cannot be handed over.
        /// </summary>
        public ArrayOf<SubscriptionStateSnapshot> Subscriptions { get; init; }

        /// <summary>
        /// Gets the session timeout, in milliseconds, the server revised for the session, or 0
        /// when it is not known. A new owner uses it for its keep-alive and recovery instead of
        /// the timeout it would request for a new session.
        /// </summary>
        public double RevisedSessionTimeout { get; init; }
    }

    /// <summary>
    /// What a managed session does when the takeover of a server session fails.
    /// </summary>
    public enum SessionTakeoverFailureMode
    {
        /// <summary>
        /// The connect fails with the activation error. No new server session is created.
        /// </summary>
        Throw,

        /// <summary>
        /// The session creates a new server session and recreates the subscriptions of the
        /// handoff on it. Notifications the previous server session still held are lost.
        /// </summary>
        CreateNewSession
    }

    /// <summary>
    /// Options of a managed session that takes a server session over from a
    /// <see cref="SessionHandoff"/>.
    /// </summary>
    public sealed record SessionTakeoverOptions
    {
        /// <summary>
        /// Gets the identity the server session is activated with. The handoff does not carry
        /// credentials; <c>null</c> uses the identity of the managed session options, or
        /// the identity token saved in the handoff when those have none.
        /// </summary>
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Gets the factory that creates the notification handler of each taken-over
        /// subscription. For a subscription with several partitions it is called once with
        /// the snapshot of the primary partition. Required when the handoff contains
        /// subscriptions.
        /// </summary>
        public Func<SubscriptionStateSnapshot, ISubscriptionNotificationHandler>? SubscriptionHandlerFactory { get; init; }

        /// <summary>
        /// Gets what the managed session does when the server session cannot be taken over.
        /// Defaults to <see cref="SessionTakeoverFailureMode.Throw"/>.
        /// </summary>
        public SessionTakeoverFailureMode FailureMode { get; init; } = SessionTakeoverFailureMode.Throw;
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

    public partial class Session
    {
        /// <summary>
        /// Gets the number of distinct server sessions this client session has been bound to.
        /// </summary>
        /// <remarks>
        /// A session created through <see cref="RecreateAsync(CancellationToken)"/> continues
        /// the generation of the session it was cloned from.
        /// </remarks>
        public long SessionGeneration => Interlocked.Read(ref m_sessionGeneration);

        /// <summary>
        /// Raised after the client session was bound to a different server session.
        /// </summary>
        public event EventHandler<SessionGenerationChangedEventArgs>? SessionGenerationChanged;

        /// <summary>
        /// Gets whether the server session is retained for another owner.
        /// </summary>
        internal bool IsServerSessionRetained => Volatile.Read(ref m_serverSessionFate) == kServerSessionRetained;

        /// <summary>
        /// Validates the saved server session of a handoff before anything is sent with it.
        /// </summary>
        /// <param name="saved">The saved server session.</param>
        /// <param name="configured">The endpoint the new owner is configured for, or null to
        /// use the endpoint of the saved session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ServiceResultException">The state is incomplete, its server nonce
        /// is invalid, or it belongs to another endpoint, security configuration or server
        /// certificate.</exception>
        internal static void ValidateHandoff(
            SessionConfiguration saved,
            EndpointDescription? configured,
            ITelemetryContext telemetry)
        {
            if (saved.SessionId.IsNull || saved.AuthenticationToken.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The saved session state has no session id or authentication token.");
            }
            EndpointDescription? endpoint = saved.ConfiguredEndpoint?.Description;
            if (endpoint == null || string.IsNullOrEmpty(endpoint.EndpointUrl))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "The saved session state has no endpoint.");
            }
            if (configured != null)
            {
                if (!Uri.TryCreate(configured.EndpointUrl, UriKind.Absolute, out Uri? configuredUrl) ||
                    !Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? savedUrl) ||
                    configuredUrl != savedUrl)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "The saved session belongs to another endpoint.");
                }
                ServiceResultException? mismatch =
                    ManagedSession.GetSecurityConfigurationMismatch(configured, endpoint);
                if (mismatch != null)
                {
                    throw mismatch;
                }
                if (!IsSameServerCertificate(configured.ServerCertificate, endpoint.ServerCertificate, telemetry))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "The saved session belongs to another server certificate.");
                }
            }
            if (endpoint.SecurityMode != MessageSecurityMode.None &&
                !IsValidSavedServerNonce(saved.ServerNonce))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNonceInvalid,
                    "The saved server nonce must contain 32 to 128 non-zero bytes.");
            }
        }

        /// <summary>
        /// Returns whether two endpoint server certificate blobs identify the same server
        /// certificate. Both must be empty, or both must be present with the same leaf
        /// certificate; a certificate missing on one side only is a mismatch.
        /// </summary>
        /// <param name="current">The certificate blob of the configured endpoint.</param>
        /// <param name="saved">The certificate blob of the saved endpoint.</param>
        /// <param name="telemetry">The telemetry context.</param>
        internal static bool IsSameServerCertificate(
            ByteString current,
            ByteString saved,
            ITelemetryContext telemetry)
        {
            if (current.IsEmpty || saved.IsEmpty)
            {
                return current.IsEmpty && saved.IsEmpty;
            }
            if (current == saved)
            {
                return true;
            }
            using CertificateCollection currentChain = Utils.ParseCertificateChainBlob(current, telemetry);
            using CertificateCollection savedChain = Utils.ParseCertificateChainBlob(saved, telemetry);
            return currentChain.Count > 0 &&
                savedChain.Count > 0 &&
                currentChain[0].RawData.AsSpan().SequenceEqual(savedChain[0].RawData);
        }

        /// <summary>
        /// Activates the saved server session of a handoff on this session, which was created
        /// but never opened. No CreateSession request is sent: the saved authentication token is
        /// reused. On failure the caller must abandon the server session, never close it.
        /// </summary>
        /// <param name="saved">The validated saved server session.</param>
        /// <param name="revisedSessionTimeout">The session timeout the server revised, in
        /// milliseconds, or 0 when it is not known.</param>
        /// <param name="identity">The identity to activate the server session with.</param>
        /// <param name="preferredLocales">The preferred locales configured for the session.</param>
        /// <param name="onActivated">Invoked as soon as ActivateSession succeeded, before the
        /// remaining setup, so the caller can capture the server nonce of that activation even
        /// when the setup fails afterwards.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ServiceResultException">The activation failed.</exception>
        internal async Task ActivateHandoffAsync(
            SessionConfiguration saved,
            double revisedSessionTimeout,
            IUserIdentity identity,
            ArrayOf<string> preferredLocales,
            Action? onActivated,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            if (TransportChannel is ManagedTransportChannelLease lease)
            {
                await lease.Entry.WaitForTransportAsync(ct).ConfigureAwait(false);
                using ClientChannelCertificateSnapshot certificates = lease.Entry.SnapshotClientCertificate();
                CertificateEntry? replacement = BuildInstanceCertificateEntry(
                    certificates.Certificate,
                    certificates.Chain);
                CertificateEntry? previous = m_instanceCertificateEntry;
                m_instanceCertificateEntry = replacement;
                m_effectiveEndpoint = m_endpoint;
                previous?.Dispose();
            }
            await LoadInstanceCertificateAsync(false, ct).ConfigureAwait(false);
            Restore(saved);
            lock (m_lock)
            {
                m_identity = identity;
                m_preferredLocales = preferredLocales;
                if (revisedSessionTimeout > 0)
                {
                    m_sessionTimeout = revisedSessionTimeout;
                }
                m_sessionClientCertificate = ByteString.From(m_instanceCertificateEntry?.Certificate.RawData);
            }
            await ReactivateExistingSessionAsync(identity, preferredLocales, m_serverNonce, ct)
                .ConfigureAwait(false);
            onActivated?.Invoke();
            await FetchNamespaceTablesAsync(this, ct).ConfigureAwait(false);
            await FetchOperationLimitsAsync(this, ct).ConfigureAwait(false);
            await StartKeepAliveTimerAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Captures the bound server session and fences it for another owner: afterwards no
        /// request, in particular no CloseSession, is sent for it, and the client is no longer
        /// bound to it.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The captured server session, with an endpoint the caller owns.</returns>
        /// <exception cref="ServiceResultException">The session is not connected, recovering
        /// or closing; nothing was changed.</exception>
        internal async Task<SessionConfiguration> RetainServerSessionAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            await m_reconnectLock.WaitAsync(ct).ConfigureAwait(false);
            SessionConfiguration saved;
            try
            {
                if (Reconnecting || ChannelRecoveryInProgress)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The session is recovering and cannot be handed over.");
                }
                if (!Connected)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotConnected,
                        "The session is not connected and cannot be handed over.");
                }
                Snapshot(out SessionConfiguration snapshot);
                ConfiguredEndpoint endpoint = CoreUtils.Clone(m_endpoint)!;
                saved = snapshot with { ConfiguredEndpoint = endpoint };
                if (Interlocked.CompareExchange(
                    ref m_serverSessionFate, kServerSessionRetained, kServerSessionOpen) == kServerSessionClosing)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidState,
                        "The session is already closing; its server session cannot be handed over.");
                }
            }
            finally
            {
                m_reconnectLock.Release();
            }
            AbandonServerSession();
            return saved;
        }

        /// <summary>
        /// Claims the close of the bound server session.
        /// </summary>
        /// <returns><c>false</c> when the server session is retained for another owner and
        /// must not be closed.</returns>
        private bool TryClaimServerSessionClose()
        {
            return Interlocked.CompareExchange(
                ref m_serverSessionFate, kServerSessionClosing, kServerSessionOpen) != kServerSessionRetained;
        }

        /// <summary>
        /// Closes a server session that a create attempt opened after the bound server session
        /// was retained for another owner. The request is sent on the channel of the attempt,
        /// past the fence, because the attempt owns this server session and nobody else can
        /// reach it.
        /// </summary>
        /// <param name="client">The client the attempt created the server session with.</param>
        /// <param name="sessionId">The id of the late server session.</param>
        /// <param name="authenticationToken">The authentication token of the late server session.</param>
        private Task CloseLateServerSessionAsync(SessionClient client, NodeId sessionId, NodeId authenticationToken)
        {
            ITransportChannel? channel = client.TransportChannel;
            if (channel == null || authenticationToken.IsNull)
            {
                return Task.CompletedTask;
            }
            return RetireServerSessionAsync(
                new RetiredServerSession(sessionId, authenticationToken, channel),
                disposeChannel: false);
        }

        /// <summary>
        /// Returns whether a saved server nonce is usable for a secure activation: OPC 10000-4
        /// requires 32 to 128 bytes, which must not all be zero.
        /// </summary>
        private static bool IsValidSavedServerNonce(ByteString serverNonce)
        {
            return !serverNonce.IsNull &&
                serverNonce.Length >= 32 &&
                serverNonce.Length <= 128 &&
                Nonce.ValidateNonce(serverNonce.ToArray(), MessageSecurityMode.Sign, 32);
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
        /// Raises generation changes queued while the session lock was held, in order. Does
        /// nothing while the current thread holds the session lock, so observers never run
        /// inside it.
        /// </summary>
        private void RaisePendingLifecycleEvents()
        {
            if (m_lock.IsHeldByCurrentThread)
            {
                return;
            }
            while (m_pendingSessionGenerationChanges.TryDequeue(out SessionGenerationChangedEventArgs? args))
            {
                RaiseSessionGenerationChanged(SessionGenerationChanged, args, this, m_logger);
            }
        }

        /// <summary>
        /// Invokes each generation observer, isolating failures so an observer cannot change
        /// the session outcome.
        /// </summary>
        internal static void RaiseSessionGenerationChanged(
            EventHandler<SessionGenerationChangedEventArgs>? handlers,
            SessionGenerationChangedEventArgs args,
            object sender,
            ILogger logger)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (EventHandler<SessionGenerationChangedEventArgs> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(sender, args);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    logger.SessionGenerationObserverFailed(exception);
                }
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
        private int m_serverSessionFate;
        private readonly ConcurrentQueue<SessionGenerationChangedEventArgs> m_pendingSessionGenerationChanges = new();
    }

    /// <summary>
    /// Source-generated log messages for <see cref="Session"/> takeovers.
    /// </summary>
    internal static partial class SessionLog
    {
        [LoggerMessage(EventId = ClientEventIds.Session + 73, Level = LogLevel.Error,
            Message = "Session: Session generation observer failed.")]
        public static partial void SessionGenerationObserverFailed(this ILogger logger, Exception exception);
    }
}