/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Reflection;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class BaseEventState
    {
        #region Initialization
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
            m_eventId = new PropertyState<byte[]>(this);
            m_eventId.Value = Guid.NewGuid().ToByteArray();

            m_eventType = new PropertyState<NodeId>(this);
            m_eventType.Value = GetDefaultTypeDefinitionId(context.NamespaceUris);

            TypeDefinitionId = m_eventType.Value;

            if (source != null)
            {
                if (!NodeId.IsNull(source.NodeId))
                {
                    m_sourceNode = new PropertyState<NodeId>(this);
                    m_sourceNode.Value = source.NodeId;
                }

                if (!QualifiedName.IsNull(source.BrowseName))
                {
                    m_sourceName = new PropertyState<string>(this);
                    m_sourceName.Value = source.BrowseName.Name;
                }
            }

            m_time = new PropertyState<DateTime>(this);
            m_time.Value = DateTime.UtcNow;

            m_receiveTime = new PropertyState<DateTime>(this);
            m_receiveTime.Value = DateTime.UtcNow;

            m_severity = new PropertyState<ushort>(this);
            m_severity.Value = (ushort)severity;

            m_message = new PropertyState<LocalizedText>(this);
            m_message.Value = message;
        }
        #endregion
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
        /// The highest possible severity.
        /// </summary>
        Max = 1000,

        /// <summary>
        /// The event has high severity.
        /// </summary>
        High = 900,

        /// <summary>
        /// The event has medium high severity.
        /// </summary>
        MediumHigh = 700,

        /// <summary>
        /// The event has medium severity.
        /// </summary>
        Medium = 500,

        /// <summary>
        /// The event has medium-low severity.
        /// </summary>
        MediumLow = 300,

        /// <summary>
        /// The event has low severity.
        /// </summary>
        Low = 100,

        /// <summary>
        /// The lowest possible severity.
        /// </summary>
        Min = 1
    }
}
