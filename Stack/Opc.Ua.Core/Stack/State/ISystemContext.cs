/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes how access the system containing the data.
    /// </summary>
    public interface ISystemContext
    {
        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        object SystemHandle { get; }

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

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        IList<string> PreferredLocales { get; }

        /// <summary>
        /// The audit log entry associated with the operation (null if not available).
        /// </summary>
        /// <value>The audit entry identifier.</value>
        string AuditEntryId { get; }

        /// <summary>
        /// The table of namespace uris to use when accessing the system.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of server uris to use when accessing the system.
        /// </summary>
        /// <value>The server URIs.</value>
        StringTable ServerUris { get; }

        /// <summary>
        /// A table containing the types that are to be used when accessing the system.
        /// </summary>
        /// <value>The type table.</value>
        ITypeTable TypeTable { get; }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        IEncodeableFactory EncodeableFactory { get; }

        /// <summary>
        /// A factory that can be used to create node ids.
        /// </summary>
        /// <value>The node identifiers factory.</value>
        INodeIdFactory NodeIdFactory { get; }

        /// <summary>
        /// A factory that can be used to create encodeable types.
        /// </summary>
        /// <value>The encodeable factory.</value>
        NodeStateFactory NodeStateFactory { get; }

        /// <summary>
        /// Telemetry context for logging and tracing in the system
        /// </summary>
        ITelemetryContext Telemetry { get; }
    }

    /// <summary>
    /// An interface that can be used to create new node ids.
    /// </summary>
    public interface INodeIdFactory
    {
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>The new NodeId.</returns>
        NodeId New(ISystemContext context, NodeState node);
    }

    /// <summary>
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class SystemContext : ISystemContext, IOperationContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        public SystemContext(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory(telemetry);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="telemetry"></param>
        public SystemContext(IOperationContext context, ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory(telemetry);
            OperationContext = context;
        }

        /// <inheritdoc/>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// An application defined handle for the system.
        /// </summary>
        /// <value>The system handle.</value>
        public object SystemHandle { get; set; }

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        public NodeId SessionId
        {
            get
            {
                if (OperationContext != null)
                {
                    return OperationContext.SessionId;
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
                if (OperationContext != null)
                {
                    return OperationContext.UserIdentity;
                }

                return m_userIdentity;
            }
            set => m_userIdentity = value;
        }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public IList<string> PreferredLocales
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
            var copy = (SystemContext)MemberwiseClone();

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
        private IList<string> m_preferredLocales;
        private string m_auditEntryId;
        private IUserIdentity m_userIdentity;
    }
}
