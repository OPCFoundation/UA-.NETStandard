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
    public partial class AuditEventState
    {
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

            m_status = new PropertyState<bool>(this) { Value = status };

            if (actionTimestamp != DateTime.MinValue)
            {
                m_actionTimeStamp = new PropertyState<DateTime>(this) { Value = actionTimestamp };
            }

            if (context.NamespaceUris != null)
            {
                m_serverId = new PropertyState<string>(this)
                {
                    Value = context.NamespaceUris.GetString(1)
                };
            }

            if (context.AuditEntryId != null)
            {
                m_clientAuditEntryId = new PropertyState<string>(this)
                {
                    Value = context.AuditEntryId
                };
            }

            if (context.UserId != null)
            {
                m_clientUserId = new PropertyState<string>(this)
                {
                    Value = context.UserId
                };
            }
        }
    }
}
