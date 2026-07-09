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
using System.Globalization;
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
    public class SessionManager : ISessionManager
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
            m_lastSessionId = BitConverter.ToUInt32(Nonce.CreateRandomNonceData(sizeof(uint)), 0);

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);
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

                // create snapshot of all sessions
                KeyValuePair<NodeId, ISession>[] sessions = [.. m_sessions];
                m_sessions.Clear();

                foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in sessions)
                {
                    sessionKeyValue.Value?.Dispose();
                }

                m_shutdownEvent.Set();
                m_shutdownEvent.Dispose();
                m_semaphoreSlim.Dispose();
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
                // start thread to monitor sessions.
                m_shutdownEvent.Reset();

                // TODO: Await the task completion in shutdown and pass cancellation token
                _ = Task.Factory.StartNew(
                    () => MonitorSessionsAsync(m_minSessionTimeout),
                    default,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Stops the session manager and closes all sessions.
        /// </summary>
        public virtual void Shutdown()
        {
            // stop the monitoring thread.
            m_shutdownEvent.Set();

            // dispose of session objects using a snapshot.
            KeyValuePair<NodeId, ISession>[] sessions = [.. m_sessions];
            m_sessions.Clear();

            foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in sessions)
            {
                sessionKeyValue.Value?.Dispose();
            }
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

            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // check session count.
                if (m_maxSessionCount > 0 && m_sessions.Count >= m_maxSessionCount)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }

                // check for same Nonce in another session
                if (!clientNonce.IsEmpty)
                {
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

                // can assign a simple identifier if secured.
                authenticationToken = default;
                // CreateSession is reached only after a secure channel is bound.
                SecureChannelContext channelContext = context.ChannelContext!;
                if (!string.IsNullOrEmpty(channelContext.SecureChannelId) &&
                    channelContext.EndpointDescription!
                        .SecurityMode != MessageSecurityMode.None)
                {
                    authenticationToken = new NodeId(
                        Utils.IncrementIdentifier(ref m_lastSessionId));
                }

                // must assign a hard-to-guess id if not secured.
                if (authenticationToken.IsNull)
                {
                    byte[] token = Nonce.CreateRandomNonceData(32);
                    authenticationToken = new NodeId(token.ToByteString());
                }

                // determine session timeout.
                if (requestedSessionTimeout > m_maxSessionTimeout)
                {
                    revisedSessionTimeout = m_maxSessionTimeout;
                }

                if (requestedSessionTimeout < m_minSessionTimeout)
                {
                    revisedSessionTimeout = m_minSessionTimeout;
                }

                // create server nonce.
                tempNonce = Nonce.CreateNonce(
                    channelContext.EndpointDescription!.SecurityPolicyUri!);
                serverNonceObject = tempNonce;

                // assign client name.
                if (string.IsNullOrEmpty(sessionName))
                {
                    sessionName = Utils.Format("Session {0}", sessionId);
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

                // Reserve the session slot while holding the lock so the session
                // count cap and client-nonce uniqueness stay enforced atomically.
                // The expensive asynchronous part (registering the session
                // diagnostics node) runs after the lock is released, so it no
                // longer serializes every concurrent CreateSession/ActivateSession
                // behind the session-manager lock. m_sessions is a concurrent
                // dictionary; the lock is only needed for the check-then-add
                // atomicity above.
                if (!m_sessions.TryAdd(authenticationToken, session))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }
                reserved = true;
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
            }
            catch
            {
                // roll back the reserved slot; m_sessions is concurrent so this
                // needs no lock.
                if (reserved)
                {
                    m_sessions.TryRemove(authenticationToken, out _);
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
            IUserIdentityTokenHandler? newIdentity = null;
            string? clientKey = null;

            // fast path no lock
            if (!m_sessions.TryGetValue(authenticationToken, out _) && !SupportsSessionRestore)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            if (!m_sessions.TryGetValue(authenticationToken, out _) && SupportsSessionRestore)
            {
                restoredSession = await RestoreSessionAsync(
                    authenticationToken,
                    context,
                    cancellationToken).ConfigureAwait(false);
            }

            Nonce? serverNonceObject = null;
            try
            {
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

                        // Restore completed outside the global lock. Re-check
                        // under the lock in case another concurrent activation
                        // already admitted the same mirrored session.
                        if (!m_sessions.TryAdd(authenticationToken, restoredSession) &&
                            !m_sessions.TryGetValue(authenticationToken, out session))
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                        }

                        session ??= restoredSession;
                        if (!ReferenceEquals(session, restoredSession))
                        {
                            restoredSession.Dispose();
                        }
                        restoredSession = null;
                    }

                    // get client lockout key.
                    clientKey = GetClientLockoutKey(session);

                    // check if client is locked out due to too many failed authentication attempts.
                    if (IsClientLockedOut(clientKey, out long remainingLockoutTicks))
                    {
                        long remainingSeconds = remainingLockoutTicks / m_timeProvider.TimestampFrequency;
                        m_logger.LogWarning(
                            "Client {ClientKey} is locked out. Remaining lockout time: {RemainingSeconds} seconds.",
                            clientKey,
                            remainingSeconds);
                        throw new ServiceResultException(
                            StatusCodes.BadUserAccessDenied,
                            $"Too many failed authentication attempts. Try again in {remainingSeconds} seconds.");
                    }

                    // check if session timeout has expired.
                    if (session.HasExpired)
                    {
                        // raise audit event for session closed because of timeout
                        m_server.ReportAuditCloseSessionEvent(null!, session, m_logger, "Session/Timeout");

                        await m_server.CloseSessionAsync(null!, session.Id, false, default).ConfigureAwait(false);

                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    }
                }
                finally
                {
                    m_semaphoreSlim.Release();
                }

                ByteString serverNonce;
                UserTokenPolicy? userTokenPolicy;
                // Note: session lookup, lockout and expiry failures above are not
                // authentication failures and deliberately do NOT record a
                // brute-force attempt - only a failed client-signature or user
                // identity validation below does, so a timed-out or unknown session
                // cannot lock out a legitimate client.

                // Verify the client signature outside the global lock. This is the
                // CPU-bound part of activation (RSA/ECDSA verify); keeping it out of
                // the session-manager lock lets concurrent activations verify in
                // parallel. It operates only on this session's state, which is
                // guarded by the session's own lock inside ValidateBeforeActivate.
                try
                {
                    // create new server nonce.
                    serverNonceObject = Nonce.CreateNonce(
                        context.ChannelContext!.EndpointDescription!.SecurityPolicyUri!);

                    // validate before activation.
                    session.ValidateBeforeActivate(
                        context,
                        clientSignature!,
                        userIdentityToken,
                        userTokenSignature!,
                        out newIdentity,
                        out userTokenPolicy);

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
                            context.ChannelContext!.EndpointDescription!,
                            cancellationToken)
                        .ConfigureAwait(false);

                    // parse the token manually if the identity is not provided.
                    if (identity == null)
                    {
                        tempIdentity = newIdentity != null
                            ? new UserIdentity(newIdentity)
                            : new UserIdentity();
                        identity = tempIdentity;
                    }

                    // use the identity as the effectiveIdentity if not provided.
                    effectiveIdentity ??= identity;
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

                // check for validation error.
                if (ServiceResult.IsBad(error))
                {
                    RecordFailedAuthentication(clientKey);
                    throw new ServiceResultException(error!);
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

                // Add mandatory roles based on session/channel security context (e.g., TrustedApplication).
                effectiveIdentity = AddMandatoryRoles(session, context, effectiveIdentity);

                // Per Part 18 §5.2.8: when the authenticated user has the
                // MustChangePassword bit set on UserManagement, the activation
                // response shall carry Good_PasswordChangeRequired so the
                // client knows to prompt for a new password. The role
                // restriction is enforced separately by AddMandatoryRoles.
                ServiceResult activationStatus = ComputeActivationStatus(effectiveIdentity);

                // activate session.

                bool contextChanged = session.Activate(
                    context,
                    newIdentity!,
                    identity,
                    effectiveIdentity,
                    localeIds,
                    serverNonceObject);
                serverNonceObject = null; // ownership transferred to session
                tempIdentity = null; // ownership transferred to session

                // raise session related event.
                if (contextChanged)
                {
                    RaiseSessionEvent(session, SessionEventReason.Activated);
                }

                // indicates that the identity context for the session has changed.
                return (contextChanged, serverNonce, activationStatus);
            }
            finally
            {
                restoredSession?.Dispose();
                serverNonceObject?.Dispose();
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

            // thread safe search for the session.
            foreach (KeyValuePair<NodeId, ISession> current in m_sessions)
            {
                if (current.Value.Id == sessionId)
                {
#pragma warning disable CA2000 // Disposed correctly later
                    if (!m_sessions.TryRemove(current.Key, out session))
#pragma warning restore CA2000
                    {
                        // found but was already removed by another thread
                        return;
                    }
                    break;
                }
            }

            // close the session if removed.
            if (session != null)
            {
                try
                {
                    // raise session related event.
                    RaiseSessionEvent(session, SessionEventReason.Closing);

                    // close the session.
                    await session.CloseAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    session.Dispose();

                    // update diagnostics.
                    lock (m_server.DiagnosticsWriteLock)
                    {
                        m_server.ServerDiagnostics.CurrentSessionCount--;
                    }
                }
            }
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
                    EventHandler<ValidateSessionLessRequestEventArgs>? handler = m_ValidateSessionLessRequest;

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

                        return new OperationContext(requestHeader, secureChannelContext, requestType, requestLifetime, args.Identity);
                    }

                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }

                // validate request header.
                session!.ValidateRequest(requestHeader, secureChannelContext, requestType);

                // validate user has permissions for additional info
                session.ValidateDiagnosticInfo(requestHeader);

                // Lazily reconcile the RoleManager subscription. The
                // RoleManager may be injected after SessionManager
                // construction (see IServerInternal.SetRoleManager), so we
                // can't subscribe at startup; the first request that flows
                // through a fully-initialized server wires it up.
                EnsureRoleManagerSubscription();

                // Part 18 §4.4.1 — if a Role configuration change marked the
                // session's identity stale, re-evaluate the mandatory roles
                // (Anonymous/AuthenticatedUser/TrustedApplication + live
                // RoleManager identity-mapping rules) before the request runs
                // so that downstream access checks see the current grants.
                ReevaluateIdentityIfStale(session, secureChannelContext);

                // return context.
                return new OperationContext(requestHeader, secureChannelContext, requestType, requestLifetime, session);
            }
            catch (ServiceResultException sre)
            {
                if (sre.StatusCode == StatusCodes.BadSessionNotActivated && session != null)
                {
                    await CloseSessionAsync(session.Id, requestLifetime.CancellationToken).ConfigureAwait(false);
                }
                throw;
            }
            catch (Exception e)
            {
                throw ServiceResultException.Unexpected(e, e.Message);
            }
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
                    channelAppUri = session.SessionDiagnostics?.ClientDescription?.ApplicationUri;
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex, "Failed to populate channel context for authentication.");
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
                lock (m_eventLock)
                {
                    if (m_ImpersonateUser != null)
                    {
                        var args = new ImpersonateEventArgs(
                            newIdentity,
                            userTokenPolicy,
                            endpointDescription);
                        m_ImpersonateUser(session, args);

                        if (ServiceResult.IsBad(args.IdentityValidationError))
                        {
                            return (null, null, args.IdentityValidationError);
                        }

                        return (args.Identity, args.EffectiveIdentity, null);
                    }
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
            // Part 18 §5.2.8 — restrict the session to the Anonymous role if
            // the user has MustChangePassword set. The ChangePassword method
            // is callable by USERNAME sessions regardless of role; once the
            // password is changed the next ActivateSession will see
            // MustChangePassword == false and grant the full role set.
            if (effectiveIdentity.TokenType == UserTokenType.UserName &&
                !string.IsNullOrEmpty(effectiveIdentity.DisplayName) &&
                m_server.UserManagement?.MustChangePassword(effectiveIdentity.DisplayName) == true)
            {
                if (effectiveIdentity is RoleBasedIdentity rbiMustChange)
                {
                    return rbiMustChange.WithAdditionalRoles([Role.Anonymous], m_server.NamespaceUris);
                }
                return new RoleBasedIdentity(
                    effectiveIdentity,
                    [Role.Anonymous],
                    m_server.NamespaceUris);
            }

            // Assign TrustedApplication role per OPC UA Part 3 §4.9.
            if (session.ClientCertificate != null &&
                context.ChannelContext?.EndpointDescription?.SecurityMode >= MessageSecurityMode.Sign)
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
                    session.ClientCertificate,
                    context.ChannelContext?.EndpointDescription);

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
        /// <see cref="AddMandatoryRoles"/> using the original impersonated
        /// <see cref="ISession.Identity"/> as the starting point, so the
        /// outcome is deterministic and idempotent.
        /// </para>
        /// <para>
        /// Multiple concurrent requests racing through this method may each
        /// compute the same refresh; the last writer wins. This is acceptable
        /// because the computation is pure with respect to the current
        /// RoleManager state.
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
                // Build a minimal OperationContext to satisfy the
                // AddMandatoryRoles signature; only ChannelContext is
                // consulted (for the endpoint).
                var refreshContext = new OperationContext(
                    new RequestHeader(),
                    secureChannelContext,
                    RequestType.Unknown,
                    RequestLifetime.None,
                    session.EffectiveIdentity);

                IUserIdentity refreshed = AddMandatoryRoles(
                    session,
                    refreshContext,
                    session.Identity);

                session.RefreshEffectiveIdentity(refreshed);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(ex,
                    "Failed to re-evaluate session {SessionId} identity after Role configuration change.",
                    session.Id);
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
                m_logger.LogWarning(ex,
                    "Failed to mark sessions stale after Role configuration change.");
            }
        }

        /// <summary>
        /// Ensures the SessionManager is subscribed to the current
        /// <see cref="IRoleManager"/>'s <see cref="IRoleManager.RoleConfigurationChanged"/>
        /// event.
        /// </summary>
        /// <remarks>
        /// <see cref="IServerInternal.SetRoleManager"/> may be called after
        /// SessionManager construction, and the RoleManager instance can be
        /// swapped at runtime (e.g. by integrators wiring a persistence
        /// backend). The subscription is reconciled lazily on each request
        /// so the wiring works regardless of injection order.
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
                        m_logger.LogTrace(e, "Session event handler raised an exception.");
                    }
                }
            }
        }

        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private async ValueTask MonitorSessionsAsync(object data)
        {
            try
            {
                m_logger.LogInformation("Server - Session Monitor Thread Started.");

                int sleepCycle = Convert.ToInt32(data, CultureInfo.InvariantCulture);

                while (true)
                {
                    // enumerator is thread safe
                    foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in m_sessions)
                    {
                        ISession session = sessionKeyValue.Value;
                        if (session.HasExpired)
                        {
                            // update diagnostics.
                            lock (m_server.DiagnosticsWriteLock)
                            {
                                m_server.ServerDiagnostics.SessionTimeoutCount++;
                            }

                            // raise audit event for session closed because of timeout
                            m_server.ReportAuditCloseSessionEvent(null!, session, m_logger, "Session/Timeout");

                            await m_server.CloseSessionAsync(null!, session.Id, false)
                                .ConfigureAwait(false);
                        }
                        // if a session had no activity for the last m_minSessionTimeout milliseconds, send a keep alive event.
                        else if (m_timeProvider.GetTimestampMilliseconds() - session.LastContactTickCount > m_minSessionTimeout)
                        {
                            // signal the channel that the session is still active.
                            RaiseSessionEvent(session, SessionEventReason.ChannelKeepAlive);
                        }
                    }

                    if (m_shutdownEvent.WaitOne(sleepCycle))
                    {
                        m_logger.LogTrace("Server - Session Monitor Thread Exited Normally.");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Server - Session Monitor Thread Exited Unexpectedly");
            }
        }

        private readonly SemaphoreSlim m_semaphoreSlim = new(1, 1);
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly NodeIdDictionary<ISession> m_sessions;
        private uint m_lastSessionId;
        private readonly ManualResetEvent m_shutdownEvent;

        private readonly int m_minSessionTimeout;
        private readonly int m_maxSessionTimeout;
        private readonly int m_maxSessionCount;
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

        /// <summary>
        /// Last <see cref="IRoleManager"/> we wired
        /// <see cref="OnRoleConfigurationChanged"/> onto. Reconciled in
        /// <see cref="EnsureRoleManagerSubscription"/>.
        /// </summary>
        private IRoleManager? m_subscribedRoleManager;

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
            "server.CurrentInstance.IdentityRegistry.Register(...). See Docs/IdentityProviders.md.")]
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
        /// Gets the lockout key for a client based on certificate thumbprint or application URI.
        /// </summary>
        private static string GetClientLockoutKey(ISession session)
        {
            if (session?.ClientCertificate != null)
            {
                return session.ClientCertificate.Thumbprint;
            }

            string? applicationUri = session?.SessionDiagnostics?.ClientDescription?.ApplicationUri;
            if (!string.IsNullOrEmpty(applicationUri))
            {
                return applicationUri!;
            }

            return session?.SecureChannelId ?? string.Empty;
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
                m_logger.LogWarning(
                    "Client {ClientKey} has been locked out after {FailedAttempts} failed authentication attempts. Lockout expires in {RemainingSeconds} seconds.",
                    clientKey,
                    lockoutInfo.FailedAttempts,
                    remainingSeconds);
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
}
