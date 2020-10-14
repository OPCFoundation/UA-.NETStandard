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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores information used while a thread is completing an operation on behalf of a client.
    /// </summary>
    public class OperationContext : IOperationContext
    {
        #region Constructors
        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="identity">The identity used in the request.</param>
        public OperationContext(RequestHeader requestHeader, RequestType requestType, IUserIdentity identity = null)
        {
            if (requestHeader == null) throw new ArgumentNullException(nameof(requestHeader));
            
            m_channelContext    = SecureChannelContext.Current;
            m_session           = null;
            m_identity          = identity;
            m_preferredLocales  = new string[0];
            m_diagnosticsMask   = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            m_stringTable       = new StringTable();
            m_auditLogEntryId   = requestHeader.AuditEntryId;
            m_requestId         = Utils.IncrementIdentifier(ref s_lastRequestId);
            m_requestType       = requestType;
            m_clientHandle      = requestHeader.RequestHandle;
            m_operationDeadline = DateTime.MaxValue;

            if (requestHeader.TimeoutHint > 0)
            {
                m_operationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="session">The session.</param>
        public OperationContext(RequestHeader requestHeader, RequestType requestType, Session session)
        {
            if (requestHeader == null) throw new ArgumentNullException(nameof(requestHeader));
            if (session == null)       throw new ArgumentNullException(nameof(session));
            
            m_channelContext     = SecureChannelContext.Current;
            m_session            = session;
            m_identity           = session.EffectiveIdentity;
            m_preferredLocales   = session.PreferredLocales;
            m_diagnosticsMask    = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            m_stringTable        = new StringTable();
            m_auditLogEntryId    = requestHeader.AuditEntryId;
            m_requestId          = Utils.IncrementIdentifier(ref s_lastRequestId);
            m_requestType        = requestType;
            m_clientHandle       = requestHeader.RequestHandle;
            m_operationDeadline  = DateTime.MaxValue;

            if (requestHeader.TimeoutHint > 0)
            {
                m_operationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        public OperationContext(Session session, DiagnosticsMasks diagnosticsMasks)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            
            m_channelContext    = null;
            m_session           = session;
            m_identity          = session.EffectiveIdentity;
            m_preferredLocales  = session.PreferredLocales;
            m_diagnosticsMask   = diagnosticsMasks;
            m_stringTable       = new StringTable();
            m_auditLogEntryId   = null;
            m_requestId         = 0;
            m_requestType       = RequestType.Unknown;
            m_clientHandle      = 0;
            m_operationDeadline = DateTime.MaxValue;
        }

        /// <summary>
        /// Initializes the context with a monitored item.
        /// </summary>
        /// <param name="monitoredItem">The monitored item.</param>
        public OperationContext(IMonitoredItem monitoredItem)
        {
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));
            
            m_channelContext = null;
            m_session = monitoredItem.Session;

            if (m_session != null)
            {
                m_identity = m_session.Identity;
                m_preferredLocales  = m_session.PreferredLocales;
            }                
                
            m_diagnosticsMask   = DiagnosticsMasks.SymbolicId;
            m_stringTable       = new StringTable();
            m_auditLogEntryId   = null;
            m_requestId         = 0;
            m_requestType       = RequestType.Unknown;
            m_clientHandle      = 0;
            m_operationDeadline = DateTime.MaxValue;
        }
        #endregion   
                
        #region Public Properties
        /// <summary>
        /// The context for the secure channel used to send the request.
        /// </summary>
        /// <value>The channel context.</value>
        public SecureChannelContext ChannelContext
        {
            get { return m_channelContext; }
        }

        /// <summary>
        /// The session associated with the context.
        /// </summary>
        /// <value>The session.</value>
        public Session Session
        {
            get { return m_session; }
        }

        /// <summary>
        /// The security policy used for the secure channel.
        /// </summary>
        /// <value>The security policy URI.</value>
        public string SecurityPolicyUri
        {
            get 
            { 
                if (m_channelContext != null && m_channelContext.EndpointDescription != null)
                {
                    return m_channelContext.EndpointDescription.SecurityPolicyUri;
                }

                return null;
            }
        }
        
        /// <summary>
        /// The type of request.
        /// </summary>
        /// <value>The type of the request.</value>
        public RequestType RequestType
        {
            get { return m_requestType; }
        }

        /// <summary>
        /// A unique identifier assigned to the request by the server.
        /// </summary>
        /// <value>The request id.</value>
        public uint RequestId
        {
            get { return m_requestId; }
        }

        /// <summary>
        /// The handle assigned by the client to the request.
        /// </summary>
        /// <value>The client handle.</value>
        public uint ClientHandle
        {
            get { return m_clientHandle; }
        }

        /// <summary>
        /// Updates the status code (thread safe).
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        public void SetStatusCode(StatusCode statusCode)
        {
            Interlocked.Exchange(ref m_operationStatus, (long)statusCode.Code);
        }
        #endregion

        #region IOperationContext Members
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId
        {
            get 
            { 
                if (m_session != null)
                {
                    return m_session.Id;
                }

                return null;
            }
        }

        /// <summary>
        /// The identity context to use when processing the request.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity
        {
            get { return m_identity; }
        }

        /// <summary>
        /// The locales to use for the operation.
        /// </summary>
        /// <value>The preferred locales.</value>
        public IList<string> PreferredLocales
        {
            get { return m_preferredLocales; }
        }

        /// <summary>
        /// The diagnostics mask specified with the request.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get { return m_diagnosticsMask; }
        }

        /// <summary>
        /// A table of diagnostics strings to return in the response.
        /// </summary>
        /// <value>The string table.</value>
        /// <remarks>
        /// This object is thread safe.
        /// </remarks>
        public StringTable StringTable
        {
            get { return m_stringTable; }
        }

        /// <summary>
        /// When the request times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline
        {
            get { return m_operationDeadline; }
        }

        /// <summary>
        /// The current status of the request (used to check for timeouts/client cancel requests).
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus
        {
            get { return (uint)m_operationStatus; }
        }

        /// <summary>
        /// The audit log entry id provided by the client which must be included in an audit events generated by the server.
        /// </summary>
        /// <value>The audit entry id.</value>
        public string AuditEntryId
        {
            get { return m_auditLogEntryId; }
        }
        #endregion

        #region Private Fields
        private SecureChannelContext m_channelContext;
        private Session m_session;
        private IUserIdentity m_identity;
        private IList<string> m_preferredLocales;
        private DiagnosticsMasks m_diagnosticsMask;
        private StringTable m_stringTable;
        private string m_auditLogEntryId;
        private uint m_requestId;        
        private RequestType m_requestType;
        private uint m_clientHandle;
        private DateTime m_operationDeadline;
        private long m_operationStatus;
        private static long s_lastRequestId;
        #endregion
    }
}
