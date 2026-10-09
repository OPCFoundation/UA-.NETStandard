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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

// TODO: RCS1256 — needs polyfill for net48
#pragma warning disable RCS1256 // Invalid argument null check

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class SessionManager : ISessionManager, ISessionBindingProvider
    {
        /// <summary>
        /// Initializes the manager with its configuration.
        /// </summary>
        public SessionManager(IServerInternal server, ApplicationConfiguration configuration)
            : this(server, configuration, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the manager with its configuration and an explicit
        /// <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server instance.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used for monotonic timeout
        /// calculations and client-lockout windows. When <c>null</c>, the
        /// time provider exposed by the server (via
        /// <see cref="ITimeProviderProvider"/>) is used, falling back to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public SessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            TimeProvider? timeProvider)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_logger = server.Telemetry.CreateLogger<SessionManager>();

            m_minSessionTimeout = configuration.ServerConfiguration!.MinSessionTimeout;
            m_maxSessionTimeout = configuration.ServerConfiguration.MaxSessionTimeout;
            m_maxSessionCount = configuration.ServerConfiguration.MaxSessionCount;
            m_maxFailedAuthenticationAttempts = configuration.ServerConfiguration
                .MaxFailedAuthenticationAttempts;
            m_maxRequestAge = configuration.ServerConfiguration.MaxRequestAge;
            m_httpsMutualTls = configuration.ServerConfiguration.HttpsMutualTls;
            m_maxBrowseContinuationPoints = configuration.ServerConfiguration
                .MaxBrowseContinuationPoints;
            m_maxHistoryContinuationPoints = configuration.ServerConfiguration
                .MaxHistoryContinuationPoints;

            // Lockout duration / failure expiration are stored in TimestampFrequency
            // ticks so they can be compared directly against TimeProvider.GetTimestamp().
            long ticksPerSecond = m_timeProvider.TimestampFrequency;
            m_lockoutDurationTicks = ticksPerSecond * 5 * 60;
            m_failureExpirationTicks = ticksPerSecond * 1 * 60;

            m_sessions = new NodeIdDictionary<ISession>(m_maxSessionCount);

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);
        }

        /// <summary>
        /// Gets or sets the built-in Session-less Service invocation
        /// (OPC 10000-4 §6.3). <see langword="null"/>, the default, disables
        /// it unless a <see cref="ValidateSessionLessRequest"/> handler
        /// decides.
        /// </summary>
        /// <remarks>
        /// While set, the Session-less requests of this manager are limited by
        /// <see cref="SessionlessInvocationOptions.MaxConcurrentRequests"/> and
        /// <see cref="SessionlessInvocationOptions.MaxConcurrentRequestsPerChannel"/>,
        /// including those a <see cref="ValidateSessionLessRequest"/> handler
        /// decides. Setting it starts a new budget.
        /// </remarks>
        public SessionlessInvocationOptions? SessionlessInvocation
        {
            get => Volatile.Read(ref m_sessionlessBudget)?.Options;
            set => Volatile.Write(
                ref m_sessionlessBudget,
                value != null ? new SessionlessRequestBudget(value) : null);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from RoleManager configuration events before
                // tearing down sessions to avoid late-stage callbacks racing
                // with disposal.
                IRoleManager? subscribed = Interlocked.Exchange(ref m_subscribedRoleManager, null);
                subscribed?.RoleConfigurationChanged -= OnRoleConfigurationChanged;

                CloseAllSessions();

                m_shutdownEvent.Set();
                m_shutdownEvent.Dispose();
                m_semaphoreSlim.Dispose();
                m_workerCts?.Cancel();
                m_workerCts?.Dispose();
                m_workerCts = null;
            }
        }

        /// <summary>
        /// Starts the session manager.
        /// </summary>
        public virtual async ValueTask StartupAsync(CancellationToken cancellationToken = default)
        {
            await m_semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                lock (m_bindingsLock)
                {
                    m_stopping = false;
                }

                // start thread to monitor sessions.
                m_shutdownEvent.Reset();

                // Recreated on every startup: a token source cannot be reset once
                // ShutdownAsync has cancelled it, and the manager supports restart.
                m_workerCts?.Dispose();
                m_workerCts = new CancellationTokenSource();

                m_monitorWorkerTask = StartSessionMonitor(m_workerCts.Token);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Starts the session monitor loop and returns a task that completes when the
        /// loop has actually exited.
        /// </summary>
        /// <remarks>
        /// The inner <c>AsTask</c> plus <c>Unwrap</c> matter: <see cref="Task.Factory"/>
        /// hands back a task that completes as soon as the loop first yields, so
        /// awaiting the raw <see cref="Task.Factory"/> result would only await the
        /// scheduling of the loop and let shutdown race ahead of it.
        /// </remarks>
        private Task StartSessionMonitor(CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(
                    static state =>
                    {
                        (SessionManager manager, CancellationToken ct) =
                            ((SessionManager, CancellationToken))state!;
                        return manager
                            .MonitorSessionsAsync(manager.m_minSessionTimeout, ct)
                            .AsTask();
                    },
                    (this, cancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default)
                .Unwrap();
        }

        /// <summary>
        /// Stops the session manager and closes all sessions, waiting for the session
        /// monitor loop to exit before returning.
        /// </summary>
        public virtual async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            // stop the monitoring loop.
            m_shutdownEvent.Set();

            // Cancel so the monitor's inter-cycle delay is abandoned immediately
            // instead of running to the end of its sleep cycle.
            m_workerCts?.Cancel();

            Task? monitorWorkerTask = m_monitorWorkerTask;
            if (monitorWorkerTask is not null)
            {
                await monitorWorkerTask.ConfigureAwait(false);
                m_monitorWorkerTask = null;
            }

            m_workerCts?.Dispose();
            m_workerCts = null;

            // Sessions still open at shutdown are closed like any other session: the Closing
            // event is raised, the SessionDiagnostics node is removed and the session count
            // is decremented, rather than the sessions only being disposed.
            foreach (ISession session in DetachAllSessions())
            {
                // The monitor has stopped, so no timeout can claim the session any more. A
                // session a client close or a timeout claimed before is audited by that close;
                // every other session is terminated by the server, which is audited once per
                // session (OPC 10000-5 6.4.7) through the same close claim.
                bool terminated = SessionTermination.TryClaimClose(session);
                try
                {
                    RaiseSessionEvent(session, SessionEventReason.Closing);
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    m_logger.FailedToCloseSessionAtShutdown(e, session.Id);
                }
                finally
                {
                    session.Dispose();
                    m_server.UpdateServerDiagnostics(diagnostics => diagnostics.CurrentSessionCount--);
                    if (terminated)
                    {
                        m_server.ReportAuditCloseSessionEvent(null!, session, m_logger, "Session/Terminated");
                    }
                }
            }
        }

        /// <summary>
        /// Disposes every tracked session and empties the session table.
        /// </summary>
        private void CloseAllSessions()
        {
            foreach (ISession session in DetachAllSessions())
            {
                session.Dispose();
            }
        }

        /// <summary>
        /// Stops admitting sessions and empties the session table.
        /// </summary>
        /// <returns>The sessions that were tracked.</returns>
        private List<ISession> DetachAllSessions()
        {
            var sessions = new List<ISession>();
            lock (m_bindingsLock)
            {
                m_stopping = true;
                foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in m_sessions)
                {
                    if (sessionKeyValue.Value != null)
                    {
                        sessions.Add(sessionKeyValue.Value);
                    }
                }
                m_sessions.Clear();
                m_channelSessionCounts.Clear();
            }
            return sessions;
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async ValueTask<CreateSessionResult> CreateSessionAsync(
            OperationContext context,
            Certificate serverCertificate,
            string? sessionName,
            ByteString clientNonce,
            ApplicationDescription? clientDescription,
            string? endpointUrl,
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken cancellationToken = default)
        {
            NodeId sessionId = default;
            NodeId authenticationToken;
            ByteString serverNonce;
            double revisedSessionTimeout = requestedSessionTimeout;

            ISession session;
            Nonce? tempNonce = null;
            Nonce? serverNonceObject = null;
            bool reserved = false;

            // A request that is rejected for a duplicate clientNonce must not close
            // another Session to make room for itself, so the nonce is checked before
            // the cap eviction. The check under the lock below stays authoritative
            // for concurrent creations.
            ThrowIfClientNonceInUse(context, clientNonce);

            // Part 4 5.7.2.1: at the cap, the oldest Session that was never
            // activated is closed to make room, so Sessions that are created
            // and abandoned cannot lock legitimate Clients out.
            await EvictNonActivatedSessionsAtCapAsync(cancellationToken).ConfigureAwait(false);

            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // check session count.
                if (m_maxSessionCount > 0 && m_sessions.Count >= m_maxSessionCount)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }

                // check for same Nonce in another session.
                ThrowIfClientNonceInUse(context, clientNonce);

                // always assign a hard-to-guess id. A secure channel id does not
                // make a sequential token safe: HTTPS (and the HTTPS hosted
                // WebSocket profiles) share one SecureChannelId between every
                // client of a listener, so the token is the only secret that
                // binds a request to its session (Part 6 7.4.1, Part 4 7.35).
                byte[] token = Nonce.CreateRandomNonceData(32);
                authenticationToken = new NodeId(token.ToByteString());

                // CreateSession is reached only after a secure channel is bound.
                SecureChannelContext channelContext = context.ChannelContext!;

                // determine session timeout. Every comparison with NaN is false,
                // so NaN is revised explicitly instead of being returned as the
                // revisedSessionTimeout (Part 4 5.7.2.2).
                if (double.IsNaN(requestedSessionTimeout))
                {
                    revisedSessionTimeout = m_minSessionTimeout;
                }
                else if (requestedSessionTimeout > m_maxSessionTimeout)
                {
                    revisedSessionTimeout = m_maxSessionTimeout;
                }

                if (requestedSessionTimeout < m_minSessionTimeout)
                {
                    revisedSessionTimeout = m_minSessionTimeout;
                }

                // create server nonce.
                tempNonce = Nonce.CreateNonce(kSessionNonceLength);
                serverNonceObject = tempNonce;

                // assign client name (Part 4 5.7.2.2). The session id is only
                // assigned by InitializeAsync, so a separate counter keeps the
                // names (and the diagnostics BrowseNames) distinct. It is not
                // derived from the authentication token, which must stay secret.
                if (string.IsNullOrEmpty(sessionName))
                {
                    sessionName = Utils.Format(
                        "Session {0}",
                        Utils.IncrementIdentifier(ref m_lastSessionNameId));
                }

                // create instance of session.
                session = CreateSession(
                    context,
                    m_server,
                    serverCertificate,
                    authenticationToken,
                    clientNonce,
                    serverNonceObject,
                    sessionName!,
                    clientDescription!,
                    endpointUrl!,
                    clientCertificate!,
                    clientCertificateChain!,
                    revisedSessionTimeout,
                    maxResponseMessageSize,
                    m_maxRequestAge,
                    m_maxBrowseContinuationPoints);
                tempNonce = null; // ownership transferred to session

                // A client certificate whose validation error was accepted establishes
                // no trusted application identity. Recorded before the session is
                // published, so a concurrent request, role evaluation or a derived
                // manager mirroring the session never reads it as validated.
                if (context.ClientCertificateErrorAccepted)
                {
                    ClientCertificateProvenance.SetValidated(session, false);
                }

                // Part 5 12.11: MaxResponseMessageSize is a mandatory field of
                // SessionDiagnosticsDataType and reports the CreateSession request
                // value. Set before InitializeAsync publishes the diagnostics node.
                session.UpdateDiagnostics(d => d.MaxResponseMessageSize = maxResponseMessageSize);

                m_sessionActivationStates.Add(
                    session,
                    new SessionActivationState(
                        channelContext.ClientChannelCertificate.ToByteString(),
                        channelContext.EndpointDescription!.SecurityPolicyUri ??
                        SecurityPolicies.None,
                        channelContext.EndpointDescription.SecurityMode));

                // Reserve the session slot while holding the lock so the session
                // count cap and client-nonce uniqueness stay enforced atomically.
                // The expensive asynchronous part (registering the session
                // diagnostics node) runs after the lock is released, so it no
                // longer serializes every concurrent CreateSession/ActivateSession
                // behind the session-manager lock. m_sessions is a concurrent
                // dictionary; the lock is only needed for the check-then-add
                // atomicity above.
                bool stopping;
                lock (m_bindingsLock)
                {
                    stopping = m_stopping;
                    if (!stopping)
                    {
                        if (!m_sessions.TryAdd(authenticationToken, session))
                        {
                            throw new ServiceResultException(StatusCodes.BadTooManySessions);
                        }
                        reserved = true;
                    }
                }
                if (stopping)
                {
                    serverNonceObject.Dispose();
                    session.Dispose();
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
            }
            finally
            {
                tempNonce?.Dispose();
                m_semaphoreSlim.Release();
            }

            // complete the asynchronous part of session creation
            // (registers the session diagnostics node and sets Id) outside the
            // global session-manager lock.
            try
            {
                await session.InitializeAsync(context, cancellationToken)
                    .ConfigureAwait(false);
                if (!m_sessions.TryGetValue(authenticationToken, out ISession? current) ||
                    !ReferenceEquals(current, session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }
            }
            catch
            {
                if (reserved)
                {
                    lock (m_bindingsLock)
                    {
                        if (m_sessions.TryGetValue(authenticationToken, out ISession? current) &&
                            ReferenceEquals(current, session))
                        {
                            m_sessions.TryRemove(authenticationToken, out _);
                            if (m_sessionActivationStates.TryGetValue(session, out SessionActivationState? state))
                            {
                                RemoveSessionBinding(state);
                            }
                        }
                    }
                }
                serverNonceObject.Dispose();
                session.Dispose();
                throw;
            }

            // get the session id.
            sessionId = session.Id;
            serverNonce = serverNonceObject.Data.ToByteString();

            // raise session related event.
            RaiseSessionEvent(session, SessionEventReason.Created);

            // return session.
            return new CreateSessionResult
            {
                Session = session,
                SessionId = sessionId,
                AuthenticationToken = authenticationToken,
                RevisedSessionTimeout = revisedSessionTimeout,
                ServerNonce = serverNonce
            };
        }

        /// <summary>
        /// Rejects a clientNonce that another Session already uses with
        /// Bad_NonceInvalid (Part 4 5.7.2.2). A None channel does not require a
        /// random nonce, so a client reusing one there is not rejected.
        /// </summary>
        private void ThrowIfClientNonceInUse(OperationContext context, ByteString clientNonce)
        {
            if (clientNonce.IsEmpty ||
                context.ChannelContext?.EndpointDescription?.SecurityMode == MessageSecurityMode.None)
            {
                return;
            }

            // iterate over key/value pairs in the dictionary with a thread safe iterator
            foreach (KeyValuePair<NodeId, ISession> sessionKeyValueIterator in m_sessions)
            {
                ByteString sessionClientNonce =
                    sessionKeyValueIterator.Value?.ClientNonce ?? default;
                if (sessionClientNonce == clientNonce)
                {
                    throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                }
            }
        }

        /// <summary>
        /// Activates an existing session
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async ValueTask<(bool IdentityContextChanged, ByteString ServerNonce, ServiceResult ActivationStatus)> ActivateSessionAsync(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData? clientSignature,
            ExtensionObject userIdentityToken,
            SignatureData? userTokenSignature,
            ArrayOf<string> localeIds,
            CancellationToken cancellationToken = default)
        {
            ISession? session = null;
            ISession? restoredSession = null;
            ISession? admittedRestore = null;
            bool activated = false;
            IUserIdentityTokenHandler? newIdentity = null;
            string? clientKey = null;
            SemaphoreSlim? activationLock = null;
            ByteString serverNonce = default;
            ServiceResult activationStatus = ServiceResult.Good;
            string? clientUserId = null;
            UserTokenType clientUserTokenType = UserTokenType.Anonymous;
            long activationSequence = 0;
            bool contextChanged = false;

            // fast path no lock
            if (!m_sessions.TryGetValue(authenticationToken, out _) && !SupportsSessionRestore)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            PendingRestore? pendingRestore = null;
            if (!m_sessions.TryGetValue(authenticationToken, out _) && SupportsSessionRestore)
            {
                (pendingRestore, restoredSession) = await JoinRestoreAsync(
                    authenticationToken,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }

            Nonce? serverNonceObject = null;
            try
            {
                bool sessionExpired;
                // The global lock guards the session-manager dictionary and
                // session lifecycle (lookup, lockout, expiry). It is deliberately
                // released before the client-signature verification below:
                // ValidateBeforeActivate only touches this session's own state
                // (guarded by the session's own lock), so running the CPU-bound
                // RSA verify under the global lock would serialize every
                // concurrent ActivateSession and cap connect throughput.
                await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // find session.
                    if (!m_sessions.TryGetValue(authenticationToken, out session))
                    {
                        if (restoredSession == null)
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                        }

                        // Restore completed outside the global lock. Concurrent
                        // activations of the same token share one restored session
                        // (JoinRestoreAsync); the first to get here admits it.
                        lock (m_bindingsLock)
                        {
                            if (m_stopping)
                            {
                                throw new ServiceResultException(StatusCodes.BadServerHalted);
                            }

                            // A restore that was admitted once and is gone again was
                            // discarded or closed meanwhile and must not come back.
                            if (pendingRestore!.Admitted ||
                                !m_sessions.TryAdd(authenticationToken, restoredSession))
                            {
                                if (!m_sessions.TryGetValue(authenticationToken, out session))
                                {
                                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                                }
                            }
                            else
                            {
                                pendingRestore.Admitted = true;
                                RemovePendingRestoreLocked(authenticationToken, pendingRestore);
                            }
                        }

                        if (session == null)
                        {
                            // This activation admitted the restored session: it is
                            // discarded again if no activation commits it.
                            session = restoredSession;
                            admittedRestore = restoredSession;
                            m_server.UpdateServerDiagnostics(diagnostics => diagnostics.CurrentSessionCount++);
                        }
                    }

                    // get client lockout key.
                    clientKey = GetClientLockoutKey(session, context.ChannelContext);

                    // check if client is locked out due to too many failed authentication attempts.
                    if (IsClientLockedOut(clientKey, out long remainingLockoutTicks))
                    {
                        long remainingSeconds = remainingLockoutTicks / m_timeProvider.TimestampFrequency;
                        m_logger.ClientClientKeyIsLockedOutRemainingLockout(clientKey, remainingSeconds);
                        throw new ServiceResultException(
                            StatusCodes.BadUserAccessDenied,
                            $"Too many failed authentication attempts. Try again in {remainingSeconds} seconds.");
                    }

                    sessionExpired = session.HasExpired;
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }

                if (sessionExpired)
                {
                    // Close re-enters this manager, so it must run outside the global gate.
                    // The shared close claim also prevents duplicate audit and diagnostic updates.
                    await CloseTimedOutSessionAsync(session).ConfigureAwait(false);
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }

                if (!m_sessionActivationStates.TryGetValue(
                        session,
                        out SessionActivationState? activationState))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The Session transfer security state is unavailable.");
                }
                await activationState.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                activationLock = activationState.Lock;

                try
                {
                    // Admit the activation under the lock the cap eviction claims its
                    // victim under: either the eviction sees this activation in flight
                    // and skips the session, or the activation sees the claim and fails.
                    lock (m_bindingsLock)
                    {
                        if (activationState.CapEvictionClaimed)
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionClosed);
                        }
                        activationState.ActivationInFlight = true;
                    }

                    if (!m_sessions.TryGetValue(authenticationToken, out ISession? currentSession) ||
                        !ReferenceEquals(currentSession, session) ||
                        SessionTermination.IsClosingOrClaimed(session))
                    {
                        // A timeout or server termination may already be tearing the
                        // session down; it then waits for this lock only to remove it.
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    }

                    SecureChannelContext channelContext = context.ChannelContext!;
                    bool isNewChannel = activationState.RequiresNewChannelChecks ||
                        (session.Activated &&
                            !session.IsSecureChannelValid(channelContext.SecureChannelId));

                    EndpointDescription currentEndpoint =
                        channelContext.EndpointDescription!;
                    if (!string.Equals(
                            activationState.SecurityPolicyUri,
                            currentEndpoint.SecurityPolicyUri,
                            StringComparison.Ordinal) ||
                        activationState.SecurityMode != currentEndpoint.SecurityMode)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected);
                    }
                    if (!session.Activated && !session.IsSecureChannelValid(channelContext.SecureChannelId))
                    {
                        throw new ServiceResultException(StatusCodes.BadSecureChannelIdInvalid);
                    }

                    bool requiresClientCertificate =
                        currentEndpoint.SecurityMode != MessageSecurityMode.None ||
                        !string.Equals(
                            currentEndpoint.SecurityPolicyUri,
                            SecurityPolicies.None,
                            StringComparison.Ordinal);
                    if (isNewChannel &&
                        ((requiresClientCertificate &&
                            activationState.OriginalClientChannelCertificate.IsEmpty) ||
                            activationState.OriginalClientChannelCertificate !=
                                channelContext.ClientChannelCertificate.ToByteString()))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "The SecureChannel client certificate does not match the original channel.");
                    }

                    UserTokenPolicy? userTokenPolicy;
                    // Note: session lookup, lockout and expiry failures above are not
                    // authentication failures and deliberately do NOT record a
                    // brute-force attempt - only a failed client-signature or user
                    // identity validation below does, so a timed-out or unknown session
                    // cannot lock out a legitimate client.

                    try
                    {
                        // Session nonces are application nonces, independent of
                        // SecureChannel policy nonce or ephemeral-key sizes.
                        serverNonceObject = Nonce.CreateNonce(kSessionNonceLength);

                        (newIdentity, userTokenPolicy) = await session
                            .ValidateBeforeActivateAsync(
                                context,
                                clientSignature!,
                                userIdentityToken,
                                userTokenSignature!,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (isNewChannel &&
                            currentEndpoint.SecurityMode ==
                                MessageSecurityMode.Sign &&
                            newIdentity is AnonymousIdentityTokenHandler)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadIdentityChangeNotSupported,
                                "Anonymous sessions cannot move to a Sign-only SecureChannel.");
                        }

                        serverNonce = serverNonceObject.Data.ToByteString();
                    }
                    catch (ServiceResultException)
                    {
                        RecordFailedAuthentication(clientKey!);
                        throw;
                    }

                    IUserIdentity? identity = null;
                    IUserIdentity? effectiveIdentity = null;
                    ServiceResult? error = null;
                    UserIdentity? tempIdentity = null;

                    try
                    {
                        (
                            identity,
                            effectiveIdentity,
                            error) = await AuthenticateUserIdentityAsync(
                                session,
                                newIdentity!,
                                userTokenPolicy,
                                currentEndpoint,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (ServiceResult.IsBad(error))
                        {
                            throw new ServiceResultException(error!);
                        }

                        // parse the token manually if the identity is not provided.
                        if (identity == null)
                        {
                            if (newIdentity == null ||
                                newIdentity.TokenType != UserTokenType.Anonymous)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadIdentityTokenRejected);
                            }

                            tempIdentity = new UserIdentity(newIdentity);
                            identity = tempIdentity;
                        }

                        // use the identity as the effectiveIdentity if not provided.
                        effectiveIdentity ??= identity;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        RecordFailedAuthentication(clientKey);

                        if (e is not ServiceResultException)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadIdentityTokenInvalid,
                                e,
                                "Could not validate user identity token: {0}",
                                newIdentity!);
                        }
                        throw;
                    }

                    // Compare the continuity key rather than the diagnostic
                    // ClientUserId so neither a different issuer/subject split nor
                    // a different token type carrying the same identifier can be
                    // mistaken for the original owner (OPC 10000-4 5.7.3.1).
                    clientUserId = ClientUserIdResolver.ResolveContinuityKey(
                        newIdentity!,
                        identity);
                    clientUserTokenType = newIdentity!.TokenType;
                    if (isNewChannel &&
                        (!activationState.HasClientUserId ||
                            !string.Equals(
                                activationState.ClientUserId,
                                clientUserId,
                                StringComparison.Ordinal)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadIdentityChangeNotSupported,
                            "The user identity does not match the identity associated with the Session.");
                    }

                    // Clear failed authentication attempts on successful activation,
                    // but only for non-anonymous identities. An anonymous login must not
                    // reset the lockout counter that was accumulated from failed
                    // username/certificate attempts — otherwise an attacker can reset the
                    // counter by interleaving anonymous logins between password guesses.
                    if (newIdentity is not (null or AnonymousIdentityTokenHandler))
                    {
                        ClearFailedAuthentication(clientKey);
                    }

                    // Remember what the identity was mapped to, so live role re-evaluation
                    // rebuilds from the same starting point as this activation. It is only
                    // recorded once the activation is about to commit, so a failed attempt
                    // leaves the mapping of the still-active identity in place.
                    var impersonated = new ImpersonatedIdentity(identity, effectiveIdentity);

                    // Add mandatory roles based on session/channel security context (e.g., TrustedApplication).
                    effectiveIdentity = AddMandatoryRoles(session, context, effectiveIdentity);

                    // Per Part 18 §5.2.8: when the authenticated user has the
                    // MustChangePassword bit set on UserManagement, the activation
                    // response shall carry Good_PasswordChangeRequired so the
                    // client knows to prompt for a new password. The role
                    // restriction is enforced separately by AddMandatoryRoles.
                    activationStatus = ComputeActivationStatus(effectiveIdentity);

                    // Re-check after the (possibly slow) authentication: a close that started
                    // meanwhile has already abandoned the session's subscriptions, so
                    // reporting a successful activation would hand the client a session that
                    // is removed as soon as this lock is released (OPC 10000-4 5.7.2.1).
                    if (SessionTermination.IsClosingOrClaimed(session))
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    }


                    lock (m_bindingsLock)
                    {
                        activationState.IsCommitting = true;
                    }

                    // Set before Activate: a re-evaluation racing with it then works on the
                    // previous generation, which Activate supersedes.
                    ImpersonatedIdentity? previousImpersonated = activationState.Impersonated;
                    activationState.Impersonated = impersonated;
                    try
                    {
                        contextChanged = session.Activate(
                            context,
                            newIdentity!,
                            identity,
                            effectiveIdentity,
                            localeIds,
                            serverNonceObject);
                    }
                    catch
                    {
                        activationState.Impersonated = previousImpersonated;
                        lock (m_bindingsLock)
                        {
                            activationState.IsCommitting = false;
                        }
                        throw;
                    }
                    serverNonceObject = null; // ownership transferred to session
                    tempIdentity = null; // ownership transferred to session

                    // The first activation of a restored session consumes the restore
                    // (e.g. a mirrored single-use nonce) only once it passed every check
                    // and the session was activated locally, so a failed or abandoned
                    // attempt leaves it for another one. Only the binding commit, which
                    // fails only for a session that is being closed, follows. Until it,
                    // the session is unbound and is discarded when the activation fails.
                    if (activationState.RestorePending)
                    {
                        bool admitted = false;
                        try
                        {
                            admitted = await AdmitRestoredSessionAsync(
                                authenticationToken,
                                session,
                                context,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (!admitted)
                            {
                                lock (m_bindingsLock)
                                {
                                    activationState.IsCommitting = false;
                                }
                            }
                        }
                        if (!admitted)
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                        }
                        activationState.RestorePending = false;
                    }

                    activationState.ClientUserId = clientUserId;
                    activationState.ClientUserTokenType = clientUserTokenType;
                    activationState.HasClientUserId = true;
                    activationState.RequiresNewChannelChecks = false;

                    // Stamp the activation while the gate is still held so a
                    // listener that persists activation state can discard a write
                    // that a newer concurrent activation has already superseded.
                    activationSequence = ++activationState.ActivationSequence;
                    CommitSessionBinding(authenticationToken, session, activationState);
                    activated = true;
                }
                finally
                {
                    lock (m_bindingsLock)
                    {
                        activationState.ActivationInFlight = false;
                    }
                    activationLock.Release();
                    activationLock = null;
                }

                // The session already holds serverNonce and is bound to this channel;
                // a fault now would hide the nonce from the client and desync it
                // (Part 4 5.7.3.1), so a failing callback is only logged. The work
                // can no longer change the outcome, so the request token (TimeoutHint,
                // Cancel) does not apply: a cancelled mirror write would leave a
                // persisted activation state older than the one the client now uses.
                try
                {
                    await OnSessionActivatedAsync(
                        authenticationToken,
                        session,
                        serverNonce,
                        clientUserTokenType,
                        clientUserId,
                        activationSequence,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    m_logger.SessionActivatedCallbackFailed(e, session.Id);
                }

                // External callbacks run after the activation transaction has
                // committed and released its per-Session gate.
                if (contextChanged)
                {
                    RaiseSessionEvent(session, SessionEventReason.Activated);
                }

                return (contextChanged, serverNonce, activationStatus);
            }
            finally
            {
                serverNonceObject?.Dispose();
                activationLock?.Release();

                // A restored session that no activation admitted already registered
                // its diagnostics node, which must not outlive it; the last activation
                // sharing the restore releases it.
                if (pendingRestore != null &&
                    LeaveRestore(authenticationToken, pendingRestore) &&
                    restoredSession != null)
                {
                    await CloseUnadmittedRestoredSessionAsync(restoredSession).ConfigureAwait(false);
                }

                // A restored session no activation committed is removed again, so it
                // neither holds a session slot nor times out into a close that a
                // distributed manager would treat as the end of the mirrored session.
                if (admittedRestore != null && !activated)
                {
                    await DiscardRestoredSessionAsync(authenticationToken, admittedRestore).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Restores a session for an unknown token, sharing the restore with every
        /// concurrent activation of the same token. The replica materializes one copy of
        /// the session, so a retry racing the first attempt neither competes with it for
        /// the identity of the session (for example its SessionId) nor fails because the
        /// first attempt consumed the restore.
        /// </summary>
        private async ValueTask<(PendingRestore Pending, ISession? Session)> JoinRestoreAsync(
            NodeId authenticationToken,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            PendingRestore? pending;
            bool start = false;
            lock (m_bindingsLock)
            {
                if (!m_pendingRestores.TryGetValue(authenticationToken, out pending))
                {
                    pending = new PendingRestore();
                    m_pendingRestores.Add(authenticationToken, pending);
                    start = true;
                }
                pending.Users++;
            }

            if (start)
            {
                try
                {
                    pending.Completion.TrySetResult(await RestoreSessionAsync(
                        authenticationToken,
                        context,
                        cancellationToken).ConfigureAwait(false));
                }
                catch (Exception e)
                {
                    pending.Completion.TrySetException(e);
                }
            }

            try
            {
                return (pending, await pending.Completion.Task.ConfigureAwait(false));
            }
            catch
            {
                LeaveRestore(authenticationToken, pending);
                throw;
            }
        }

        /// <summary>
        /// Leaves a shared restore.
        /// </summary>
        /// <returns>
        /// <c>true</c> when this was the last activation sharing it and no activation
        /// admitted the restored session, which the caller then releases.
        /// </returns>
        private bool LeaveRestore(NodeId authenticationToken, PendingRestore pending)
        {
            lock (m_bindingsLock)
            {
                pending.Users--;
                if (pending.Users > 0)
                {
                    return false;
                }
                RemovePendingRestoreLocked(authenticationToken, pending);
                return !pending.Admitted;
            }
        }

        private void RemovePendingRestoreLocked(NodeId authenticationToken, PendingRestore pending)
        {
            if (m_pendingRestores.TryGetValue(authenticationToken, out PendingRestore? current) &&
                ReferenceEquals(current, pending))
            {
                m_pendingRestores.Remove(authenticationToken);
            }
        }

        /// <summary>
        /// A restore shared by the concurrent activations of one authentication token;
        /// guarded by the bindings lock.
        /// </summary>
        private sealed class PendingRestore
        {
            public TaskCompletionSource<ISession?> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int Users { get; set; }

            public bool Admitted { get; set; }
        }

        /// <summary>
        /// Releases a restored Session that was never admitted to the session table.
        /// </summary>
        private async ValueTask CloseUnadmittedRestoredSessionAsync(ISession session)
        {
            try
            {
                if (!session.Id.IsNull)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                m_logger.FailedToDiscardRestoredSession(e, session.Id);
            }
            finally
            {
                session.Dispose();
            }
        }

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <remarks>
        /// This method should not throw an exception if the session no longer exists.
        /// </remarks>
        public virtual async ValueTask CloseSessionAsync(NodeId sessionId, CancellationToken cancellationToken = default)
        {
            ISession? session = null;
            NodeId authenticationToken = NodeId.Null;

            // thread safe search for the session.
            foreach (KeyValuePair<NodeId, ISession> current in m_sessions)
            {
                if (current.Value.Id == sessionId)
                {
                    authenticationToken = current.Key;
                    session = current.Value;
                    break;
                }
            }

            // close the session if found.
            if (session != null)
            {
                SemaphoreSlim activationLock = m_sessionActivationStates.GetValue(
                    session,
                    _ => new SessionActivationState(
                        default,
                        SecurityPolicies.None,
                        MessageSecurityMode.None)).Lock;
                await activationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                bool removed = false;
                try
                {
                    await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        lock (m_bindingsLock)
                        {
                            if (!m_sessions.TryGetValue(
                                    authenticationToken,
                                    out ISession? currentSession) ||
                                !ReferenceEquals(currentSession, session) ||
                                !m_sessions.TryRemove(authenticationToken, out _))
                            {
                                return;
                            }
                            removed = true;
                            if (m_sessionActivationStates.TryGetValue(session, out SessionActivationState? state))
                            {
                                RemoveSessionBinding(state);
                            }
                        }
                    }
                    finally
                    {
                        m_semaphoreSlim.Release();
                    }

                    // raise session related event.
                    RaiseSessionEvent(session, SessionEventReason.Closing);

                    // close the session. The session is already removed, so the teardown
                    // must finish: a cancelled close would leave its SessionDiagnostics
                    // node and array entry behind for a session that no longer exists.
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (removed)
                        {
                            session.Dispose();

                            // update diagnostics.
                            m_server.UpdateServerDiagnostics(diagnostics => diagnostics.CurrentSessionCount--);
                        }
                    }
                    finally
                    {
                        activationLock.Release();
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool HasSession(string secureChannelId)
        {
            if (secureChannelId == null)
            {
                throw new ArgumentNullException(nameof(secureChannelId));
            }
            lock (m_bindingsLock)
            {
                return m_channelSessionCounts.ContainsKey(secureChannelId);
            }
        }

        /// <inheritdoc/>
        public bool TryGetSessionContext(
            NodeId authenticationToken,
            SecureChannelContext channelContext,
            [NotNullWhen(true)] out SessionBindingContext? context)
        {
            if (channelContext == null)
            {
                throw new ArgumentNullException(nameof(channelContext));
            }
            context = null;
            ISession session;
            SessionActivationState state;
            SessionBindingContext binding;
            ByteString originalClientChannelCertificate;
            lock (m_bindingsLock)
            {
                if (authenticationToken.IsNull ||
                    !m_sessions.TryGetValue(authenticationToken, out ISession? currentSession) ||
                    !m_sessionActivationStates.TryGetValue(currentSession, out SessionActivationState? currentState) ||
                    currentState.IsCommitting ||
                    currentState.BindingContext is not SessionBindingContext currentBinding)
                {
                    return false;
                }
                session = currentSession;
                state = currentState;
                binding = currentBinding;
                originalClientChannelCertificate = state.OriginalClientChannelCertificate;
            }

            // Session diagnostics callbacks can query membership while holding a Session lock.
            // Probe Session state without the index lock, then check that the snapshot is still current.
            if (!session.Activated ||
                SessionTermination.IsClosingOrClaimed(session) ||
                session.HasExpired ||
                !string.Equals(binding.SecureChannelId, channelContext.SecureChannelId, StringComparison.Ordinal) ||
                !session.IsSecureChannelValid(channelContext.SecureChannelId) ||
                channelContext.EndpointDescription is not EndpointDescription endpoint ||
                binding.SecurityMode != endpoint.SecurityMode ||
                !string.Equals(binding.SecurityPolicyUri, endpoint.SecurityPolicyUri, StringComparison.Ordinal) ||
                (binding.SecurityPolicyUri != SecurityPolicies.None &&
                    originalClientChannelCertificate != channelContext.ClientChannelCertificate.ToByteString()))
            {
                return false;
            }

            lock (m_bindingsLock)
            {
                if (state.IsCommitting ||
                    !ReferenceEquals(state.BindingContext, binding) ||
                    !m_sessions.TryGetValue(authenticationToken, out ISession? currentSession) ||
                    !ReferenceEquals(currentSession, session))
                {
                    return false;
                }
                context = binding;
                return true;
            }
        }

        private void CommitSessionBinding(
            NodeId authenticationToken,
            ISession session,
            SessionActivationState state)
        {
            lock (m_bindingsLock)
            {
                state.IsCommitting = false;
                // Shutdown can remove the session while authentication is awaiting a provider,
                // and a timeout or termination can start closing it.
                if (!m_sessions.TryGetValue(authenticationToken, out ISession? current) ||
                    !ReferenceEquals(current, session) ||
                    SessionTermination.IsClosingOrClaimed(session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }

                var binding = new SessionBindingContext(
                    session.Id,
                    session.SecureChannelId,
                    state.ActivationSequence,
                    state.ClientUserTokenType,
                    state.ClientUserId,
                    state.SecurityPolicyUri,
                    state.SecurityMode);
                if (state.BindingContext?.SecureChannelId != binding.SecureChannelId)
                {
                    RemoveSessionBinding(state);
                    m_channelSessionCounts.TryGetValue(binding.SecureChannelId, out int count);
                    m_channelSessionCounts[binding.SecureChannelId] = count + 1;
                }
                state.BindingContext = binding;
            }
        }

        private void RemoveSessionBinding(SessionActivationState state)
        {
            if (state.BindingContext is not SessionBindingContext binding)
            {
                return;
            }
            if (m_channelSessionCounts.TryGetValue(binding.SecureChannelId, out int count))
            {
                if (count == 1)
                {
                    m_channelSessionCounts.Remove(binding.SecureChannelId);
                }
                else
                {
                    m_channelSessionCounts[binding.SecureChannelId] = count - 1;
                }
            }
            state.BindingContext = null;
        }

        /// <summary>
        /// Supplies the original transfer security state for a Session restored
        /// by <see cref="RestoreSessionAsync"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected void SetRestoredSessionTransferSecurityState(
            ISession session,
            ByteString originalClientChannelCertificate,
            string securityPolicyUri,
            MessageSecurityMode securityMode,
            UserTokenType clientUserTokenType,
            string? clientUserId)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new ArgumentException(
                    "A security policy URI is required.",
                    nameof(securityPolicyUri));
            }
            if (clientUserTokenType is < UserTokenType.Anonymous or
                > UserTokenType.IssuedToken)
            {
                throw new ArgumentOutOfRangeException(nameof(clientUserTokenType));
            }
            if (clientUserTokenType == UserTokenType.Anonymous !=
                (clientUserId == null))
            {
                throw new ArgumentException(
                    "Anonymous ClientUserIds must be null and non-anonymous ClientUserIds must be non-null.",
                    nameof(clientUserId));
            }

            SessionActivationState state = m_sessionActivationStates.GetValue(
                session,
                _ => new SessionActivationState(
                    originalClientChannelCertificate,
                    securityPolicyUri,
                    securityMode));
            state.OriginalClientChannelCertificate = originalClientChannelCertificate;
            state.SecurityPolicyUri = securityPolicyUri;
            state.SecurityMode = securityMode;
            state.ClientUserTokenType = clientUserTokenType;
            state.ClientUserId = clientUserId;
            state.HasClientUserId = true;
            state.RequiresNewChannelChecks = true;
            state.RestorePending = true;
        }

        /// <summary>
        /// Called once for a Session materialized by <see cref="RestoreSessionAsync"/>,
        /// after its first <c>ActivateSession</c> passed the client signature, user
        /// identity and continuity checks and the session was activated locally, immediately
        /// before the activation is bound and committed.
        /// </summary>
        /// <remarks>
        /// A distributed manager consumes its single-use restore state here (for example
        /// the mirrored server nonce), so an activation that fails validation leaves that
        /// state intact for another attempt or another replica. Returning <c>false</c>
        /// rejects the activation with <see cref="StatusCodes.BadSessionIdInvalid"/>. When
        /// the first activation of a restored Session fails for any reason, the Session is
        /// discarded from this manager without going through
        /// <see cref="CloseSessionAsync"/>, so mirrored state is not deleted. Only the binding,
        /// which fails only for a session that is being closed, commits after this call returns
        /// <c>true</c>, so the request's cancellation is not passed: a restore must not be
        /// consumed and then abandoned. Work that must only happen for a committed activation
        /// belongs in <see cref="OnSessionActivatedAsync"/>.
        /// </remarks>
        /// <param name="authenticationToken">The Session authentication token.</param>
        /// <param name="session">The restored Session.</param>
        /// <param name="context">The operation context of the activation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><c>true</c> to commit the activation; <c>false</c> to reject it.</returns>
        protected virtual ValueTask<bool> AdmitRestoredSessionAsync(
            NodeId authenticationToken,
            ISession session,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<bool>(true);
        }

        /// <summary>
        /// Removes a restored Session whose first activation failed, without the
        /// virtual <see cref="CloseSessionAsync"/> and therefore without deleting any
        /// mirrored state. Its diagnostics node is removed and the session disposed.
        /// </summary>
        private async ValueTask DiscardRestoredSessionAsync(NodeId authenticationToken, ISession session)
        {
            // Under the activation gate, so a concurrent activation of the same restored
            // session either committed it already or fails once it is removed.
            if (!m_sessionActivationStates.TryGetValue(session, out SessionActivationState? state))
            {
                return;
            }
            await state.Lock.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (m_bindingsLock)
                {
                    // A restored session that an activation committed is bound; one whose
                    // activation failed after Session.Activate (or after its admission) is not.
                    if (state.BindingContext != null ||
                        !m_sessions.TryGetValue(authenticationToken, out ISession? current) ||
                        !ReferenceEquals(current, session) ||
                        !SessionTermination.TryClaimClose(session) ||
                        !m_sessions.TryRemove(authenticationToken, out _))
                    {
                        return;
                    }
                    RemoveSessionBinding(state);
                }
            }
            finally
            {
                state.Lock.Release();
            }

            try
            {
                await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_logger.FailedToDiscardRestoredSession(e, session.Id);
            }
            finally
            {
                session.Dispose();
                m_server.UpdateServerDiagnostics(diagnostics => diagnostics.CurrentSessionCount--);
            }
        }

        /// <summary>
        /// Called after activation state has committed and the per-Session
        /// activation gate has been released.
        /// </summary>
        /// <param name="authenticationToken">The Session authentication token.</param>
        /// <param name="session">The activated Session.</param>
        /// <param name="serverNonce">The nonce issued by this activation.</param>
        /// <param name="clientUserTokenType">The activated user token type.</param>
        /// <param name="clientUserId">
        /// The identity continuity key of the activated owner, or <c>null</c> when
        /// the Session is anonymous.
        /// </param>
        /// <param name="activationSequence">
        /// A per-Session sequence number that increases with every successful
        /// activation. Implementations that persist activation state outside the
        /// activation gate use it to discard writes that a newer concurrent
        /// activation has already superseded.
        /// </param>
        /// <param name="cancellationToken">
        /// The cancellation token. The activation has already committed and the
        /// callback cannot fail it (a failure is only logged), so the request's
        /// cancellation is not passed; implementations bound their own work.
        /// </param>
        protected virtual ValueTask OnSessionActivatedAsync(
            NodeId authenticationToken,
            ISession session,
            ByteString serverNonce,
            UserTokenType clientUserTokenType,
            string? clientUserId,
            long activationSequence,
            CancellationToken cancellationToken)
        {
            return default;
        }

        /// <summary>
        /// Validates request header and returns a request context.
        /// </summary>
        /// <remarks>
        /// This method verifies that the session id is valid and that it uses secure channel id
        /// associated with current thread. It also verifies that the timestamp is not too
        /// and that the sequence number is not out of order (update requests only).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="requestHeader"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async ValueTask<OperationContext> ValidateRequestAsync(
            RequestHeader? requestHeader,
            SecureChannelContext secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ISession? session = null;

            try
            {
                // check for create session request.
                if (requestType is RequestType.CreateSession or RequestType.ActivateSession)
                {
                    return new OperationContext(requestHeader, secureChannelContext, requestType, requestLifetime);
                }

                // find session.
                if (!m_sessions.TryGetValue(requestHeader.AuthenticationToken, out session))
                {
                    // Session-less invocation (OPC 10000-4 §6.3) is limited to
                    // the View (without RegisterNodes/UnregisterNodes),
                    // Attribute, Method, NodeManagement and Query Service Sets;
                    // everything else needs a Session.
                    if (!IsSessionlessService(requestType))
                    {
                        throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                    }

                    return await CreateSessionlessContextAsync(
                            requestHeader,
                            secureChannelContext,
                            requestType,
                            requestLifetime)
                        .ConfigureAwait(false);
                }

                // validate request header.
                session!.ValidateRequest(requestHeader, secureChannelContext, requestType);

                // A Session rejects requests itself once it is closing; a custom ISession
                // whose close was only claimed by the server cannot, so it is checked here.
                if (SessionTermination.IsClosingOrClaimed(session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }

                // Lazily reconcile the RoleManager subscription. The
                // RoleManager is bound during server startup, after
                // SessionManager construction, so we
                // can't subscribe at startup; the first request that flows
                // through a fully-initialized server wires it up.
                EnsureRoleManagerSubscription();

                // Part 18 §4.4.1 — if a Role configuration change marked the
                // session's identity stale, re-evaluate the mandatory roles
                // (Anonymous/AuthenticatedUser/TrustedApplication + live
                // RoleManager identity-mapping rules) before the request runs
                // so that downstream access checks see the current grants.
                ReevaluateIdentityIfStale(session, secureChannelContext);

                // validate user has permissions for additional info. Decided after the
                // re-evaluation so the privilege reflects the roles the request runs with.
                session.ValidateDiagnosticInfo(requestHeader);

                // return context.
                return new OperationContext(requestHeader, secureChannelContext, requestType, requestLifetime, session);
            }
            catch (ServiceResultException sre)
            {
                if ((sre.StatusCode == StatusCodes.BadSessionClosed ||
                        sre.StatusCode == StatusCodes.BadSessionNotActivated) &&
                    session != null &&
                    !SessionTermination.IsClosingOrClaimed(session) &&
                    session.HasExpired)
                {
                    // The request found the session timed out before the session monitor
                    // did: terminate it now (OPC 10000-4 5.7.2.1), as ActivateSession does.
                    // A never-activated session that has also expired is a timeout too.
                    // The shared close claim keeps the count and audit single-shot.
                    await CloseTimedOutSessionAsync(session).ConfigureAwait(false);
                }
                else if (sre.StatusCode == StatusCodes.BadSessionNotActivated && session != null)
                {
                    // The server terminates the session because of the client's error, so
                    // it goes through the regular close path and is counted and audited as
                    // an abort. Not cancellable by the rejected request: a close cut short
                    // by the request's timeout or transport cancellation must not leave the
                    // session half torn down.
                    await m_server.TerminateSessionAsync(
                        session.Id,
                        deleteSubscriptions: false,
                        m_logger,
                        CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
            catch (OperationCanceledException)
            {
                // The request was cancelled or timed out, for example while a
                // Session-less Access Token was validated: the endpoint maps it
                // to the status of the request lifetime.
                throw;
            }
            catch (Exception e)
            {
                throw ServiceResultException.Unexpected(e, e.Message);
            }
        }

        /// <summary>
        /// Creates the context of a request that names no Session: it runs
        /// within the Session-less request budget, as the identity the
        /// <see cref="ValidateSessionLessRequest"/> handler or the
        /// <see cref="SessionlessInvocation"/> options decide.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The request carries no acceptable identity, or the handler or the options refuse it.
        /// </exception>
        /// <exception cref="ServerBusyException">
        /// The Session-less request budget has no place for the request.
        /// </exception>
        private async ValueTask<OperationContext> CreateSessionlessContextAsync(
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime)
        {
            EventHandler<ValidateSessionLessRequestEventArgs>? handler = m_ValidateSessionLessRequest;
            SessionlessRequestBudget? budget = Volatile.Read(ref m_sessionlessBudget);
            SessionlessInvocationOptions? sessionless = budget?.Options;

            if (handler == null && sessionless == null)
            {
                // No authenticationToken at all is a Session-less call; a
                // Server without support answers Bad_ServiceUnsupported
                // (§6.3.1). A token that names no Session stays
                // Bad_SessionIdInvalid, which is what a Client whose
                // Session expired needs to see.
                if (requestHeader.AuthenticationToken.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadServiceUnsupported,
                        "Session-less Service invocation is not enabled on this Server.");
                }
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }

            // The request has no Session to be accounted for: take its place in
            // the Session-less budget before any identity is checked, which
            // bounds the validations that run at the same time as well. The
            // context returns the lease when the request is done, and every
            // other way out of here does.
            IDisposable? lease = null;
            if (budget != null)
            {
                SessionlessLimit limit = budget.TryAcquire(secureChannelContext, out lease);
                if (limit != SessionlessLimit.None)
                {
                    m_logger.SessionlessRequestRefused(limit, requestType);
                    throw new ServerBusyException(
                        new ServiceResult(
                            StatusCodes.BadServerTooBusy,
                            new LocalizedText(limit == SessionlessLimit.Server
                                ? "The Server runs the maximum number of Session-less requests."
                                : "The channel runs the maximum number of Session-less requests.")),
                        retryAfter: null);
                }
            }

            try
            {
                OperationContext context;
                if (handler != null)
                {
                    var args = new ValidateSessionLessRequestEventArgs(
                        requestHeader.AuthenticationToken,
                        requestType);
                    handler(this, args);

                    if (ServiceResult.IsBad(args.Error))
                    {
                        throw new ServiceResultException(args.Error);
                    }

                    context = new OperationContext(
                        requestHeader,
                        secureChannelContext,
                        requestType,
                        requestLifetime,
                        MapSessionlessRoles(args.Identity, secureChannelContext));
                }
                else
                {
                    IUserIdentity identity = await ValidateSessionlessRequestAsync(
                            requestHeader.AuthenticationToken,
                            secureChannelContext,
                            sessionless!,
                            requestLifetime.CancellationToken)
                        .ConfigureAwait(false);
                    context = new OperationContext(
                        requestHeader,
                        secureChannelContext,
                        requestType,
                        requestLifetime,
                        MapSessionlessRoles(identity, secureChannelContext));
                }

                if (lease != null)
                {
                    context.AttachSessionlessLease(lease);
                    lease = null;
                }
                return context;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        /// <summary>
        /// Maps the roles of the identity of a Session-less request the way
        /// ActivateSession maps those of a Session, so that role-mapped Nodes
        /// are accessed under the same rules. Without a Session there is no
        /// client application certificate, so no TrustedApplication role and
        /// no application-based role mapping.
        /// </summary>
        private IUserIdentity MapSessionlessRoles(
            IUserIdentity? identity,
            SecureChannelContext secureChannelContext)
        {
            if (identity == null)
            {
                return null!;
            }
            return ResolveRoles(identity, null, secureChannelContext.EndpointDescription);
        }

        /// <summary>
        /// Returns whether a Service may be invoked without a Session
        /// (OPC 10000-4 §6.3.1): the View (without RegisterNodes and
        /// UnregisterNodes), Attribute, Method, NodeManagement and Query
        /// Service Sets.
        /// </summary>
        /// <param name="requestType">The Service.</param>
        /// <returns>
        /// <see langword="true"/> if the Service may be invoked without a
        /// Session; otherwise <see langword="false"/>.
        /// </returns>
        public static bool IsSessionlessService(RequestType requestType)
        {
            return requestType is
                RequestType.Browse or
                RequestType.BrowseNext or
                RequestType.TranslateBrowsePathsToNodeIds or
                RequestType.Read or
                RequestType.HistoryRead or
                RequestType.Write or
                RequestType.HistoryUpdate or
                RequestType.Call or
                RequestType.AddNodes or
                RequestType.AddReferences or
                RequestType.DeleteNodes or
                RequestType.DeleteReferences or
                RequestType.QueryFirst or
                RequestType.QueryNext;
        }

        /// <summary>
        /// Determines the identity a Session-less request runs as, per
        /// <paramref name="options"/>.
        /// </summary>
        /// <param name="authenticationToken">
        /// The <c>authenticationToken</c> of the request header.
        /// </param>
        /// <param name="secureChannelContext">The channel the request arrived on.</param>
        /// <param name="options">The Session-less invocation options.</param>
        /// <param name="cancellationToken">Cancels the validation.</param>
        /// <returns>The identity the request runs as.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="secureChannelContext"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The request carries no acceptable identity.
        /// </exception>
        protected virtual async ValueTask<IUserIdentity> ValidateSessionlessRequestAsync(
            NodeId authenticationToken,
            SecureChannelContext secureChannelContext,
            SessionlessInvocationOptions options,
            CancellationToken cancellationToken)
        {
            if (secureChannelContext == null)
            {
                throw new ArgumentNullException(nameof(secureChannelContext));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            EndpointDescription? endpoint = secureChannelContext.EndpointDescription;

            if (!authenticationToken.IsNull)
            {
                // An Access Token (§6.3.1). Session tokens are UInt32 or
                // ByteString NodeIds; a String NodeId carries the token itself.
                if (!options.AcceptAccessTokens ||
                    !authenticationToken.TryGetValue(out string accessToken) ||
                    string.IsNullOrEmpty(accessToken))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }

                // "The SecureChannel shall have encryption enabled to prevent
                // eavesdroppers from seeing the Access Token." HTTPS encrypts
                // at the transport.
                if (endpoint == null || !IsConfidential(endpoint))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityModeInsufficient,
                        "An Access Token requires an encrypted SecureChannel.");
                }

                var tokenHandler = new IssuedIdentityTokenHandler(
                    Profiles.JwtUserToken,
                    System.Text.Encoding.UTF8.GetBytes(accessToken));
                var policy = new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    IssuedTokenType = Profiles.JwtUserToken
                };
                AuthenticationResult result = await m_server.IdentityRegistry
                    .AuthenticateAsync(
                        new AuthenticationContext(tokenHandler, policy, endpoint, m_server.MessageContext),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.Outcome == AuthenticationOutcome.Accepted && result.Identity != null)
                {
                    return result.Identity;
                }

                m_logger.SessionlessAccessTokenRejected(result.Outcome);
                throw new ServiceResultException(
                    result.Error ?? new ServiceResult(StatusCodes.BadIdentityTokenRejected));
            }

            if (options.AllowAnonymous)
            {
                ValidateAnonymousSessionlessChannel(endpoint, secureChannelContext);
                return new UserIdentity();
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenInvalid,
                "The Session-less request carries no Access Token.");
        }

        /// <summary>
        /// Checks that a Session-less request without an Access Token may run
        /// as an anonymous user on the channel it arrived on.
        /// </summary>
        /// <remarks>
        /// "If application authentication through the SecureChannel is
        /// sufficient, Servers may not require the Access Token and assume an
        /// anonymous user" (OPC 10000-4 §6.3.1). The request therefore has to
        /// meet what the endpoint demands of CreateSession and ActivateSession:
        /// the client application certificate of the channel where the
        /// endpoint uses security, or HTTPS with mutual TLS, and an anonymous
        /// user token policy.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// The channel does not authenticate the application, or the endpoint
        /// offers no anonymous user token policy.
        /// </exception>
        private void ValidateAnonymousSessionlessChannel(
            EndpointDescription? endpoint,
            SecureChannelContext secureChannelContext)
        {
            if (endpoint == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityModeInsufficient,
                    "The endpoint of the Session-less request is not known.");
            }

            // An application certificate is required as ActivateSession requires it
            // for a new channel; HTTPS with mutual TLS asks for it on every request.
            bool requiresClientCertificate =
                endpoint.SecurityMode != MessageSecurityMode.None ||
                !string.Equals(endpoint.SecurityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal) ||
                (m_httpsMutualTls &&
                    endpoint.EndpointUrl != null &&
                    Utils.IsUriHttpsScheme(endpoint.EndpointUrl));
            if (requiresClientCertificate &&
                (secureChannelContext.ClientChannelCertificate == null ||
                    secureChannelContext.ClientChannelCertificate.Length == 0))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "The Session-less request carries no Access Token and its channel has no client certificate.");
            }

            // Anonymous only where an anonymous Session would be possible: the
            // check of ActivateSession for an anonymous user identity token.
            if (!endpoint.UserIdentityTokens.IsEmpty && !OffersAnonymousUserTokenPolicy(endpoint))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenRejected,
                    "Anonymous user token policy not supported.");
            }
        }

        /// <summary>
        /// Returns whether <paramref name="endpoint"/> lists an anonymous user token policy.
        /// </summary>
        private static bool OffersAnonymousUserTokenPolicy(EndpointDescription endpoint)
        {
            for (int ii = 0; ii < endpoint.UserIdentityTokens.Count; ii++)
            {
                if (endpoint.UserIdentityTokens[ii].TokenType == UserTokenType.Anonymous)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether the channel of <paramref name="endpoint"/> keeps
        /// an Access Token confidential: SignAndEncrypt, or an HTTPS
        /// endpoint, whose transport encrypts.
        /// </summary>
        private static bool IsConfidential(EndpointDescription endpoint)
        {
            return endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt ||
                (endpoint.EndpointUrl != null && Utils.IsUriHttpsScheme(endpoint.EndpointUrl));
        }

        /// <summary>
        /// Validates an inbound user token through the identity registry first, then the legacy event.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="session"/>, <paramref name="newIdentity"/>, or
        /// <paramref name="endpointDescription"/> is <c>null</c>.
        /// </exception>
        protected virtual async ValueTask<(
            IUserIdentity? Identity,
            IUserIdentity? EffectiveIdentity,
            ServiceResult? Error)> AuthenticateUserIdentityAsync(
                ISession session,
                IUserIdentityTokenHandler newIdentity,
                UserTokenPolicy? userTokenPolicy,
                EndpointDescription endpointDescription,
                CancellationToken cancellationToken)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (newIdentity == null)
            {
                throw new ArgumentNullException(nameof(newIdentity));
            }
            if (endpointDescription == null)
            {
                throw new ArgumentNullException(nameof(endpointDescription));
            }

            UserTokenPolicy policy = userTokenPolicy ??
                new UserTokenPolicy
                {
                    TokenType = newIdentity.TokenType
                };

            Certificate? channelCert = null;
            try
            {
                string? channelAppUri = null;
                try
                {
                    Certificate? rawCert = session.ClientCertificate;
                    if (rawCert != null)
                    {
                        channelCert = Certificate.FromRawData(rawCert.RawData);
                    }
                    channelAppUri = session.ClientApplicationUri;
                }
                catch (Exception ex)
                {
                    m_logger.FailedToPopulateChannelContextForAuthentication(ex);
                }

                var authCtx = new AuthenticationContext(
                    newIdentity,
                    policy,
                    endpointDescription,
                    m_server.MessageContext,
                    channelCert,
                    channelAppUri);

                AuthenticationResult authResult = await m_server.IdentityRegistry
                    .AuthenticateAsync(authCtx, cancellationToken)
                    .ConfigureAwait(false);

                if (authResult.Outcome == AuthenticationOutcome.Accepted)
                {
                    return (authResult.Identity, authResult.Identity, null);
                }

                if (authResult.Outcome == AuthenticationOutcome.Rejected)
                {
                    return (
                        null,
                        null,
                        authResult.Error ?? new ServiceResult(StatusCodes.BadIdentityTokenRejected));
                }

                // check if the application has a callback which validates the identity tokens.
                // The callback may be slow (password hashing, directory lookups), so it runs
                // outside m_eventLock, which every session event of every request takes.
                ImpersonateEventHandler? impersonateUser;
                lock (m_eventLock)
                {
                    impersonateUser = m_ImpersonateUser;
                }

                if (impersonateUser != null)
                {
                    var args = new ImpersonateEventArgs(
                        newIdentity,
                        userTokenPolicy,
                        endpointDescription);
                    impersonateUser(session, args);

                    if (ServiceResult.IsBad(args.IdentityValidationError))
                    {
                        return (null, null, args.IdentityValidationError);
                    }

                    return (args.Identity, args.EffectiveIdentity, null);
                }

                return (null, null, null);
            }
            finally
            {
                channelCert?.Dispose();
            }
        }

        /// <summary>
        /// Assigns mandatory roles to the effective identity based on the session's security context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Per OPC UA Part 3 §4.9, the <see cref="Role.TrustedApplication"/> role is always
        /// assigned when a Session has been authenticated with a trusted ApplicationInstance
        /// Certificate and uses at least a signed communication channel.
        /// </para>
        /// <para>
        /// Per OPC UA Part 18 §4.4 the live <see cref="IRoleManager"/> identity-mapping rules
        /// are evaluated and any matching roles are layered on top of the identity supplied
        /// by the ImpersonateUser callback.
        /// </para>
        /// <para>
        /// Per OPC UA Part 18 §5.2.8, when the session authenticates via a USERNAME token and
        /// the user has the <see cref="UserConfigurationMask.MustChangePassword"/> bit set,
        /// the session is restricted to the <see cref="Role.Anonymous"/> role only — the
        /// session can only call <c>ChangePassword</c> until the password is changed.
        /// </para>
        /// </remarks>
        protected virtual IUserIdentity AddMandatoryRoles(
            ISession session,
            OperationContext context,
            IUserIdentity effectiveIdentity)
        {
            // Only a client certificate that passed validation identifies the
            // application. One whose validation error an OnApplicationCertificateError
            // override accepted still signs the session but grants neither
            // TrustedApplication nor application-based role mappings.
            Certificate? applicationCertificate =
                ClientCertificateProvenance.IsValidated(session)
                    ? session.ClientCertificate
                    : null;

            return ResolveRoles(
                effectiveIdentity,
                applicationCertificate,
                context.ChannelContext?.EndpointDescription);
        }

        /// <summary>
        /// Maps the roles of an identity: the restriction of a user that must change
        /// the password, the TrustedApplication role and the live
        /// <see cref="IRoleManager"/> identity-mapping rules. A Session receives them
        /// through <see cref="AddMandatoryRoles"/>, a Session-less request through
        /// <see cref="CreateSessionlessContextAsync"/>, so both resolve the roles alike.
        /// </summary>
        /// <param name="effectiveIdentity">The identity to map the roles of.</param>
        /// <param name="applicationCertificate">
        /// The validated application certificate of the client, or <see langword="null"/>
        /// if the client has none or its certificate did not pass validation.
        /// </param>
        /// <param name="endpoint">The endpoint the request arrived on.</param>
        private IUserIdentity ResolveRoles(
            IUserIdentity effectiveIdentity,
            Certificate? applicationCertificate,
            EndpointDescription? endpoint)
        {
            // Part 18 5.2.8 - the Session "shall have only the Role Anonymous" if
            // the user has MustChangePassword set, so every other role and any
            // role-derived privilege of the impersonated identity is discarded
            // rather than merged. The ChangePassword method is callable by
            // USERNAME sessions regardless of role; once the password is changed
            // the next ActivateSession will see MustChangePassword == false and
            // grant the full role set.
            if (effectiveIdentity.TokenType == UserTokenType.UserName &&
                !string.IsNullOrEmpty(effectiveIdentity.DisplayName) &&
                m_server.UserManagement?.MustChangePassword(effectiveIdentity.DisplayName) == true)
            {
                return RoleBasedIdentity.CreateRestricted(
                    effectiveIdentity,
                    [Role.Anonymous],
                    m_server.NamespaceUris);
            }

            // Assign TrustedApplication role per OPC UA Part 3 §4.9.
            if (applicationCertificate != null &&
                endpoint?.SecurityMode >= MessageSecurityMode.Sign)
            {
                if (effectiveIdentity is RoleBasedIdentity rbi)
                {
                    effectiveIdentity = rbi.WithAdditionalRoles([Role.TrustedApplication], m_server.NamespaceUris);
                }
                else
                {
                    effectiveIdentity = new RoleBasedIdentity(
                        effectiveIdentity,
                        [Role.TrustedApplication],
                        m_server.NamespaceUris);
                }
            }

            // Layer in roles from the live IRoleManager identity-mapping rules
            // (Part 18 §4.4.4). Roles already granted by the ImpersonateUser
            // callback are preserved.
            IRoleManager roleManager = m_server.RoleManager;
            if (roleManager != null)
            {
                IList<NodeId> dynamicRoleIds = roleManager.ResolveGrantedRoles(
                    effectiveIdentity,
                    applicationCertificate,
                    endpoint);

                if (dynamicRoleIds.Count > 0)
                {
                    var dynamicRoles = new List<Role>(dynamicRoleIds.Count);
                    foreach (NodeId roleId in dynamicRoleIds)
                    {
                        dynamicRoles.Add(new Role(roleId, roleId.ToString()));
                    }

                    if (effectiveIdentity is RoleBasedIdentity rbi2)
                    {
                        effectiveIdentity = rbi2.WithAdditionalRoles(dynamicRoles, m_server.NamespaceUris);
                    }
                    else
                    {
                        effectiveIdentity = new RoleBasedIdentity(
                            effectiveIdentity,
                            dynamicRoles,
                            m_server.NamespaceUris);
                    }
                }
            }

            return effectiveIdentity;
        }

        /// <summary>
        /// Computes the <see cref="ServiceResult"/> that the ActivateSession
        /// response shall carry.
        /// </summary>
        /// <remarks>
        /// Per OPC UA Part 18 §5.2.8, when the session authenticates via a
        /// USERNAME token and the user has the
        /// <see cref="UserConfigurationMask.MustChangePassword"/> bit set, the
        /// activation must return <c>Good_PasswordChangeRequired</c>. All other
        /// activations return <c>Good</c>.
        /// </remarks>
        /// <param name="effectiveIdentity">
        /// The identity after impersonation and role resolution. Must not be
        /// <see langword="null"/>.
        /// </param>
        protected virtual ServiceResult ComputeActivationStatus(IUserIdentity effectiveIdentity)
        {
            if (effectiveIdentity != null &&
                effectiveIdentity.TokenType == UserTokenType.UserName &&
                !string.IsNullOrEmpty(effectiveIdentity.DisplayName) &&
                m_server.UserManagement?.MustChangePassword(effectiveIdentity.DisplayName) == true)
            {
                return new ServiceResult(StatusCodes.GoodPasswordChangeRequired);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Re-evaluates the session's effective identity if it has been
        /// marked stale by a Role configuration change.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Implements OPC UA Part 18 §4.4.1 "live re-evaluation": when an
        /// <see cref="IRoleManager"/> identity-mapping rule changes, sessions
        /// receive the new role grants on the next request without needing
        /// the client to re-activate. The re-evaluation re-runs
        /// <see cref="AddMandatoryRoles"/> from the effective identity the
        /// last activation mapped <see cref="ISession.Identity"/> to (falling
        /// back to <see cref="ISession.Identity"/> itself), so the outcome is
        /// deterministic and idempotent.
        /// </para>
        /// <para>
        /// The identity and generation are captured before the role computation
        /// and committed conditionally, so a concurrent activation or role
        /// change cannot be overwritten by an older refresh.
        /// </para>
        /// </remarks>
        protected virtual void ReevaluateIdentityIfStale(
            ISession session,
            SecureChannelContext secureChannelContext)
        {
            if (session == null || !session.IsIdentityStale)
            {
                return;
            }

            try
            {
                IdentityRefreshSnapshot snapshot = session.CaptureIdentityRefreshSnapshot();
                if (!session.IsIdentityStale)
                {
                    return;
                }

                // Build a minimal OperationContext to satisfy the
                // AddMandatoryRoles signature; only ChannelContext is
                // consulted (for the endpoint).
                using var refreshContext = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext,
                    RequestType.Unknown,
                    RequestLifetime.None,
                    snapshot.Identity);

                // Start from the effective identity the activation mapped this identity to
                // (e.g. by an ImpersonateUser callback), not from the raw client identity.
                IUserIdentity baseIdentity = snapshot.Identity;
                if (m_sessionActivationStates.TryGetValue(session, out SessionActivationState? state) &&
                    state.Impersonated is ImpersonatedIdentity impersonated &&
                    ReferenceEquals(impersonated.Identity, snapshot.Identity))
                {
                    baseIdentity = impersonated.EffectiveIdentity;
                }

                IUserIdentity refreshed = AddMandatoryRoles(
                    session,
                    refreshContext,
                    baseIdentity);

                _ = session.TryRefreshEffectiveIdentity(
                    snapshot.Identity,
                    snapshot.Generation,
                    refreshed);
            }
            catch (Exception ex)
            {
                m_logger.FailedToReEvaluateSessionSessionIdIdentity(ex, session.Id);
            }
        }

        /// <summary>
        /// Marks every active session's effective identity stale in response
        /// to a Role configuration change (Part 18 §4.4.1).
        /// </summary>
        /// <remarks>
        /// The actual re-evaluation happens lazily on the next request via
        /// <see cref="ReevaluateIdentityIfStale"/> so that the event handler
        /// stays cheap and contention-free.
        /// </remarks>
        protected virtual void OnRoleConfigurationChanged(object? sender, RoleConfigurationChangedEventArgs e)
        {
            try
            {
                // GetSessions() is virtual to allow tests to inject sentinel
                // sessions; production walks the SessionManager's own table.
                foreach (ISession session in GetSessions())
                {
                    session?.MarkIdentityStale();
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToMarkSessionsStaleAfterRole(ex);
            }
        }

        /// <summary>
        /// Ensures the SessionManager is subscribed to the current
        /// <see cref="IRoleManager"/>'s <see cref="IRoleManager.RoleConfigurationChanged"/>
        /// event.
        /// </summary>
        /// <remarks>
        /// The RoleManager is bound during server startup, which can happen after
        /// SessionManager construction. The subscription is reconciled lazily on each
        /// request so the wiring works regardless of binding order.
        /// </remarks>
        private void EnsureRoleManagerSubscription()
        {
            IRoleManager? current = m_server.RoleManager;
            IRoleManager? previous = m_subscribedRoleManager;
            if (current == previous)
            {
                return;
            }

            // CAS to claim the swap; on failure another thread already
            // reconciled — defer to it.
            if (Interlocked.CompareExchange(ref m_subscribedRoleManager, current, previous) != previous)
            {
                return;
            }

            previous?.RoleConfigurationChanged -= OnRoleConfigurationChanged;
            current?.RoleConfigurationChanged += OnRoleConfigurationChanged;
        }

        /// <summary>
        /// Gets a value indicating whether this manager can restore sessions
        /// that are not present in the local session table (e.g. a mirrored
        /// session after a failover). When <c>false</c> (the default), an
        /// <c>ActivateSession</c> for an unknown token fails fast with
        /// <see cref="StatusCodes.BadSessionIdInvalid"/> exactly as before.
        /// </summary>
        protected virtual bool SupportsSessionRestore => false;

        /// <summary>
        /// Restores a session that is not present in the local session table.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="ActivateSessionAsync"/> when the supplied
        /// <paramref name="authenticationToken"/> is unknown locally. The
        /// default returns <c>null</c> so the activation is rejected. A
        /// distributed manager overrides this to reconstruct a mirrored session
        /// (e.g. from a shared store), after which the normal activation path
        /// performs the full client-certificate signature validation — the
        /// token is never an authenticator on its own. The returned session
        /// must be fully initialized (its diagnostics node registered, i.e.
        /// <see cref="ISession.InitializeAsync"/> awaited).
        /// </remarks>
        /// <param name="authenticationToken">The unknown authentication token.</param>
        /// <param name="context">The operation context of the activation.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The restored session to admit to the local table, or <c>null</c> to
        /// reject the activation.
        /// </returns>
        protected virtual ValueTask<ISession?> RestoreSessionAsync(
            NodeId authenticationToken,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ISession?>((ISession?)null);
        }

        /// <summary>
        /// Creates a new instance of a session.
        /// </summary>
        protected virtual ISession CreateSession(
            OperationContext context,
            IServerInternal server,
            Certificate serverCertificate,
            NodeId sessionCookie,
            ByteString clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            Certificate clientCertificate,
            CertificateCollection clientCertificateChain,
            double sessionTimeout,
            uint maxResponseMessageSize,
            int maxRequestAge, // TBD - Remove unused parameter.
            int maxContinuationPoints) // TBD - Remove unused parameter.
        {
            return new Session(
                context,
                m_server,
                serverCertificate,
                sessionCookie,
                clientNonce,
                serverNonce,
                sessionName,
                clientDescription,
                endpointUrl,
                clientCertificate,
                clientCertificateChain,
                sessionTimeout,
                m_maxBrowseContinuationPoints,
                m_maxHistoryContinuationPoints,
                m_timeProvider);
        }

        /// <inheritdoc />
        public virtual void RaiseSessionDiagnosticsChangedEvent(ISession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            RaiseSessionEvent(session, SessionEventReason.DiagnosticsChanged);
        }

        /// <summary>
        /// Raises an event related to a session.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected virtual void RaiseSessionEvent(ISession session, SessionEventReason reason)
        {
            lock (m_eventLock)
            {
                SessionEventHandler? handler = null;

                switch (reason)
                {
                    case SessionEventReason.Created:
                        handler = m_SessionCreated;
                        break;
                    case SessionEventReason.Activated:
                        handler = m_SessionActivated;
                        break;
                    case SessionEventReason.Closing:
                        handler = m_SessionClosing;
                        break;
                    case SessionEventReason.DiagnosticsChanged:
                        handler = m_SessionDiagnosticsChanged;
                        break;
                    case SessionEventReason.ChannelKeepAlive:
                        handler = m_SessionChannelKeepAlive;
                        break;
                    case SessionEventReason.Impersonating:
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected SessionEventReason {reason}");
                }

                if (handler != null)
                {
                    try
                    {
                        handler(session, reason);
                    }
                    catch (Exception e)
                    {
                        m_logger.SessionEventHandlerRaisedAnException(e);
                    }
                }
            }
        }

        /// <summary>
        /// Closes, counts and audits a session whose timeout has elapsed. Only the caller
        /// that claims the close does so; ActivateSession, request validation and the
        /// session monitor can all observe the same expiry, and a client close, a
        /// termination or a cap eviction may already be closing the session.
        /// </summary>
        private async ValueTask CloseTimedOutSessionAsync(ISession session)
        {
            if (session.IsClosing || !SessionTermination.TryClaimClose(session))
            {
                return;
            }

            try
            {
                // Deliberately not cancellable: a close already under way must finish so the
                // session is torn down cleanly even when shutdown has cancelled the monitor loop.
                await m_server.CloseClaimedSessionAsync(session, false, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                // update diagnostics; the session is removed even when a part of its
                // teardown fails.
                m_server.UpdateServerDiagnostics(diagnostics => diagnostics.SessionTimeoutCount++);

                // raise audit event for session closed because of timeout
                m_server.ReportAuditCloseSessionEvent(null!, session, m_logger, "Session/Timeout");
            }
        }

        /// <summary>
        /// Closes the oldest Sessions that were never activated while the Session
        /// table is at its cap (Part 4 5.7.2.1). A cap filled with activated
        /// Sessions is left alone and CreateSession fails with Bad_TooManySessions.
        /// </summary>
        /// <remarks>
        /// Runs before the session-manager lock is taken, because closing a Session
        /// takes its activation lock first. A Session whose ActivateSession is in
        /// flight is not a victim: it may be about to become activated, and closing it
        /// would wait for its activation (and authentication) to finish. Each victim is
        /// claimed under the bindings lock, so an activation that has not committed yet
        /// fails in CommitSessionBinding and concurrent creators never close the same
        /// Session twice; the check is repeated a bounded number of times.
        /// </remarks>
        private async ValueTask EvictNonActivatedSessionsAtCapAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0;
                attempt < kMaxCapEvictionAttempts &&
                    m_maxSessionCount > 0 &&
                    m_sessions.Count >= m_maxSessionCount;
                attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ISession? victim = null;
                DateTimeUtc victimConnectionTime = DateTimeUtc.MaxValue;
                long victimSequence = long.MaxValue;
                foreach (KeyValuePair<NodeId, ISession> entry in m_sessions)
                {
                    ISession candidate = entry.Value;
                    if (candidate == null ||
                        candidate.Id.IsNull ||
                        !IsCapEvictionCandidate(candidate) ||
                        !m_sessionActivationStates.TryGetValue(candidate, out SessionActivationState? candidateState))
                    {
                        continue;
                    }

                    // The oldest by connection time; sessions created within one clock tick
                    // are ordered by creation, not by the table's enumeration order.
                    DateTimeUtc connectionTime = candidate.ReadDiagnostics(d => d.ClientConnectionTime);
                    if (victim == null ||
                        connectionTime < victimConnectionTime ||
                        (connectionTime == victimConnectionTime &&
                            candidateState.CreationSequence < victimSequence))
                    {
                        victim = candidate;
                        victimConnectionTime = connectionTime;
                        victimSequence = candidateState.CreationSequence;
                    }
                }

                if (victim == null)
                {
                    return;
                }

                // Re-checked and claimed under the lock CommitSessionBinding re-checks
                // IsClosing under, without taking a Session lock while it is held.
                // An activation registers itself in flight under the same lock after
                // it acquired its gate, and fails once the eviction claim is recorded.
                lock (m_bindingsLock)
                {
                    if (!IsCapEvictionCandidate(victim) ||
                        !m_sessionActivationStates.TryGetValue(victim, out SessionActivationState? victimState) ||
                        !SessionTermination.TryClaimClose(victim))
                    {
                        continue;
                    }
                    victimState.CapEvictionClaimed = true;
                }

                m_logger.ClosingNonActivatedSessionAtCap(victim.Id, m_maxSessionCount);

                // Not cancellable for the same reason as a timed-out close: a close
                // that started must finish so the slot is really released. Counted and
                // audited once, as a termination by the server.
                await m_server.TerminateClaimedSessionAsync(
                    victim,
                    deleteSubscriptions: true,
                    m_logger,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Whether the session may be closed to make room at the session cap: it was never
        /// activated, is not closing and has no ActivateSession in flight. Takes no Session
        /// lock, so it can be called under the bindings lock; the in-flight flag is only
        /// authoritative there.
        /// </summary>
        private bool IsCapEvictionCandidate(ISession session)
        {
            return !session.Activated &&
                !SessionTermination.IsClosingOrClaimed(session) &&
                m_sessionActivationStates.TryGetValue(session, out SessionActivationState? state) &&
                !state.IsCommitting &&
                !state.ActivationInFlight &&
                !state.CapEvictionClaimed &&
                state.Lock.CurrentCount != 0;
        }

        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private async ValueTask MonitorSessionsAsync(
            int sleepCycle,
            CancellationToken cancellationToken = default)
        {
            try
            {
                m_logger.ServerSessionMonitorThreadStarted();

                while (true)
                {
                    // enumerator is thread safe
                    foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in m_sessions)
                    {
                        ISession session = sessionKeyValue.Value;
                        try
                        {
                            if (session.HasExpired)
                            {
                                await CloseTimedOutSessionAsync(session).ConfigureAwait(false);
                            }
                            // if a session had no activity for the last m_minSessionTimeout milliseconds, send a keep alive event.
                            else if (m_timeProvider.GetTimestampMilliseconds() - session.LastContactTickCount > m_minSessionTimeout)
                            {
                                // signal the channel that the session is still active.
                                RaiseSessionEvent(session, SessionEventReason.ChannelKeepAlive);
                            }
                        }
                        catch (Exception e) when (e is not OperationCanceledException ||
                            !cancellationToken.IsCancellationRequested)
                        {
                            // one failing session must not stop the monitor: every other
                            // session still has to time out.
                            m_logger.FailedToCloseTimedOutSession(e, session.Id);
                        }
                    }

                    if (m_shutdownEvent.WaitOne(0))
                    {
                        m_logger.ServerSessionMonitorThreadExitedNormally();
                        break;
                    }

                    // Asynchronous so the sleep does not park a thread-pool thread for
                    // the whole cycle, and so shutdown can abandon it immediately.
                    await m_timeProvider
                        .Delay(TimeSpan.FromMilliseconds(sleepCycle), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                m_logger.ServerSessionMonitorThreadExitedNormally();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_logger.ServerSessionMonitorThreadExitedNormally();
            }
            catch (Exception e)
            {
                m_logger.ServerSessionMonitorThreadExitedUnexpectedly(e);
            }
        }

        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly NodeIdDictionary<ISession> m_sessions;
        private readonly Lock m_bindingsLock = new();
        private readonly Dictionary<NodeId, PendingRestore> m_pendingRestores = [];
        private readonly Dictionary<string, int> m_channelSessionCounts = new(StringComparer.Ordinal);
        private bool m_stopping;

        private readonly ConditionalWeakTable<ISession, SessionActivationState>
            m_sessionActivationStates =
#if NET8_0_OR_GREATER
                [];
#else
                new();
#endif

        private uint m_lastSessionNameId;
        private readonly ManualResetEvent m_shutdownEvent;
        private Task? m_monitorWorkerTask;
        private CancellationTokenSource? m_workerCts;

        private readonly int m_minSessionTimeout;
        private readonly int m_maxSessionTimeout;
        private readonly int m_maxSessionCount;
        private readonly bool m_httpsMutualTls;
        private readonly int m_maxRequestAge;

        private readonly int m_maxBrowseContinuationPoints;
        private readonly int m_maxHistoryContinuationPoints;

        private readonly ConcurrentDictionary<string, ClientLockoutInfo> m_clientLockouts = new();
        private readonly int m_maxFailedAuthenticationAttempts;
        private readonly long m_lockoutDurationTicks;
        private readonly long m_failureExpirationTicks;

        private readonly Lock m_eventLock = new();
        private event SessionEventHandler? m_SessionCreated;
        private event SessionEventHandler? m_SessionActivated;
        private event SessionEventHandler? m_SessionClosing;
        private event SessionEventHandler? m_SessionDiagnosticsChanged;
        private event SessionEventHandler? m_SessionChannelKeepAlive;
        private event ImpersonateEventHandler? m_ImpersonateUser;
        private event EventHandler<ValidateSessionLessRequestEventArgs>? m_ValidateSessionLessRequest;
        private SessionlessRequestBudget? m_sessionlessBudget;

        /// <summary>
        /// Last <see cref="IRoleManager"/> we wired
        /// <see cref="OnRoleConfigurationChanged"/> onto. Reconciled in
        /// <see cref="EnsureRoleManagerSubscription"/>.
        /// </summary>
        private IRoleManager? m_subscribedRoleManager;

        /// <summary>
        /// The length of the Session nonces this Server issues in the
        /// CreateSession and ActivateSession responses.
        /// </summary>
        /// <remarks>
        /// OPC 10000-4 5.7.2.2 (Table 15) requires this nonce to be between 32 and
        /// 128 bytes for every SecurityPolicy, so it is deliberately independent of
        /// the SecureChannel policy nonce length. Deriving it from the policy would
        /// emit a 16 byte nonce for the legacy Basic128Rsa15 policy, which is below
        /// the range a conformant Client accepts, and would needlessly generate an
        /// ephemeral key pair for the ECC policies. The ECC ephemeral key is a
        /// separate value carried in the AdditionalParameters ECDHKey entry.
        /// </remarks>
        private const int kSessionNonceLength = 32;
        private const int kMaxCapEvictionAttempts = 3;

        /// <summary>
        /// Pairs an activated identity with the effective identity it was mapped to.
        /// </summary>
        private sealed record ImpersonatedIdentity(IUserIdentity Identity, IUserIdentity EffectiveIdentity);

        private sealed class SessionActivationState
        {
            public SessionActivationState(
                ByteString originalClientChannelCertificate,
                string securityPolicyUri,
                MessageSecurityMode securityMode)
            {
                OriginalClientChannelCertificate = originalClientChannelCertificate;
                SecurityPolicyUri = securityPolicyUri;
                SecurityMode = securityMode;
            }

            public SemaphoreSlim Lock { get; } = new(1, 1);

            /// <summary>
            /// Orders sessions by creation where ClientConnectionTime cannot: on a coarse
            /// clock (about 15 ms on .NET Framework) sessions created back to back share it.
            /// </summary>
            public long CreationSequence { get; } = Interlocked.Increment(ref s_lastCreationSequence);

            public ByteString OriginalClientChannelCertificate { get; set; }

            public string SecurityPolicyUri { get; set; }

            public MessageSecurityMode SecurityMode { get; set; }

            public UserTokenType ClientUserTokenType { get; set; }

            public string? ClientUserId { get; set; }

            public bool HasClientUserId { get; set; }

            public bool RequiresNewChannelChecks { get; set; }

            /// <summary>
            /// The session was materialized by <see cref="RestoreSessionAsync"/> and no
            /// activation has committed it yet; read and cleared under the activation gate.
            /// </summary>
            public bool RestorePending { get; set; }

            public long ActivationSequence { get; set; }

            public SessionBindingContext? BindingContext { get; set; }

            public bool IsCommitting { get; set; }

            /// <summary>
            /// An ActivateSession holds the activation gate and was admitted; set and read
            /// under the bindings lock.
            /// </summary>
            public bool ActivationInFlight { get; set; }

            /// <summary>
            /// The session was claimed as a cap eviction victim; set and read under the
            /// bindings lock. An activation that acquires the gate afterwards fails.
            /// </summary>
            public bool CapEvictionClaimed { get; set; }

            /// <summary>
            /// The effective identity the authenticator (or ImpersonateUser callback) returned
            /// for the activated identity, before the mandatory roles were added. Live role
            /// re-evaluation starts from it so roles granted only to the effective identity
            /// survive a role configuration change.
            /// </summary>
            public ImpersonatedIdentity? Impersonated
            {
                get => Volatile.Read(ref m_impersonated);
                set => Volatile.Write(ref m_impersonated, value);
            }

            private ImpersonatedIdentity? m_impersonated;
            private static long s_lastCreationSequence;
        }

        /// <inheritdoc/>
        public event SessionEventHandler SessionCreated
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SessionCreated += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SessionCreated -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event SessionEventHandler SessionActivated
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SessionActivated += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SessionActivated -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event SessionEventHandler SessionDiagnosticsChanged
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SessionDiagnosticsChanged += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SessionDiagnosticsChanged -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event SessionEventHandler SessionClosing
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SessionClosing += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SessionClosing -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event SessionEventHandler SessionChannelKeepAlive
        {
            add
            {
                lock (m_eventLock)
                {
                    m_SessionChannelKeepAlive += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_SessionChannelKeepAlive -= value;
                }
            }
        }

        /// <inheritdoc/>
        [Obsolete(
            "Replaced by IUserTokenAuthenticator + IServerIdentityRegistry. " +
            "Register authenticators via services.AddIdentityAuthenticator<T>() or " +
            "server.CurrentInstance.IdentityRegistry.Register(...). See docs/IdentityProviders.md.")]
        public event ImpersonateEventHandler ImpersonateUser
        {
            add
            {
                lock (m_eventLock)
                {
                    m_ImpersonateUser += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_ImpersonateUser -= value;
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler<ValidateSessionLessRequestEventArgs> ValidateSessionLessRequest
        {
            add
            {
                lock (m_eventLock)
                {
                    m_ValidateSessionLessRequest += value;
                }
            }
            remove
            {
                lock (m_eventLock)
                {
                    m_ValidateSessionLessRequest -= value;
                }
            }
        }

        /// <inheritdoc/>
        public virtual IList<ISession> GetSessions()
        {
            return [.. m_sessions.Values];
        }

        /// <inheritdoc/>
        public ISession? GetSession(NodeId authenticationToken)
        {
            // find session.
            if (m_sessions.TryGetValue(authenticationToken, out ISession? session))
            {
                return session;
            }
            return null;
        }

        /// <summary>
        /// Gets the lockout key for a client without using client-controlled session metadata.
        /// </summary>
        private static string GetClientLockoutKey(
            ISession session,
            SecureChannelContext? channelContext)
        {
            if (session.ClientCertificate != null)
            {
                return session.ClientCertificate.Thumbprint;
            }

            if (channelContext?.PeerAddress != null)
            {
                return "peer:" + channelContext.PeerAddress;
            }

            // HTTP bindings may share a channel id across the entire listener.
            // Without an observed peer, isolate the fallback to this server-created session.
            return "session:" + session.Id;
        }

        /// <summary>
        /// Checks if a client is currently locked out due to too many failed authentication attempts.
        /// </summary>
        private bool IsClientLockedOut(string clientKey, out long remainingLockoutTicks)
        {
            remainingLockoutTicks = 0;

            if (m_maxFailedAuthenticationAttempts <= 0 || string.IsNullOrEmpty(clientKey))
            {
                // Lockout disabled (MaxFailedAuthenticationAttempts <= 0) or no key.
                return false;
            }

            if (m_clientLockouts.TryGetValue(clientKey, out ClientLockoutInfo? lockoutInfo))
            {
                long currentTicks = m_timeProvider.GetTimestamp();
                if (lockoutInfo.IsLockedOut(currentTicks))
                {
                    remainingLockoutTicks = lockoutInfo.LockoutEndTicks - currentTicks;
                    return true;
                }

                if (lockoutInfo.IsExpired(currentTicks, m_failureExpirationTicks))
                {
                    m_clientLockouts.TryRemove(clientKey, out _);
                }
            }

            return false;
        }

        /// <summary>
        /// Records a failed authentication attempt for a client.
        /// </summary>
        private void RecordFailedAuthentication(string clientKey)
        {
            if (m_maxFailedAuthenticationAttempts <= 0 || string.IsNullOrEmpty(clientKey))
            {
                // Lockout disabled (MaxFailedAuthenticationAttempts <= 0) or no key.
                return;
            }

            long currentTicks = m_timeProvider.GetTimestamp();
            ClientLockoutInfo lockoutInfo = m_clientLockouts.AddOrUpdate(
                clientKey,
                _ => new ClientLockoutInfo(1, currentTicks, m_lockoutDurationTicks, m_maxFailedAuthenticationAttempts),
                (_, existing) => existing.IncrementFailures(
                    currentTicks, m_lockoutDurationTicks, m_failureExpirationTicks, m_maxFailedAuthenticationAttempts));

            if (lockoutInfo.IsLockedOut(currentTicks))
            {
                long remainingSeconds = (lockoutInfo.LockoutEndTicks - currentTicks) / m_timeProvider.TimestampFrequency;
                m_logger.ClientClientKeyHasBeenLockedOutAfter(clientKey, lockoutInfo.FailedAttempts, remainingSeconds);
            }
        }

        /// <summary>
        /// Clears the failed authentication attempts for a client after successful authentication.
        /// </summary>
        private void ClearFailedAuthentication(string clientKey)
        {
            if (!string.IsNullOrEmpty(clientKey))
            {
                m_clientLockouts.TryRemove(clientKey, out _);
            }
        }

        /// <inheritdoc/>
        public void ClearAuthenticationLockouts()
        {
            m_clientLockouts.Clear();
        }

        /// <summary>
        /// Tracks failed authentication attempts and lockout state for a client.
        /// </summary>
        private sealed class ClientLockoutInfo
        {
            public ClientLockoutInfo(
                int failedAttempts,
                long lastFailureTicks,
                long lockoutDurationTicks,
                int maxAttempts)
            {
                FailedAttempts = failedAttempts;
                LastFailureTicks = lastFailureTicks;
                LockoutEndTicks = failedAttempts >= maxAttempts
                    ? lastFailureTicks + lockoutDurationTicks
                    : 0;
            }

            public int FailedAttempts { get; }
            public long LastFailureTicks { get; }
            public long LockoutEndTicks { get; }

            public bool IsLockedOut(long currentTicks)
            {
                return LockoutEndTicks > currentTicks;
            }

            public bool IsExpired(long currentTicks, long expirationTicks)
            {
                return !IsLockedOut(currentTicks) && (currentTicks - LastFailureTicks) > expirationTicks;
            }

            public ClientLockoutInfo IncrementFailures(
                long currentTicks,
                long lockoutDurationTicks,
                long expirationTicks,
                int maxAttempts)
            {
                if (IsLockedOut(currentTicks))
                {
                    return this;
                }

                if (IsExpired(currentTicks, expirationTicks))
                {
                    return new ClientLockoutInfo(1, currentTicks, lockoutDurationTicks, maxAttempts);
                }

                return new ClientLockoutInfo(
                    FailedAttempts + 1,
                    currentTicks,
                    lockoutDurationTicks,
                    maxAttempts);
            }
        }
    }

    /// <summary>
    /// Source-generated log messages for SessionManager.
    /// </summary>
    internal static partial class SessionManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.SessionManager + 0, Level = LogLevel.Warning,
            Message = "Client {ClientKey} is locked out. Remaining lockout time: {RemainingSeconds} seconds.")]
        public static partial void ClientClientKeyIsLockedOutRemainingLockout(
            this ILogger logger,
            string? clientKey,
            long remainingSeconds);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 1, Level = LogLevel.Debug,
            Message = "Failed to populate channel context for authentication.")]
        public static partial void FailedToPopulateChannelContextForAuthentication(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 2, Level = LogLevel.Warning,
            Message = "Failed to re-evaluate session {SessionId} identity after Role configuration change.")]
        public static partial void FailedToReEvaluateSessionSessionIdIdentity(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 3, Level = LogLevel.Warning,
            Message = "Failed to mark sessions stale after Role configuration change.")]
        public static partial void FailedToMarkSessionsStaleAfterRole(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 4, Level = LogLevel.Trace,
            Message = "Session event handler raised an exception.")]
        public static partial void SessionEventHandlerRaisedAnException(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 5, Level = LogLevel.Information,
            Message = "Server - Session Monitor Thread Started.")]
        public static partial void ServerSessionMonitorThreadStarted(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 6, Level = LogLevel.Trace,
            Message = "Server - Session Monitor Thread Exited Normally.")]
        public static partial void ServerSessionMonitorThreadExitedNormally(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 7, Level = LogLevel.Error,
            Message = "Server - Session Monitor Thread Exited Unexpectedly")]
        public static partial void ServerSessionMonitorThreadExitedUnexpectedly(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 8, Level = LogLevel.Warning,
            Message = "Client {ClientKey} has been locked out after {FailedAttempts} failed authentication " +
                "attempts. Lockout expires in {RemainingSeconds} seconds.")]
        public static partial void ClientClientKeyHasBeenLockedOutAfter(
            this ILogger logger,
            string? clientKey,
            int failedAttempts,
            long remainingSeconds);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 9, Level = LogLevel.Error,
            Message = "Server - Session Monitor failed to process session {SessionId}.")]
        public static partial void FailedToCloseTimedOutSession(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 17, Level = LogLevel.Warning,
            Message = "Server - Session activated callback failed for session {SessionId}; the activation stands.")]
        public static partial void SessionActivatedCallbackFailed(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 11, Level = LogLevel.Information,
            Message = "Server - Closing non-activated session {SessionId}: the session limit of " +
                "{MaxSessionCount} is reached.")]
        public static partial void ClosingNonActivatedSessionAtCap(
            this ILogger logger,
            NodeId sessionId,
            int maxSessionCount);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 10, Level = LogLevel.Warning,
            Message = "Server - Failed to close session {SessionId} at shutdown.")]
        public static partial void FailedToCloseSessionAtShutdown(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 18, Level = LogLevel.Warning,
            Message = "Server - Rejected the access token of a session-less request ({Outcome}).")]
        public static partial void SessionlessAccessTokenRejected(
            this ILogger logger,
            AuthenticationOutcome outcome);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 19, Level = LogLevel.Debug,
            Message = "Server - Refused a session-less {RequestType} request, the {Limit} limit is reached.")]
        public static partial void SessionlessRequestRefused(
            this ILogger logger,
            SessionlessLimit limit,
            RequestType requestType);

        [LoggerMessage(EventId = ServerEventIds.SessionManager + 12, Level = LogLevel.Warning,
            Message = "Server - Failed to discard restored session {SessionId} after its activation failed.")]
        public static partial void FailedToDiscardRestoredSession(
            this ILogger logger,
            Exception ex,
            NodeId sessionId);
    }
}
