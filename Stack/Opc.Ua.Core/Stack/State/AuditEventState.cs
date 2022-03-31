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
using System.Xml;
using System.IO;
using System.Reflection;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class AuditEventState
    {
        #region Initialization
        /// <summary>
        /// Initializes a new event.
        /// </summary>
        /// <param name="context">The current system context.</param>
        /// <param name="source">The source of the event.</param>
        /// <param name="severity">The severity for the event.</param>
        /// <param name="message">The default message.</param>
        /// <param name="status">Whether the operation that caused the event succeeded.</param>
        /// <param name="actionTimestamp">When the operation started.</param>
        public virtual void Initialize(
            ISystemContext context, 
            NodeState source, 
            EventSeverity severity,
            LocalizedText message,
            bool status,
            DateTime actionTimestamp)
        {
            base.Initialize(context, source, severity, message);

            m_status = new PropertyState<bool>(this);
            m_status.Value = status;

            if (actionTimestamp != DateTime.MinValue)
            {
                m_actionTimeStamp = new PropertyState<DateTime>(this);
                m_actionTimeStamp.Value = actionTimestamp;
            }

            if (context.NamespaceUris != null)
            {
                m_serverId = new PropertyState<string>(this);
                m_serverId.Value = context.NamespaceUris.GetString(1);
            }

            if (context.AuditEntryId != null)
            {
                m_clientAuditEntryId = new PropertyState<string>(this);
                m_clientAuditEntryId.Value = context.AuditEntryId;
            }

            if (context.UserIdentity != null)
            {
                m_clientUserId = new PropertyState<string>(this);
                m_clientUserId.Value = context.UserIdentity.DisplayName;
            }
        }
        #endregion
    }
}
