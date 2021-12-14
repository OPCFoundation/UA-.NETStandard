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
        public static OpcUaClientEventSource EventLog { get; } = new OpcUaClientEventSource();
    }

    /// <summary>
    /// Event source for high performance logging.
    /// </summary>
    [EventSource(Name = "OPC-UA-Client")]
    public class OpcUaClientEventSource : EventSource
    {
        private const int SubscriptionStateId = 1;

        /// <summary>
        /// The client messages.
        /// </summary>
        private const string SubscriptionStateMessage = "Subscription {0}, Id={1}, LastNotificationTime={2:HH:mm:ss}, GoodPublishRequestCount={3}, PublishingInterval={4}, KeepAliveCount={5}, PublishingEnabled={6}, MonitoredItemCount={7}";
        private const string MonitoredItemReadyMessage = "IsReadyToPublish[{0}] {1}";

        /// <summary>
        /// The Client Event Ids used for event messages, when calling ILogger.
        /// </summary>
        private readonly EventId SubscriptionStateMessageEventId = new EventId(TraceMasks.Operation, nameof(SubscriptionState));

        /// <summary>
        /// The keywords used for this event source.
        /// </summary>
        public static class Keywords
        {
            /// <summary>
            /// Trace
            /// </summary>
            public const EventKeywords Trace = (EventKeywords)1;
            /// <summary>
            /// Diagnostic
            /// </summary>
            public const EventKeywords Diagnostic = (EventKeywords)2;
            /// <summary>
            /// Error
            /// </summary>
            public const EventKeywords Exception = (EventKeywords)4;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Service = (EventKeywords)8;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Security = (EventKeywords)16;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Session = (EventKeywords)32;
            /// <summary>
            /// Service
            /// </summary>
            public const EventKeywords Subscription = (EventKeywords)64;
        }

        /// <summary>
        /// The state of the client subscription.
        /// </summary>
        [Event(SubscriptionStateId, Message = SubscriptionStateMessage, Level = EventLevel.Informational, Keywords = Keywords.Subscription)]
        public void SubscriptionState(string context, uint id, DateTime lastNotificationTime, int goodPublishRequestCount,
            double currentPublishingInterval, uint currentKeepAliveCount, bool currentPublishingEnabled, uint monitoredItemCount)
        {
            if (IsEnabled())
            {
                WriteEvent(SubscriptionStateId, context, id, lastNotificationTime, goodPublishRequestCount,
                    (int)currentPublishingInterval, currentKeepAliveCount, currentPublishingEnabled, monitoredItemCount);
            }
            else if (Utils.Logger.IsEnabled(LogLevel.Information))
            {
                Utils.LogInfo(SubscriptionStateMessageEventId, SubscriptionStateMessage,
                    context, id, lastNotificationTime, goodPublishRequestCount,
                    currentPublishingInterval, currentKeepAliveCount, currentPublishingEnabled, monitoredItemCount);
            }
        }

        /// <summary>
        /// Log a Notification.
        /// </summary>
        [NonEvent]
        public void Notification(uint clientHandle, Variant wrappedValue)
        {
            if ((Utils.TraceMask & Utils.TraceMasks.OperationDetail) != 0)
            {
                if (IsEnabled())
                {
                    //TODO
                    //WriteEvent();
                }
                else if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Utils.LogTrace("Notification: ClientHandle={0}, Value={1}",
                        clientHandle, wrappedValue);
                }
            }
        }
    }
}
