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
