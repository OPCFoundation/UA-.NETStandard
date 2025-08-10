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

namespace Opc.Ua
{
    public partial class BaseEventState
    {
        /// <summary>
        /// Initializes a new event.
        /// </summary>
        /// <param name="context">The current system context.</param>
        /// <param name="source">The source of the event.</param>
        /// <param name="severity">The severity for the event.</param>
        /// <param name="message">The default message.</param>
        public virtual void Initialize(
            ISystemContext context,
            NodeState source,
            EventSeverity severity,
            LocalizedText message
        )
        {
            m_eventId = new PropertyState<byte[]>(this) { Value = Guid.NewGuid().ToByteArray() };

            m_eventType = new PropertyState<NodeId>(this) { Value = GetDefaultTypeDefinitionId(context.NamespaceUris) };

            TypeDefinitionId = m_eventType.Value;

            if (source != null)
            {
                if (!NodeId.IsNull(source.NodeId))
                {
                    m_sourceNode = new PropertyState<NodeId>(this)
                    {
                        Value = source.NodeId,
                        RolePermissions = source.RolePermissions,
                        UserRolePermissions = source.UserRolePermissions,
                        NodeId = source.NodeId,
                    };
                }

                if (!QualifiedName.IsNull(source.BrowseName))
                {
                    m_sourceName = new PropertyState<string>(this) { Value = source.BrowseName.Name };
                }
            }

            m_time = new PropertyState<DateTime>(this) { Value = DateTime.UtcNow };

            m_receiveTime = new PropertyState<DateTime>(this) { Value = DateTime.UtcNow };

            m_severity = new PropertyState<ushort>(this) { Value = (ushort)severity };

            m_message = new PropertyState<LocalizedText>(this) { Value = message };
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a copy of the node and all children.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            var clone = (BaseEventState)Activator.CreateInstance(GetType());
            return CloneChildren(clone);
        }
    }

    /// <summary>
    /// The severity for an event.
    /// </summary>
    /// <remarks>
    /// Event severities can have any value between 1 and 1000. This enumeration provides default values.
    /// </remarks>
    public enum EventSeverity : int
    {
        /// <summary>
        /// The lowest possible severity.
        /// </summary>
        Min = 1,

        /// <summary>
        /// The event has low severity.
        /// </summary>
        Low = 100,

        /// <summary>
        /// The event has medium-low severity.
        /// </summary>
        MediumLow = 300,

        /// <summary>
        /// The event has medium severity.
        /// </summary>
        Medium = 500,

        /// <summary>
        /// The event has medium high severity.
        /// </summary>
        MediumHigh = 700,

        /// <summary>
        /// The event has high severity.
        /// </summary>
        High = 900,

        /// <summary>
        /// The highest possible severity.
        /// </summary>
        Max = 1000,
    }
}
