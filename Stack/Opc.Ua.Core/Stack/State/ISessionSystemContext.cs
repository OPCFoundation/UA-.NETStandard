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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes how access the system containing the data.
    /// </summary>
    public interface ISessionSystemContext : ISystemContext
    {
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        NodeId SessionId { get; }

        /// <summary>
        /// The identity of the user (null if not available).
        /// </summary>
        /// <value>The user identity.</value>
        IUserIdentity UserIdentity { get; }
    }

    /// <summary>
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class SessionSystemContext : ISessionSystemContext, ISessionOperationContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SessionSystemContext"/> class.
        /// </summary>
        public SessionSystemContext(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionSystemContext"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public SessionSystemContext(IOperationContext context, ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory();
            OperationContext = context;
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        public object SystemHandle { get; set; }

        /// <inheritdoc/>
        public string UserId => UserIdentity?.DisplayName;

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        public NodeId SessionId
        {
            get
            {
                if (OperationContext is ISessionOperationContext session)
                {
                    return session.SessionId;
                }

                return m_sessionId;
            }
            set => m_sessionId = value;
        }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity
        {
            get
            {
                if (OperationContext is ISessionOperationContext session)
                {
                    return session.UserIdentity;
                }

                return m_userIdentity;
            }
            set => m_userIdentity = value;
        }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public ArrayOf<string> PreferredLocales
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.PreferredLocales;
                }

                return m_preferredLocales;
            }
            set => m_preferredLocales = value;
        }

        /// <summary>
        /// The audit log entry associated with the operation (null if not available).
        /// </summary>
        /// <value>The audit entry identifier.</value>
        public string AuditEntryId
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.AuditEntryId;
                }

                return m_auditEntryId;
            }
            set => m_auditEntryId = value;
        }

        /// <summary>
        /// The table of namespace uris to use when accessing the system.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris { get; set; }

        /// <summary>
        /// The table of server uris to use when accessing the system.
        /// </summary>
        /// <value>The server URIs.</value>
        public StringTable ServerUris { get; set; }

        /// <summary>
        /// A table containing the types that are to be used when accessing the system.
        /// </summary>
        /// <value>The type table.</value>
        public ITypeTable TypeTable { get; set; }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        public IEncodeableFactory EncodeableFactory { get; set; }

        /// <summary>
        /// A factory that can be used to create node instances.
        /// </summary>
        /// <value>The node state factory.</value>
        public NodeStateFactory NodeStateFactory { get; set; }

        /// <summary>
        /// A factory that can be used to create node ids.
        /// </summary>
        /// <value>The node identifiers factory.</value>
        public INodeIdFactory NodeIdFactory { get; set; }

        /// <summary>
        /// The operation context associated with the system context.
        /// </summary>
        /// <value>The operation context.</value>
        public IOperationContext OperationContext { get; protected set; }

        /// <summary>
        /// Creates a copy of the context that can be used with the specified operation context.
        /// </summary>
        /// <param name="context">The operation context to use.</param>
        /// <returns>
        /// A copy of the system context that references the new operation context.
        /// </returns>
        public ISystemContext Copy(IOperationContext context)
        {
            var copy = (SessionSystemContext)MemberwiseClone();

            if (context != null)
            {
                copy.OperationContext = context;
            }

            return copy;
        }

        /// <summary>
        /// The diagnostics mask associated with the operation.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.DiagnosticsMask;
                }

                return DiagnosticsMasks.None;
            }
        }

        /// <summary>
        /// The table of strings associated with the operation.
        /// </summary>
        /// <value>The string table.</value>
        public StringTable StringTable
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.StringTable;
                }

                return null;
            }
        }

        /// <summary>
        /// When the operation will be abandoned if it has not completed.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.OperationDeadline;
                }

                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// The current status of the operation.
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.OperationStatus;
                }

                return StatusCodes.Good;
            }
        }

        private NodeId m_sessionId;
        private ArrayOf<string> m_preferredLocales;
        private string m_auditEntryId;
        private IUserIdentity m_userIdentity;
    }
}
