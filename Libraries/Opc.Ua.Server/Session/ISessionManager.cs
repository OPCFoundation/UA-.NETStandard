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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Allows application components to receive notifications when changes to sessions occur.
    /// </summary>
    /// <remarks>
    /// Sinks that receive these events must not block the thread.
    /// </remarks>
    public interface ISessionManager : IDisposable
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
        /// Starts the session manager.
        /// </summary>
        void Startup();

        /// <summary>
        /// Stops the session manager and closes all sessions.
        /// </summary>
        void Shutdown();

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

        /// <summary>
        /// Creates a new session.
        /// </summary>
        Session CreateSession(
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
            out NodeId sessionId,
            out NodeId authenticationToken,
            out byte[] serverNonce,
            out double revisedSessionTimeout);

        /// <summary>
        /// Activates an existing session
        /// </summary>
        bool ActivateSession(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            StringCollection localeIds,
            out byte[] serverNonce);

        /// <summary>
        /// Closes the specified session.
        /// </summary>
        /// <remarks>
        /// This method should not throw an exception if the session no longer exists.
        /// </remarks>
        void CloseSession(NodeId sessionId);

        /// <summary>
        /// Validates request header and returns a request context.
        /// </summary>
        /// <remarks>
        /// This method verifies that the session id is valid and that it uses secure channel id
        /// associated with current thread. It also verifies that the timestamp is not too
        /// and that the sequence number is not out of order (update requests only).
        /// </remarks>
        OperationContext ValidateRequest(RequestHeader requestHeader, RequestType requestType);
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

    #region ValidateSessionLessRequestEventArgs Class
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
