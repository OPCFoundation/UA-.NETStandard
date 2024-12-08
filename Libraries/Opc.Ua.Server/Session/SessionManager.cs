/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public class SessionManager : ISessionManager, IDisposable
    {
        #region Constructors
        /// <summary>
        /// Initializes the manager with its configuration.
        /// </summary>
        public SessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            m_server = server;

            m_minSessionTimeout = configuration.ServerConfiguration.MinSessionTimeout;
            m_maxSessionTimeout = configuration.ServerConfiguration.MaxSessionTimeout;
            m_maxSessionCount = configuration.ServerConfiguration.MaxSessionCount;
            m_maxRequestAge = configuration.ServerConfiguration.MaxRequestAge;
            m_maxBrowseContinuationPoints = configuration.ServerConfiguration.MaxBrowseContinuationPoints;
            m_maxHistoryContinuationPoints = configuration.ServerConfiguration.MaxHistoryContinuationPoints;
            m_minNonceLength = configuration.SecurityConfiguration.NonceLength;

            m_sessions = new ConcurrentDictionary<NodeId, Session>(Environment.ProcessorCount, m_maxSessionCount);
            m_lastSessionId = BitConverter.ToInt64(Utils.Nonce.CreateNonce(sizeof(long)), 0);

            // create a event to signal shutdown.
            m_shutdownEvent = new ManualResetEvent(true);
        }
        #endregion

        #region IDisposable Members        
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
                var sessions = m_sessions.ToArray();
                m_sessions.Clear();

                foreach (var sessionKeyValue in sessions)
                {
                    Utils.SilentDispose(sessionKeyValue.Value);
                }

                m_shutdownEvent.Set();
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Starts the session manager.
        /// </summary>
        public virtual void Startup()
        {
            lock (m_lock)
            {
                // start thread to monitor sessions.
                m_shutdownEvent.Reset();

                Task.Factory.StartNew(() => {
                    MonitorSessions(m_minSessionTimeout);
                }, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach);
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
            var sessions = m_sessions.ToArray();
            m_sessions.Clear();

            foreach (var sessionKeyValue in sessions)
            {
                Utils.SilentDispose(sessionKeyValue.Value);
            }
        }

        /// <summary>
        /// Creates a new session.
        /// </summary>
        public virtual Session CreateSession(
            OperationContext context,
            X509Certificate2 serverCertificate,
            string sessionName,
            byte[] clientNonce,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out byte[] serverNonce,
            out double revisedSessionTimeout)
        {
            sessionId = 0;
            revisedSessionTimeout = requestedSessionTimeout;

            Session session = null;

            lock (m_lock)
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
                    foreach (var sessionKeyValueIterator in m_sessions)
                    {
                        byte[] sessionClientNonce = sessionKeyValueIterator.Value?.ClientNonce;
                        if (Utils.CompareNonce(sessionClientNonce, clientNonce))
                        {
                            throw new ServiceResultException(StatusCodes.BadNonceInvalid);
                        }
                    }
                }

                // can assign a simple identifier if secured.
                authenticationToken = null;
                if (!String.IsNullOrEmpty(context.ChannelContext.SecureChannelId))
                {
                    if (context.ChannelContext.EndpointDescription.SecurityMode != MessageSecurityMode.None)
                    {
                        authenticationToken = new NodeId(Utils.IncrementIdentifier(ref m_lastSessionId));
                    }
                }

                // must assign a hard-to-guess id if not secured.
                if (authenticationToken == null)
                {
                    byte[] token = Utils.Nonce.CreateNonce(32);
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
                serverNonce = Utils.Nonce.CreateNonce((uint)m_minNonceLength);

                // assign client name.
                if (String.IsNullOrEmpty(sessionName))
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
                    serverNonce,
                    sessionName,
                    clientDescription,
                    endpointUrl,
                    clientCertificate,
                    revisedSessionTimeout,
                    maxResponseMessageSize,
                    m_maxRequestAge,
                    m_maxBrowseContinuationPoints);

                // get the session id.
                sessionId = session.Id;

                // save session.
                if (!m_sessions.TryAdd(authenticationToken, session))
                {
                    throw new ServiceResultException(StatusCodes.BadTooManySessions);
                }
            }

            // raise session related event.
            RaiseSessionEvent(session, SessionEventReason.Created);

            // return session.
            return session;
        }

        /// <summary>
        /// Activates an existing session
        /// </summary>
        public virtual bool ActivateSession(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            StringCollection localeIds,
            out byte[] serverNonce)
        {
            serverNonce = null;

            Session session = null;
            UserIdentityToken newIdentity = null;
            UserTokenPolicy userTokenPolicy = null;

            // fast path no lock
            if (!m_sessions.TryGetValue(authenticationToken, out _))
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }

            lock (m_lock)
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
                    m_server.ReportAuditCloseSessionEvent(null, session, "Session/Timeout");

                    m_server.CloseSession(null, session.Id, false);

                    throw new ServiceResultException(StatusCodes.BadSessionClosed);
                }

                // create new server nonce.
                serverNonce = Utils.Nonce.CreateNonce((uint)m_minNonceLength);

                // validate before activation.
                session.ValidateBeforeActivate(
                    context,
                    clientSignature,
                    clientSoftwareCertificates,
                    userIdentityToken,
                    userTokenSignature,
                    localeIds,
                    serverNonce,
                    out newIdentity,
                    out userTokenPolicy);
            }
            IUserIdentity identity = null;
            IUserIdentity effectiveIdentity = null;
            ServiceResult error = null;

            try
            {
                // check if the application has a callback which validates the identity tokens.
                lock (m_eventLock)
                {
                    if (m_impersonateUser != null)
                    {
                        ImpersonateEventArgs args = new ImpersonateEventArgs(newIdentity, userTokenPolicy, context.ChannelContext.EndpointDescription);
                        m_impersonateUser(session, args);

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
                if (identity == null)
                {
                    identity = newIdentity != null ? new UserIdentity(newIdentity) : new UserIdentity();
                }

                // use the identity as the effectiveIdentity if not provided.
                if (effectiveIdentity == null)
                {
                    effectiveIdentity = identity;
                }
            }
            catch (Exception e)
            {
                if (e is ServiceResultException)
                {
                    throw;
                }

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
                serverNonce);

            // raise session related event.
            if (contextChanged)
            {
                RaiseSessionEvent(session, SessionEventReason.Activated);
            }

            // indicates that the identity context for the session has changed.
            return contextChanged;
        }

        /// <summary>
        /// Closes the specifed session.
        /// </summary>
        /// <remarks>
        /// This method should not throw an exception if the session no longer exists.
        /// </remarks>
        public virtual void CloseSession(NodeId sessionId)
        {
            Session session = null;

            // thread safe search for the session.
            foreach (KeyValuePair<NodeId, Session> current in m_sessions)
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
        public virtual OperationContext ValidateRequest(RequestHeader requestHeader, RequestType requestType)
        {
            if (requestHeader == null) throw new ArgumentNullException(nameof(requestHeader));

            Session session = null;

            try
            {
                // check for create session request.
                if (requestType == RequestType.CreateSession || requestType == RequestType.ActivateSession)
                {
                    return new OperationContext(requestHeader, requestType);
                }

                // find session.
                if (!m_sessions.TryGetValue(requestHeader.AuthenticationToken, out session))
                {
                    EventHandler<ValidateSessionLessRequestEventArgs> handler = m_validateSessionLessRequest;

                    if (handler != null)
                    {
                        var args = new ValidateSessionLessRequestEventArgs(requestHeader.AuthenticationToken, requestType);
                        handler(this, args);

                        if (ServiceResult.IsBad(args.Error))
                        {
                            throw new ServiceResultException(args.Error);
                        }

                        return new OperationContext(requestHeader, requestType, args.Identity);
                    }

                    throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
                }

                // validate request header.
                session.ValidateRequest(requestHeader, requestType);

                // validate user has permissions for additional info
                session.ValidateDiagnosticInfo(requestHeader);

                // return context.
                return new OperationContext(requestHeader, requestType, session);
            }
            catch (Exception e)
            {
                ServiceResultException sre = e as ServiceResultException;

                if (sre != null && sre.StatusCode == StatusCodes.BadSessionNotActivated)
                {
                    if (session != null)
                    {
                        CloseSession(session.Id);
                    }
                }

                throw new ServiceResultException(e, StatusCodes.BadUnexpectedError);
            }
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Creates a new instance of a session.
        /// </summary>
        protected virtual Session CreateSession(
            OperationContext context,
            IServerInternal server,
            X509Certificate2 serverCertificate,
            NodeId sessionCookie,
            byte[] clientNonce,
            byte[] serverNonce,
            string sessionName,
            ApplicationDescription clientDescription,
            string endpointUrl,
            X509Certificate2 clientCertificate,
            double sessionTimeout,
            uint maxResponseMessageSize,
            int maxRequestAge, // TBD - Remove unused parameter.
            int maxContinuationPoints) // TBD - Remove unused parameter.
        {
            Session session = new Session(
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
                sessionTimeout,
                maxResponseMessageSize,
                m_maxRequestAge,
                m_maxBrowseContinuationPoints,
                m_maxHistoryContinuationPoints);

            return session;
        }

        /// <summary>
        /// Raises an event related to a session.
        /// </summary>
        protected virtual void RaiseSessionEvent(Session session, SessionEventReason reason)
        {
            lock (m_eventLock)
            {
                SessionEventHandler handler = null;

                switch (reason)
                {
                    case SessionEventReason.Created: { handler = m_sessionCreated; break; }
                    case SessionEventReason.Activated: { handler = m_sessionActivated; break; }
                    case SessionEventReason.Closing: { handler = m_sessionClosing; break; }
                    case SessionEventReason.ChannelKeepAlive: { handler = m_sessionChannelKeepAlive; break; }
                }

                if (handler != null)
                {
                    try
                    {
                        handler(session, reason);
                    }
                    catch (Exception e)
                    {
                        Utils.LogTrace(e, "Session event handler raised an exception.");
                    }
                }
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Periodically checks if the sessions have timed out.
        /// </summary>
        private void MonitorSessions(object data)
        {
            try
            {
                Utils.LogInfo("Server - Session Monitor Thread Started.");

                int sleepCycle = Convert.ToInt32(data, CultureInfo.InvariantCulture);

                do
                {
                    // enumerator is thread safe
                    foreach (var sessionKeyValue in m_sessions)
                    {
                        Session session = sessionKeyValue.Value;
                        if (session.HasExpired)
                        {
                            // update diagnostics.
                            lock (m_server.DiagnosticsWriteLock)
                            {
                                m_server.ServerDiagnostics.SessionTimeoutCount++;
                            }

                            // raise audit event for session closed because of timeout
                            m_server.ReportAuditCloseSessionEvent(null, session, "Session/Timeout");

                            m_server.CloseSession(null, session.Id, false);
                        }
                        // if a session had no activity for the last m_minSessionTimeout milliseconds, send a keep alive event.
                        else if (session.ClientLastContactTime.AddMilliseconds(m_minSessionTimeout) < DateTime.UtcNow)
                        {
                            // signal the channel that the session is still active.
                            RaiseSessionEvent(session, SessionEventReason.ChannelKeepAlive);
                        }
                    }

                    if (m_shutdownEvent.WaitOne(sleepCycle))
                    {
                        Utils.LogTrace("Server - Session Monitor Thread Exited Normally.");
                        break;
                    }
                }
                while (true);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Server - Session Monitor Thread Exited Unexpectedly");
            }
        }
        #endregion

        #region Private Fields
        private readonly object m_lock = new object();
        private IServerInternal m_server;
        private ConcurrentDictionary<NodeId, Session> m_sessions;
        private long m_lastSessionId;
        private ManualResetEvent m_shutdownEvent;

        private int m_minSessionTimeout;
        private int m_maxSessionTimeout;
        private int m_maxSessionCount;
        private int m_maxRequestAge;

        private int m_maxBrowseContinuationPoints;
        private int m_maxHistoryContinuationPoints;
        private int m_minNonceLength;

        private readonly object m_eventLock = new object();
        private event SessionEventHandler m_sessionCreated;
        private event SessionEventHandler m_sessionActivated;
        private event SessionEventHandler m_sessionClosing;
        private event SessionEventHandler m_sessionChannelKeepAlive;
        private event ImpersonateEventHandler m_impersonateUser;
        private event EventHandler<ValidateSessionLessRequestEventArgs> m_validateSessionLessRequest;
        #endregion

        #region ISessionManager Members
        /// <inheritdoc/>
        public event SessionEventHandler SessionCreated
        {
            add
            {
                lock (m_eventLock)
                {
                    m_sessionCreated += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_sessionCreated -= value;
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
                    m_sessionActivated += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_sessionActivated -= value;
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
                    m_sessionClosing += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_sessionClosing -= value;
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
                    m_sessionChannelKeepAlive += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_sessionChannelKeepAlive -= value;
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
                    m_impersonateUser += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_impersonateUser -= value;
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
                    m_validateSessionLessRequest += value;
                }
            }

            remove
            {
                lock (m_eventLock)
                {
                    m_validateSessionLessRequest -= value;
                }
            }
        }

        /// <inheritdoc/>
        public IList<Session> GetSessions()
        {
            lock (m_lock)
            {
                return new List<Session>(m_sessions.Values);
            }
        }


        /// <inheritdoc/>
        public Session GetSession(NodeId authenticationToken)
        {
            // find session.
            if (m_sessions.TryGetValue(authenticationToken, out Session session))
            {
                return session;
            }
            return null;
        }
        #endregion
    }

    /// <summary>
    /// Allows application components to receive notifications when changes to sessions occur.
    /// </summary>
    /// <remarks>
    /// Sinks that receive these events must not block the thread.
    /// </remarks>
    public interface ISessionManager
    {
        /// <summary>
        /// Raised after a new session is created.
        /// </summary>
        event SessionEventHandler SessionCreated;

        /// <summary>
        /// Raised whenever a session is activated and the user identity or preferred locales changed.
        /// </summary>
        event SessionEventHandler SessionActivated;

        /// <summary>
        /// Raised before a session is closed.
        /// </summary>
        event SessionEventHandler SessionClosing;

        /// <summary>
        /// Raised to signal a channel that the session is still alive.
        /// </summary>
        event SessionEventHandler SessionChannelKeepAlive;

        /// <summary>
        /// Raised before the user identity for a session is changed.
        /// </summary>
        event ImpersonateEventHandler ImpersonateUser;

        /// <summary>
        /// Raised to validate a session-less request.
        /// </summary>
        event EventHandler<ValidateSessionLessRequestEventArgs> ValidateSessionLessRequest;

        /// <summary>
        /// Returns all of the sessions known to the session manager.
        /// </summary>
        /// <returns>A list of the sessions.</returns>
        IList<Session> GetSessions();

        /// <summary>
        /// Find and return a session specified by authentication token
        /// </summary>
        /// <returns>The requested session.</returns>
        Session GetSession(NodeId authenticationToken);
    }

    /// <summary>
    /// The possible reasons for a session related event. 
    /// </summary>
    public enum SessionEventReason
    {
        /// <summary>
        /// A new session was created.
        /// </summary>
        Created,

        /// <summary>
        /// A session is being activated with a new user identity.
        /// </summary>
        Impersonating,

        /// <summary>
        /// A session was activated and the user identity or preferred locales changed.
        /// </summary>
        Activated,

        /// <summary>
        /// A session is about to be closed.
        /// </summary>
        Closing,

        /// <summary>
        /// A keep alive to signal a channel that the session is still active.
        /// Triggered by the session manager based on <see cref="ServerConfiguration.MinSessionTimeout"/>.
        /// </summary>
        ChannelKeepAlive
    }

    /// <summary>
    /// The delegate for functions used to receive session related events.
    /// </summary>
    public delegate void SessionEventHandler(Session session, SessionEventReason reason);

    #region ImpersonateEventArgs Class
    /// <summary>
    /// A class which provides the event arguments for session related event.
    /// </summary>
    public class ImpersonateEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ImpersonateEventArgs(UserIdentityToken newIdentity, UserTokenPolicy userTokenPolicy, EndpointDescription endpointDescription = null)
        {
            m_newIdentity = newIdentity;
            m_userTokenPolicy = userTokenPolicy;
            m_endpointDescription = endpointDescription;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The new user identity for the session.
        /// </summary>
        public UserIdentityToken NewIdentity
        {
            get { return m_newIdentity; }
        }

        /// <summary>
        /// The user token policy selected by the client.
        /// </summary>
        public UserTokenPolicy UserTokenPolicy
        {
            get { return m_userTokenPolicy; }
        }

        /// <summary>
        /// An application defined handle that can be used for access control operations.
        /// </summary>
        public IUserIdentity Identity
        {
            get { return m_identity; }
            set { m_identity = value; }
        }

        /// <summary>
        /// An application defined handle that can be used for access control operations.
        /// </summary>
        public IUserIdentity EffectiveIdentity
        {
            get { return m_effectiveIdentity; }
            set { m_effectiveIdentity = value; }
        }

        /// <summary>
        /// Set to indicate that an error occurred validating the identity and that it should be rejected.
        /// </summary>
        public ServiceResult IdentityValidationError
        {
            get { return m_identityValidationError; }
            set { m_identityValidationError = value; }
        }

        /// <summary>
        /// Get the EndpointDescription  
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get { return m_endpointDescription; }
        }
        #endregion

        #region Private Fields
        private UserIdentityToken m_newIdentity;
        private UserTokenPolicy m_userTokenPolicy;
        private ServiceResult m_identityValidationError;
        private IUserIdentity m_identity;
        private IUserIdentity m_effectiveIdentity;
        private EndpointDescription m_endpointDescription;
        #endregion
    }

    /// <summary>
    /// The delegate for functions used to receive impersonation events.
    /// </summary>
    public delegate void ImpersonateEventHandler(Session session, ImpersonateEventArgs args);
    #endregion

    #region ImpersonateEventArgs Class
    /// <summary>
    /// A class which provides the event arguments for session related event.
    /// </summary>
    public class ValidateSessionLessRequestEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ValidateSessionLessRequestEventArgs(NodeId authenticationToken, RequestType requestType)
        {
            AuthenticationToken = authenticationToken;
            RequestType = requestType;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The request type for the request.
        /// </summary>
        public RequestType RequestType { get; private set; }

        /// <summary>
        /// The new user identity for the session.
        /// </summary>
        public NodeId AuthenticationToken { get; private set; }

        /// <summary>
        /// The identity to associate with the session-less request.
        /// </summary>
        public IUserIdentity Identity { get; set; }

        /// <summary>
        /// Set to indicate that an error occurred validating the session-less request and that it should be rejected.
        /// </summary>
        public ServiceResult Error { get; set; }
        #endregion
    }
    #endregion
}
