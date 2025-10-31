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

#if MOVE
        /// <summary>
        /// The identity of the user (null if not available).
        /// </summary>
        /// <value>The user identity.</value>
        IUserIdentity UserIdentity { get; }
#endif

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
    /// A generic implementation for ISystemContext interface.
    /// </summary>
    public class SystemContext : ISystemContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SystemContext"/> class.
        /// </summary>
        public SystemContext(ITelemetryContext telemetry)
        {
            Telemetry = telemetry;
            NodeStateFactory = new NodeStateFactory();
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
                return m_sessionId;
            }
            set => m_sessionId = value;
        }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public IList<string> PreferredLocales
        {
            get
            {
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
        /// The diagnostics mask associated with the operation.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get
            {
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
                return StatusCodes.Good;
            }
        }

        private NodeId m_sessionId;
        private IList<string> m_preferredLocales;
        private string m_auditEntryId;
    }
}
