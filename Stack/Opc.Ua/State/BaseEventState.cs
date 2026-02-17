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
            LocalizedText message)
        {
            m_eventId = new PropertyState<ByteString>(this)
            {
                Value = Uuid.NewUuid().ToByteString()
            };

            m_eventType = new PropertyState<NodeId>(this)
            {
                Value = GetDefaultTypeDefinitionId(context.NamespaceUris)
            };

            TypeDefinitionId = m_eventType.Value;

            if (source != null)
            {
                if (!source.NodeId.IsNull)
                {
                    m_sourceNode = new PropertyState<NodeId>(this)
                    {
                        Value = source.NodeId,
                        RolePermissions = source.RolePermissions,
                        UserRolePermissions = source.UserRolePermissions,
                        NodeId = source.NodeId
                    };
                }

                if (!source.BrowseName.IsNull)
                {
                    m_sourceName = new PropertyState<string>(this)
                    {
                        Value = source.BrowseName.Name
                    };
                }
            }

            m_time = new PropertyState<DateTime>(this) { Value = DateTime.UtcNow };

            m_receiveTime = new PropertyState<DateTime>(this) { Value = DateTime.UtcNow };

            m_severity = new PropertyState<ushort>(this) { Value = (ushort)severity };

            m_message = new PropertyState<LocalizedText>(this) { Value = message };
        }
    }

    /// <summary>
    /// The severity for an event.
    /// </summary>
    /// <remarks>
    /// Event severities can have any value between 1 and 1000. This enumeration provides default values.
    /// </remarks>
    public enum EventSeverity
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
        Max = 1000
    }
}
