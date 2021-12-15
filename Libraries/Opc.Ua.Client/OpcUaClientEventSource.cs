/* Copyright (c) 1996-2021 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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

        /// <summary>
        /// The client messages.
        /// </summary>
        private const string SubscriptionStateMessage = "Subscription {0}, Id={1}, LastNotificationTime={2:HH:mm:ss}, GoodPublishRequestCount={3}, PublishingInterval={4}, KeepAliveCount={5}, PublishingEnabled={6}, MonitoredItemCount={7}";
        private const string NotificationMessage = "Notification: ClientHandle={0}, Value={1}";

        /// <summary>
        /// The Client Event Ids used for event messages, when calling ILogger.
        /// </summary>
        private readonly EventId SubscriptionStateMessageEventId = new EventId(TraceMasks.Operation, nameof(SubscriptionState));
        private readonly EventId NotificationEventId = new EventId(TraceMasks.Operation, nameof(Notification));

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
        /// The state of the client subscription.
        /// </summary>
        [Event(NotificationId, Message = NotificationMessage, Level = EventLevel.Verbose)]
        public void Notification(int clientHandle, string value)
        {
            WriteEvent(NotificationId, value, clientHandle);
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
