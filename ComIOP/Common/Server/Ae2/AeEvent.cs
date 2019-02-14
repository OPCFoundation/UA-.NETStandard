/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Stores an event received from the UA server.
    /// </summary>
    public class AeEvent
    {
        /// <summary>
        /// A number assigned by the proxy to the event when it arrives.
        /// </summary>
        public int Cookie { get; set; }

        /// <summary>
        /// The event id.
        /// </summary>
        public byte[] EventId { get; set; }

        /// <summary>
        /// The event type.
        /// </summary>
        public NodeId EventType { get; set; }

        /// <summary>
        /// The event source.
        /// </summary>
        public string SourceName { get; set; }

        /// <summary>
        /// The event time.
        /// </summary>
        public DateTime Time { get; set; }

        /// <summary>
        /// The event message.
        /// </summary>
        public LocalizedText Message { get; set; }

        /// <summary>
        /// The event severity.
        /// </summary>
        public ushort Severity { get; set; }

        /// <summary>
        /// The user that triggered the audit event.
        /// </summary>
        public string AuditUserId { get; set; }

        /// <summary>
        /// The NodeId of the condition (used for acknowledging).
        /// </summary>
        public NodeId ConditionId { get; set; }

        /// <summary>
        /// The condition branch which the event belongs to.
        /// </summary>
        public NodeId BranchId { get; set; }

        /// <summary>
        /// The name of the condition.
        /// </summary>
        public string ConditionName { get; set; }

        /// <summary>
        /// The last comment.
        /// </summary>
        public LocalizedText Comment { get; set; }

        /// <summary>
        /// The user that added the comment.
        /// </summary>
        public string CommentUserId { get; set; }

        /// <summary>
        /// The qualilty of the underlying data source.
        /// </summary>
        public StatusCode Quality { get; set; }

        /// <summary>
        /// The current Enabled state (Conditions).
        /// </summary>
        public bool EnabledState { get; set; }

        /// <summary>
        /// The current Acknowledged state (Conditions).
        /// </summary>
        public bool AckedState { get; set; }

        /// <summary>
        /// The current Active state (Alarms).
        /// </summary>
        public bool ActiveState { get; set; }

        /// <summary>
        /// When the condition transitioned into the Active state (Alarms).
        /// </summary>
        public DateTime ActiveTime { get; set; }

        /// <summary>
        /// The current Limit state (ExclusiveLimitConditions).
        /// </summary>
        public LocalizedText LimitState { get; set; }

        /// <summary>
        /// The current HighHigh state (NonExclusiveLimitConditions).
        /// </summary>
        public LocalizedText HighHighState { get; set; }

        /// <summary>
        /// The current High state (NonExclusiveLimitConditions).
        /// </summary>
        public LocalizedText HighState { get; set; }

        /// <summary>
        /// The current Low state (NonExclusiveLimitConditions).
        /// </summary>
        public LocalizedText LowState { get; set; }

        /// <summary>
        /// The current LowLow state (NonExclusiveLimitConditions).
        /// </summary>
        public LocalizedText LowLowState { get; set; }

        /// <summary>
        /// The category for the event type.
        /// </summary>
        public AeEventCategory Category { get; set; }

        /// <summary>
        /// The attribute values requested for the category.
        /// </summary>
        public object[] AttributeValues { get; set; }
    }
}
