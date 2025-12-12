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
using System.Threading;
using System.Threading.Tasks;

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
        /// Raised after diagnostics of an existing session were changed.
        /// </summary>
        event SessionEventHandler SessionDiagnosticsChanged;

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
        ValueTask StartupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the session manager and closes all sessions.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Returns all of the sessions known to the session manager.
        /// </summary>
        /// <returns>A list of the sessions.</returns>
        IList<ISession> GetSessions();

        /// <summary>
        /// Find and return a session specified by authentication token
        /// </summary>
        /// <returns>The requested session.</returns>
        ISession GetSession(NodeId authenticationToken);

        /// <summary>
        /// Creates a new session.
        /// </summary>
        ValueTask<CreateSessionResult> CreateSessionAsync(
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
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Activates an existing session
        /// </summary>
        ValueTask<(bool IdentityContextChanged, byte[] ServerNonce)> ActivateSessionAsync(
            OperationContext context,
            NodeId authenticationToken,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            StringCollection localeIds,
            CancellationToken cancellationToken = default);

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
        OperationContext ValidateRequest(RequestHeader requestHeader, SecureChannelContext secureChannelContext, RequestType requestType);

        /// <summary>
        /// Marks the service diagnostics for the specified session as updated and triggers the
        /// <see cref="ISessionManager.SessionDiagnosticsChanged"/> event so subscribers can react.
        /// </summary>
        void UpdateSessionDiagnostics(ISession session);
    }

    /// <summary>
    /// The result of a call to <see cref="ISessionManager.CreateSessionAsync"/>.
    /// </summary>
    public class CreateSessionResult
    {
        /// <summary>
        /// The created Session.
        /// </summary>
        public required ISession Session { get; init; }

        /// <summary>
        /// The SessionID assigned to the session.
        /// </summary>
        public required NodeId SessionId { get; init; }

        /// <summary>
        /// The authentication token used to identify and authorize the client.
        /// </summary>
        public required NodeId AuthenticationToken { get; init; }

        /// <summary>
        /// The server nonce of the session.
        /// </summary>
        public required byte[] ServerNonce { get; init; }

        /// <summary>
        /// The revised session timeout.
        /// </summary>
        public required double RevisedSessionTimeout { get; init; }
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
        /// The diagnostics of an existing session were changed.
        /// </summary>
        DiagnosticsChanged,

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
    public delegate void SessionEventHandler(ISession session, SessionEventReason reason);

    /// <summary>
    /// A class which provides the event arguments for session related event.
    /// </summary>
    public class ImpersonateEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ImpersonateEventArgs(
            UserIdentityToken newIdentity,
            UserTokenPolicy userTokenPolicy,
            EndpointDescription endpointDescription = null)
        {
            NewIdentity = newIdentity;
            UserTokenPolicy = userTokenPolicy;
            EndpointDescription = endpointDescription;
        }

        /// <summary>
        /// The new user identity for the session.
        /// </summary>
        public UserIdentityToken NewIdentity { get; }

        /// <summary>
        /// The user token policy selected by the client.
        /// </summary>
        public UserTokenPolicy UserTokenPolicy { get; }

        /// <summary>
        /// An application defined handle that can be used for access control operations.
        /// </summary>
        public IUserIdentity Identity { get; set; }

        /// <summary>
        /// An application defined handle that can be used for access control operations.
        /// </summary>
        public IUserIdentity EffectiveIdentity { get; set; }

        /// <summary>
        /// Set to indicate that an error occurred validating the identity and that it should be rejected.
        /// </summary>
        public ServiceResult IdentityValidationError { get; set; }

        /// <summary>
        /// Get the EndpointDescription
        /// </summary>
        public EndpointDescription EndpointDescription { get; }
    }

    /// <summary>
    /// The delegate for functions used to receive impersonation events.
    /// </summary>
    public delegate void ImpersonateEventHandler(ISession session, ImpersonateEventArgs args);

    /// <summary>
    /// A class which provides the event arguments for session related event.
    /// </summary>
    public class ValidateSessionLessRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        public ValidateSessionLessRequestEventArgs(
            NodeId authenticationToken,
            RequestType requestType)
        {
            AuthenticationToken = authenticationToken;
            RequestType = requestType;
        }

        /// <summary>
        /// The request type for the request.
        /// </summary>
        public RequestType RequestType { get; }

        /// <summary>
        /// The new user identity for the session.
        /// </summary>
        public NodeId AuthenticationToken { get; }

        /// <summary>
        /// The identity to associate with the session-less request.
        /// </summary>
        public IUserIdentity Identity { get; set; }

        /// <summary>
        /// Set to indicate that an error occurred validating the session-less request and that it should be rejected.
        /// </summary>
        public ServiceResult Error { get; set; }
    }
}
