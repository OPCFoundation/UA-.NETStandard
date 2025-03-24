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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A subscription in a format to be persited by an <see cref="ISubscriptionStore"/>
    /// </summary>
    public interface IStoredSubscription
    {
        /// <summary>
        /// The Id of the subscription
        /// </summary>
        uint Id { get; set; }
        /// <summary>
        /// If the subscription is a durable susbscrition
        /// </summary>
        bool IsDurable { get; set; }
        /// <summary>
        /// The lifetime counter 
        /// </summary>
        uint LifetimeCounter { get; set; }
        /// <summary>
        /// The max lifetime count
        /// </summary>
        uint MaxLifetimeCount { get; set; }
        /// <summary>
        /// the max keepalive count
        /// </summary>
        uint MaxKeepaliveCount { get; set; }
        /// <summary>
        /// The max message count
        /// </summary>
        uint MaxMessageCount { get; set; }
        /// <summary>
        /// The max notifications being sent to a client in a single publish message
        /// </summary>
        uint MaxNotificationsPerPublish { get; set; }
        /// <summary>
        /// The monitored items being owned by the subscription
        /// </summary>
        IEnumerable<IStoredMonitoredItem> MonitoredItems { get; set; }
        /// <summary>
        /// The priority of the subscription
        /// </summary>
        byte Priority { get; set; }
        /// <summary>
        /// The publishing interval
        /// </summary>
        double PublishingInterval { get; set; }
        /// <summary>
        /// The last messages sent to the client / queued for sending
        /// </summary>
        List<NotificationMessage> SentMessages { get; set; }
        /// <summary>
        /// The last message sent by the subscription
        /// </summary>
        int LastSentMessage { get; set; }
        /// <summary>
        /// The sequence number
        /// </summary>
        long SequenceNumber { get; set; }
        /// <summary>
        /// The user identity of the subscription
        /// </summary>
        UserIdentityToken UserIdentityToken { get; set; }
    }
}
