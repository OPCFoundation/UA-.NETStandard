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
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<SessionManager>();

            m_minSessionTimeout = configuration.ServerConfiguration.MinSessionTimeout;
            m_maxSessionTimeout = configuration.ServerConfiguration.MaxSessionTimeout;
            m_maxSessionCount = configuration.ServerConfiguration.MaxSessionCount;
            m_maxRequestAge = configuration.ServerConfiguration.MaxRequestAge;
            m_maxBrowseContinuationPoints = configuration.ServerConfiguration
                .MaxBrowseContinuationPoints;
            m_maxHistoryContinuationPoints = configuration.ServerConfiguration
                .MaxHistoryContinuationPoints;

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
                // create snapshot of all sessions
                KeyValuePair<NodeId, ISession>[] sessions = [.. m_sessions];
                m_sessions.Clear();

                foreach (KeyValuePair<NodeId, ISession> sessionKeyValue in sessions)
                {
                    Utils.SilentDispose(sessionKeyValue.Value);
                }

                m_shutdownEvent.Set();
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
                Utils.SilentDispose(sessionKeyValue.Value);
            }
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async ValueTask<CreateSessionResult> CreateSessionAsync(
            OperationContext context,
            X509Certificate2 serverCertificate,
            string sessionName,
            byte[] clientNonce,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken cancellationToken = default)
        {
            NodeId sessionId = 0;
            NodeId authenticationToken;
            byte[] serverNonce;
            double revisedSessionTimeout = requestedSessionTimeout;

            ISession session;

            await m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // check session count.
                if (m_maxSessionCount > 0 && m_sessions.Count >= m_maxSessionCount)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }

                // check for same Nonce in another session
                if (clientNonce != null)
                {
                    // iterate over key/value pairs in the dictionary with a thread safe iterator
                    foreach (KeyValuePair<NodeId, ISession> sessionKeyValueIterator in m_sessions)
                    {
                        byte[] sessionClientNonce = sessionKeyValueIterator.Value?.ClientNonce;
                        if (Nonce.CompareNonce(sessionClientNonce, clientNonce))
                        {
                            throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                        }
                    }
                }

                // can assign a simple identifier if secured.
                authenticationToken = null;
                if (!string.IsNullOrEmpty(context.ChannelContext.SecureChannelId) &&
                    context.ChannelContext.EndpointDescription
                        .SecurityMode != MessageSecurityMode.None)
                {
                    authenticationToken = new NodeId(
                        Utils.IncrementIdentifier(ref m_lastSessionId));
                }

                // must assign a hard-to-guess id if not secured.
                if (authenticationToken == null)
                {
                    byte[] token = Nonce.CreateRandomNonceData(32);
                    authenticationToken = new NodeId(token);
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
                var serverNonceObject = Nonce.CreateNonce(
                    context.ChannelContext.EndpointDescription.SecurityPolicyUri);

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
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    clientCertificateChain,
                    revisedSessionTimeout,
                    maxResponseMessageSize,
                    m_maxRequestAge,
                    m_maxBrowseContinuationPoints);

                // get the session id.
                sessionId = session.Id;
                serverNonce = serverNonceObject.Data;

                // save session.
                if (!m_sessions.TryAdd(authenticationToken, session))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }
            }
            finally
            {
                m_semaphoreSlim.Release();
            }

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
        public virtual async ValueTask<(bool IdentityContextChanged, byte[] ServerNonce)> ActivateSessionAsync(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            StringCollection localeIds,
            CancellationToken cancellationToken = default)
        {
            byte[] serverNonce = null;

            Nonce serverNonceObject = null;

            ISession session = null;
            UserIdentityToken newIdentity = null;
            UserTokenPolicy userTokenPolicy = null;

            // fast path no lock
            if (!m_sessions.TryGetValue(authenticationToken, out _))
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }

            await m_semaphoreSlim.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                // find session.
                if (!m_sessions.TryGetValue(authenticationToken, out session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }

                // check if session timeout has expired.
                if (session.HasExpired)
                {
                    // raise audit event for session closed because of timeout
                    m_server.ReportAuditCloseSessionEvent(null, session, m_logger, "Session/Timeout");

                    m_server.CloseSession(null, session.Id, false);

                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }

                // create new server nonce.
                serverNonceObject = Nonce.CreateNonce(
                    context.ChannelContext.EndpointDescription.SecurityPolicyUri);

                // validate before activation.
                session.ValidateBeforeActivate(
                    context,
                    clientSignature,
                    clientSoftwareCertificates,
                    userIdentityToken,
                    userTokenSignature,
                    out newIdentity,
                    out userTokenPolicy);

                serverNonce = serverNonceObject.Data;
            }
            finally
            {
                m_semaphoreSlim.Release();
            }
            IUserIdentity identity = null;
            IUserIdentity effectiveIdentity = null;
            ServiceResult error = null;

            try
            {
                // check if the application has a callback which validates the identity tokens.
                lock (m_eventLock)
                {
                    if (m_ImpersonateUser != null)
                    {
                        var args = new ImpersonateEventArgs(
                            newIdentity,
                            userTokenPolicy,
                            context.ChannelContext.EndpointDescription);
                        m_ImpersonateUser(session, args);

                        if (ServiceResult.IsBad(args.IdentityValidationError))
                        {
                            error = args.IdentityValidationError;
                        }
                        else
                        {
                            identity = args.Identity;
                            effectiveIdentity = args.EffectiveIdentity;
                        }
                    }
                }

                // parse the token manually if the identity is not provided.
                identity ??= newIdentity != null
                    ? new UserIdentity(newIdentity)
                    : new UserIdentity();

                // use the identity as the effectiveIdentity if not provided.
                effectiveIdentity ??= identity;
            }
            catch (Exception e) when (e is not ServiceResultException)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIdentityTokenInvalid,
                    e,
                    "Could not validate user identity token: {0}",
                    newIdentity);
            }

            // check for validation error.
            if (ServiceResult.IsBad(error))
            {
                throw new ServiceResultException(error);
            }

            // activate session.

            bool contextChanged = session.Activate(
                context,
                clientSoftwareCertificates,
                newIdentity,
                identity,
                effectiveIdentity,
                localeIds,
                serverNonceObject);

            // raise session related event.
            if (contextChanged)
            {
                RaiseSessionEvent(session, SessionEventReason.Activated);
            }

            // indicates that the identity context for the session has changed.
            return (contextChanged, serverNonce);
        }

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <remarks>
        /// This method should not throw an exception if the session no longer exists.
        /// </remarks>
        public virtual void CloseSession(NodeId sessionId)
        {
            ISession session = null;

            // thread safe search for the session.
            foreach (KeyValuePair<NodeId, ISession> current in m_sessions)
            {
                if (current.Value.Id == sessionId)
                {
                    if (!m_sessions.TryRemove(current.Key, out session))
                    {
                        // found but was already removed
                        return;
                    }
                    break;
                }
            }

            // close the session if removed.
            if (session != null)
            {
                // raise session related event.
                RaiseSessionEvent(session, SessionEventReason.Closing);

                // close the session.
                session.Close();

                // update diagnostics.
                lock (m_server.DiagnosticsWriteLock)
                {
                    m_server.ServerDiagnostics.CurrentSessionCount--;
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
        public virtual OperationContext ValidateRequest(
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            RequestType requestType)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ISession session = null;

            try
            {
                // check for create session request.
                if (requestType is RequestType.CreateSession or RequestType.ActivateSession)
                {
                    return new OperationContext(requestHeader, secureChannelContext, requestType);
                }

                // find session.
                if (!m_sessions.TryGetValue(requestHeader.AuthenticationToken, out session))
                {
                    EventHandler<ValidateSessionLessRequestEventArgs> handler
                        = m_ValidateSessionLessRequest;

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

                        return new OperationContext(requestHeader, secureChannelContext, requestType, args.Identity);
                    }

                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }

                // validate request header.
                session.ValidateRequest(requestHeader, secureChannelContext, requestType);

                // validate user has permissions for additional info
                session.ValidateDiagnosticInfo(requestHeader);

                // return context.
                return new OperationContext(requestHeader, secureChannelContext, requestType, session);
            }
            catch (Exception e)
            {
                if (e is ServiceResultException sre &&
                    sre.StatusCode == StatusCodes.BadSessionNotActivated &&
                    session != null)
                {
                    CloseSession(session.Id);
                }

                throw new ServiceResultException(e, StatusCodes.BadUnexpectedError);
            }
        }

        /// <summary>
        /// Creates a new instance of a session.
        /// </summary>
        protected virtual ISession CreateSession(
            OperationContext context,
            IServerInternal server,
            X509Certificate2 serverCertificate,
            NodeId sessionCookie,
            byte[] clientNonce,
            Nonce serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
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
                m_maxHistoryContinuationPoints);
        }

        /// <inheritdoc />
        public virtual void UpdateSessionDiagnostics(ISession session)
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
                SessionEventHandler handler = null;

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
                            m_server.ReportAuditCloseSessionEvent(null, session, m_logger, "Session/Timeout");

                            await m_server.CloseSessionAsync(null, session.Id, false)
                                .ConfigureAwait(false);
                        }
                        // if a session had no activity for the last m_minSessionTimeout milliseconds, send a keep alive event.
                        else if (session.ClientLastContactTime
                            .AddMilliseconds(m_minSessionTimeout) < DateTime.UtcNow)
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

        private readonly Lock m_eventLock = new();
        private event SessionEventHandler m_SessionCreated;
        private event SessionEventHandler m_SessionActivated;
        private event SessionEventHandler m_SessionClosing;
        private event SessionEventHandler m_SessionDiagnosticsChanged;
        private event SessionEventHandler m_SessionChannelKeepAlive;
        private event ImpersonateEventHandler m_ImpersonateUser;
        private event EventHandler<ValidateSessionLessRequestEventArgs> m_ValidateSessionLessRequest;

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
        public IList<ISession> GetSessions()
        {
            return [.. m_sessions.Values];
        }

        /// <inheritdoc/>
        public ISession GetSession(NodeId authenticationToken)
        {
            // find session.
            if (m_sessions.TryGetValue(authenticationToken, out ISession session))
            {
                return session;
            }
            return null;
        }
    }
}
