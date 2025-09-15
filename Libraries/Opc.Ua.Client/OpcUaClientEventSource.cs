/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.Tracing;

namespace Opc.Ua.Client
{
    /// <summary>
    /// EventSource for client.
    /// </summary>
    public static partial class CoreClientUtils
    {
        /// <summary>
        /// The EventSource log interface.
        /// </summary>
        internal static OpcUaClientEventSource EventLog { get; } = new OpcUaClientEventSource();
    }

    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Client", Guid = "8CFA469E-18C6-480F-9B74-B005DACDE3D3")]
    internal class OpcUaClientEventSource : EventSource
    {
        internal const int SubscriptionStateId = 1;
        internal const int NotificationId = SubscriptionStateId + 1;
        internal const int NotificationReceivedId = NotificationId + 1;
        internal const int PublishStartId = NotificationReceivedId + 1;
        internal const int PublishStopId = PublishStartId + 1;

        /// <summary>
        /// The state of the client subscription.
        /// </summary>
        [Event(
            SubscriptionStateId,
            Message = "Subscription {0}, Id={1}, LastNotificationTime={2:HH:mm:ss}, " +
                "GoodPublishRequestCount={3}, PublishingInterval={4}, KeepAliveCount={5}, " +
                "PublishingEnabled={6}, MonitoredItemCount={7}",
            Level = EventLevel.Verbose)]
        public void SubscriptionState(
            string context,
            uint id,
            DateTime lastNotificationTime,
            int goodPublishRequestCount,
            double currentPublishingInterval,
            uint currentKeepAliveCount,
            bool currentPublishingEnabled,
            uint monitoredItemCount)
        {
            WriteEvent(
                SubscriptionStateId,
                context,
                id,
                lastNotificationTime,
                goodPublishRequestCount,
                currentPublishingInterval,
                currentKeepAliveCount,
                currentPublishingEnabled,
                monitoredItemCount);
        }

        /// <summary>
        /// The notification message. Called internally to convert wrapped value.
        /// </summary>
        [Event(
            NotificationId,
            Message = "Notification: ClientHandle={0}, Value={1}",
            Level = EventLevel.Verbose)]
        public void Notification(int clientHandle, Variant value)
        {
            // expensive operation, only enable if tracemask set
            if ((Utils.TraceMask & Utils.TraceMasks.OperationDetail) != 0)
            {
                WriteEvent(NotificationId, clientHandle, value.ToString());
            }
        }

        /// <summary>
        /// A notification received in Publish complete.
        /// </summary>
        [Event(
            NotificationReceivedId,
            Message = "NOTIFICATION RECEIVED: SubId={0}, SeqNo={1}",
            Level = EventLevel.Verbose)]
        public void NotificationReceived(int subscriptionId, int sequenceNumber)
        {
            WriteEvent(NotificationReceivedId, subscriptionId, sequenceNumber);
        }

        /// <summary>
        /// A Publish begin received.
        /// </summary>
        [Event(
            PublishStartId,
            Message = "PUBLISH #{0} SENT",
            Level = EventLevel.Verbose)]
        public void PublishStart(int requestHandle)
        {
            WriteEvent(PublishStartId, requestHandle);
        }

        /// <summary>
        /// A Publish complete received.
        /// </summary>
        [Event(
            PublishStopId,
            Message = "PUBLISH #{0} RECEIVED",
            Level = EventLevel.Verbose)]
        public void PublishStop(int requestHandle)
        {
            WriteEvent(PublishStopId, requestHandle);
        }
    }
}
