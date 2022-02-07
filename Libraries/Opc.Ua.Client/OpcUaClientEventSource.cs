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
using Microsoft.Extensions.Logging;
using static Opc.Ua.Utils;

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
        private const int SubscriptionStateId = 1;
        private const int NotificationId = SubscriptionStateId + 1;
        private const int NotificationReceivedId = NotificationId + 1;
        private const int PublishStartId = NotificationId + 1;
        private const int PublishStopId = PublishStartId + 1;

        /// <summary>
        /// The client messages.
        /// </summary>
        private const string SubscriptionStateMessage = "Subscription {0}, Id={1}, LastNotificationTime={2:HH:mm:ss}, GoodPublishRequestCount={3}, PublishingInterval={4}, KeepAliveCount={5}, PublishingEnabled={6}, MonitoredItemCount={7}";
        private const string NotificationMessage = "Notification: ClientHandle={0}, Value={1}";
        private const string NotificationReceivedMessage = "NOTIFICATION RECEIVED: SubId={0}, SeqNo={1}";
        private const string PublishStartMessage = "PUBLISH #{0} SENT";
        private const string PublishStopMessage = "PUBLISH #{0} RECEIVED";

        /// <summary>
        /// The Client Event Ids used for event messages, when calling ILogger.
        /// </summary>
        private readonly EventId SubscriptionStateMessageEventId = new EventId(TraceMasks.Operation, nameof(SubscriptionState));
        private readonly EventId NotificationEventId = new EventId(TraceMasks.Operation, nameof(Notification));
        private readonly EventId NotificationReceivedEventId = new EventId(TraceMasks.Operation, nameof(NotificationReceived));
        private readonly EventId PublishStartEventId = new EventId(TraceMasks.ServiceDetail, nameof(PublishStart));
        private readonly EventId PublishStopEventId = new EventId(TraceMasks.ServiceDetail, nameof(PublishStop));

        /// <summary>
        /// The state of the client subscription.
        /// </summary>
        [Event(SubscriptionStateId, Message = SubscriptionStateMessage, Level = EventLevel.Verbose)]
        public void SubscriptionState(string context, uint id, DateTime lastNotificationTime, int goodPublishRequestCount,
            double currentPublishingInterval, uint currentKeepAliveCount, bool currentPublishingEnabled, uint monitoredItemCount)
        {
            if (IsEnabled())
            {
                WriteEvent(SubscriptionStateId, context, id, lastNotificationTime, goodPublishRequestCount,
                    currentPublishingInterval, currentKeepAliveCount, currentPublishingEnabled, monitoredItemCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Information))
            {
                Utils.LogInfo(SubscriptionStateMessageEventId, SubscriptionStateMessage,
                    context, id, lastNotificationTime, goodPublishRequestCount,
                    currentPublishingInterval, currentKeepAliveCount, currentPublishingEnabled, monitoredItemCount);
            }
        }

        /// <summary>
        /// The notification message. Called internally to convert wrapped value.
        /// </summary>
        [Event(NotificationId, Message = NotificationMessage, Level = EventLevel.Verbose)]
        public void Notification(int clientHandle, string value)
        {
            WriteEvent(NotificationId, value, clientHandle);
        }

        /// <summary>
        /// A notification received in Publish complete.
        /// </summary>
        [Event(NotificationReceivedId, Message = NotificationReceivedMessage, Level = EventLevel.Verbose)]
        public void NotificationReceived(int subscriptionId, int sequenceNumber)
        {
            if (IsEnabled())
            {
                WriteEvent(NotificationReceivedId, subscriptionId, sequenceNumber);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(NotificationReceivedEventId, NotificationReceivedMessage,
                    subscriptionId, sequenceNumber);
            }
        }

        /// <summary>
        /// A Publish begin received.
        /// </summary>
        [Event(PublishStartId, Message = PublishStartMessage, Level = EventLevel.Verbose)]
        public void PublishStart(int requestHandle)
        {
            if (IsEnabled())
            {
                WriteEvent(PublishStartId, requestHandle);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(PublishStartEventId, PublishStartMessage, requestHandle);
            }
        }

        /// <summary>
        /// A Publish complete received.
        /// </summary>
        [Event(PublishStopId, Message = PublishStopMessage, Level = EventLevel.Verbose)]
        public void PublishStop(int requestHandle)
        {
            if (IsEnabled())
            {
                WriteEvent(PublishStopId, requestHandle);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Trace))
            {
                Utils.LogTrace(PublishStopEventId, PublishStopMessage, requestHandle);
            }
        }

        /// <summary>
        /// Log a Notification.
        /// </summary>
        [NonEvent]
        public void NotificationValue(uint clientHandle, Variant wrappedValue)
        {
            // expensive operation, only enable if tracemask set
            if ((Utils.TraceMask & Utils.TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    Notification((int)clientHandle, wrappedValue.ToString());
                }
                else if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Utils.LogTrace(NotificationEventId, NotificationMessage,
                        clientHandle, wrappedValue);
                }
            }
        }
    }
}
