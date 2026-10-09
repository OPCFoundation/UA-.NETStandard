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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores information used while a thread is completing an operation on behalf of a client.
    /// <para>
    /// A context created for a Client request owns that request's tracking scope, so disposing the
    /// context reports the request as completed. Contexts created for internal work, such as
    /// sampling a MonitoredItem, own no scope and disposing them does nothing.
    /// </para>
    /// </summary>
    public class OperationContext : ISessionOperationContext, IDisposable
    {
        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="requestLifetime">The request lifetime.</param>
        /// <param name="identity">The identity used in the request.</param>
        public OperationContext(
            RequestHeader requestHeader,
            SecureChannelContext? secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime,
            IUserIdentity? identity = null)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ChannelContext = secureChannelContext;
            Session = null!;
            UserIdentity = identity!;
            PreferredLocales = Array.Empty<string>();
            DiagnosticsMask = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            StringTable = new StringTable();
            AuditEntryId = requestHeader.AuditEntryId!;
            ClientTimestamp = requestHeader.Timestamp.IsNull
                ? DateTime.MinValue
                : (DateTime)requestHeader.Timestamp;
            RequestId = Utils.IncrementIdentifier(ref s_lastRequestId);
            RequestType = requestType;
            ClientHandle = requestHeader.RequestHandle;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = requestLifetime ?? RequestLifetime.None;

            if (requestHeader.TimeoutHint > 0)
            {
                OperationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="requestLifetime">The request lifetime.</param>
        /// <param name="session">The session.</param>
        public OperationContext(
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime,
            ISession session)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ChannelContext = secureChannelContext;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            UserIdentity = session.EffectiveIdentity;
            PreferredLocales = session.PreferredLocales;
            DiagnosticsMask = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            StringTable = new StringTable();
            AuditEntryId = requestHeader.AuditEntryId!;
            ClientTimestamp = requestHeader.Timestamp.IsNull
                ? DateTime.MinValue
                : (DateTime)requestHeader.Timestamp;
            RequestId = Utils.IncrementIdentifier(ref s_lastRequestId);
            RequestType = requestType;
            ClientHandle = requestHeader.RequestHandle;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = requestLifetime ?? RequestLifetime.None;

            if (requestHeader.TimeoutHint > 0)
            {
                OperationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        public OperationContext(ISession session, DiagnosticsMasks diagnosticsMasks)
        {
            ChannelContext = null!;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            UserIdentity = session.EffectiveIdentity;
            PreferredLocales = session.PreferredLocales;
            DiagnosticsMask = diagnosticsMasks;
            StringTable = new StringTable();
            AuditEntryId = null!;
            RequestId = 0;
            RequestType = RequestType.Unknown;
            ClientHandle = 0;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = RequestLifetime.None;
        }

        /// <summary>
        /// Initializes the context with a monitored item.
        /// </summary>
        /// <remarks>
        /// The identity is the Session's <see cref="ISession.EffectiveIdentity"/>, the
        /// one the Roles granted by <see cref="IRoleManager.ResolveGrantedRoles"/> were
        /// layered onto, and not <see cref="ISession.Identity"/>, which is the token as
        /// it arrived. Permission checks made from a monitored item - most visibly the
        /// Part 3 8.55 ReceiveEvents check on an event's EventType and SourceNode - have
        /// to see the Roles the Session actually holds.
        /// </remarks>
        /// <param name="monitoredItem">The monitored item.</param>
        public OperationContext(IMonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            ChannelContext = null!;
            UserIdentity = monitoredItem.EffectiveIdentity;
            Session = monitoredItem.Session;

            if (Session != null)
            {
                UserIdentity = Session.EffectiveIdentity;
                PreferredLocales = Session.PreferredLocales;
            }

            DiagnosticsMask = DiagnosticsMasks.SymbolicId;
            StringTable = new StringTable();
            AuditEntryId = null!;
            RequestId = 0;
            RequestType = RequestType.Unknown;
            ClientHandle = 0;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = RequestLifetime.None;
        }

        /// <summary>
        /// The context for the secure channel used to send the request.
        /// </summary>
        /// <value>The channel context.</value>
        public SecureChannelContext? ChannelContext { get; }

        /// <summary>
        /// The session associated with the context.
        /// </summary>
        /// <value>The session.</value>
        public ISession Session { get; } = null!;

        /// <summary>
        /// The lifetime of the request.
        /// This object is used to track the lifetime of the request and to trigger cancellation if the client aborts the request or if the request times out.
        /// </summary>
        public RequestLifetime RequestLifetime { get; }

        /// <summary>
        /// The security policy used for the secure channel.
        /// </summary>
        /// <value>The security policy URI.</value>
        public string SecurityPolicyUri
        {
            get
            {
                if (ChannelContext != null && ChannelContext.EndpointDescription != null)
                {
                    return ChannelContext.EndpointDescription.SecurityPolicyUri!;
                }

                return null!;
            }
        }

        /// <summary>
        /// The type of request.
        /// </summary>
        /// <value>The type of the request.</value>
        public RequestType RequestType { get; }

        /// <summary>
        /// A unique identifier assigned to the request by the server.
        /// </summary>
        /// <value>The request id.</value>
        public uint RequestId { get; }

        /// <summary>
        /// The handle assigned by the client to the request.
        /// </summary>
        /// <value>The client handle.</value>
        public uint ClientHandle { get; }

        /// <summary>
        /// The time the client sent the request (RequestHeader.Timestamp), or
        /// <see cref="DateTime.MinValue"/> when unknown.
        /// </summary>
        internal DateTime ClientTimestamp { get; }

        /// <summary>
        /// The id of the Session an ActivateSession request is activating, or null.
        /// </summary>
        /// <remarks>
        /// An ActivateSession request runs without a Session context (its Session is not
        /// activated yet, so <see cref="Session"/> and <see cref="SessionId"/> stay null), but a
        /// close of the Session it targets still has to abort it like any other outstanding
        /// request of that Session (OPC 10000-4 5.7.2.1). It is assigned before the request is
        /// registered with the request manager and never changes afterwards.
        /// </remarks>
        internal NodeId ActivationTargetSessionId { get; set; }

        /// <summary>
        /// Whether a CreateSession request's client certificate is kept although an
        /// <see cref="StandardServer.OnApplicationCertificateError"/> override accepted a
        /// validation error for it.
        /// </summary>
        /// <remarks>
        /// Assigned before the session manager creates the Session, so the Session is
        /// marked as not validated (<see cref="ClientCertificateProvenance"/>) before it is
        /// published to other requests, to role evaluation or to a session mirror.
        /// </remarks>
        internal bool ClientCertificateErrorAccepted { get; set; }

        /// <summary>
        /// Updates the status code (thread safe).
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        [Obsolete("Use RequestLifetime.TryCancel instead to update the status code and trigger cancellation if needed.")]
        public void SetStatusCode(StatusCode statusCode)
        {
            RequestLifetime.TryCancel(statusCode);
        }

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId
        {
            get
            {
                if (Session != null)
                {
                    return Session.Id;
                }

                return default;
            }
        }

        /// <summary>
        /// The identity context to use when processing the request.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity { get; }

        /// <summary>
        /// The locales to use for the operation.
        /// </summary>
        /// <value>The preferred locales.</value>
        public ArrayOf<string> PreferredLocales { get; }

        /// <summary>
        /// The diagnostics mask specified with the request.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask { get; }

        /// <summary>
        /// A table of diagnostics strings to return in the response.
        /// </summary>
        /// <value>The string table.</value>
        /// <remarks>
        /// This object is thread safe.
        /// </remarks>
        public StringTable StringTable { get; }

        /// <summary>
        /// When the request times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline { get; }

        /// <summary>
        /// Gets the token that will be cancelled when the request is aborted.
        /// </summary>
        public System.Threading.CancellationToken CancellationToken => RequestLifetime?.CancellationToken ?? System.Threading.CancellationToken.None;

        /// <summary>
        /// The current status of the request (used to check for timeouts/client cancel requests).
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus => RequestLifetime.StatusCode;

        /// <summary>
        /// The audit log entry id provided by the client which must be included in an audit events generated by the server.
        /// </summary>
        /// <value>The audit entry id.</value>
        public string AuditEntryId { get; } = null!;

        /// <summary>
        /// Reports the request as completed and releases any lifecycle operation waiting for it.
        /// Disposing a context that does not track a request does nothing, and disposing the same
        /// context twice is safe.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable method to dispose of resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    System.Threading.Interlocked.Exchange(ref m_requestScope, null)?.Dispose();
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref m_sessionlessLease, null)?.Dispose();
                }
            }
        }

        /// <summary>
        /// Attaches the scope that tracks this request while it executes, so that disposing the
        /// context completes the request.
        /// </summary>
        /// <param name="requestScope">The scope that tracks the request.</param>
        internal void AttachRequestScope(IDisposable requestScope)
        {
            m_requestScope = requestScope;
        }

        /// <summary>
        /// Attaches the capacity a Session-less request holds in the Session-less request budget, so
        /// that disposing the context returns it however the request ends.
        /// </summary>
        /// <param name="lease">The lease to dispose with the context.</param>
        internal void AttachSessionlessLease(IDisposable lease)
        {
            m_sessionlessLease = lease;
        }

        /// <summary>
        /// Returns a copy of a node manager's system context bound to this operation,
        /// reusing the copy made for the previous call with the same template.
        /// </summary>
        /// <remarks>
        /// Per-node node manager callbacks (permission and node metadata, reference
        /// descriptions) otherwise clone the system context once for every node of a
        /// request. The copies are equal for one request, so the last one is kept.
        /// </remarks>
        /// <param name="template">The node manager's system context.</param>
        internal ServerSystemContext GetSystemContext(ServerSystemContext template)
        {
            SystemContextCacheEntry? entry = m_systemContextCache;
            if (entry != null && ReferenceEquals(entry.Template, template))
            {
                return entry.Copy;
            }

            ServerSystemContext copy = template.Copy(this);
            m_systemContextCache = new SystemContextCacheEntry(template, copy);
            return copy;
        }

        /// <summary>
        /// A system context copy and the template it was made from, published as one
        /// reference so concurrent readers never pair a copy with the wrong template.
        /// </summary>
        private sealed class SystemContextCacheEntry
        {
            public SystemContextCacheEntry(ServerSystemContext template, ServerSystemContext copy)
            {
                Template = template;
                Copy = copy;
            }

            public ServerSystemContext Template { get; }
            public ServerSystemContext Copy { get; }
        }

        private IDisposable? m_requestScope;
        private IDisposable? m_sessionlessLease;
        private SystemContextCacheEntry? m_systemContextCache;
        private static uint s_lastRequestId;
    }
}
