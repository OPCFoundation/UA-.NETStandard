#if OPCUA_CLIENT_V2
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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions.Engine
{
    /// <summary>
    /// Abstraction for the V1 Subscription message cache so that the
    /// bridge can feed translated notifications without a direct
    /// assembly reference to Opc.Ua.Client.
    /// </summary>
    internal interface ISubscriptionMessageSink
    {
        /// <summary>
        /// Stores a <see cref="NotificationMessage"/> in the V1
        /// subscription cache.
        /// </summary>
        /// <param name="availableSequenceNumbers">
        /// The currently available sequence numbers on the server.
        /// </param>
        /// <param name="message">
        /// The notification message to cache.
        /// </param>
        void SaveMessageInCache(
            ArrayOf<uint> availableSequenceNumbers,
            NotificationMessage message);
    }

    /// <summary>
    /// Bridges V2 subscription notification callbacks to V1 Subscription
    /// event/cache mechanisms, enabling V1 Subscription objects to receive
    /// notifications when the V2 engine is active.
    /// </summary>
    /// <remarks>
    /// The bridge converts V2 record-based notifications
    /// (<see cref="DataValueChange"/>, <see cref="EventNotification"/>)
    /// into V1 <see cref="NotificationMessage"/> instances and forwards
    /// them to an <see cref="ISubscriptionMessageSink"/>, which is
    /// typically implemented by the V1 <c>Subscription</c> class.
    /// </remarks>
    internal sealed class V2SubscriptionBridge : ISubscriptionNotificationHandler
    {
        private readonly ISubscriptionMessageSink m_messageSink;

        /// <summary>
        /// Creates a new bridge for the specified V1 message sink.
        /// </summary>
        /// <param name="messageSink">
        /// The sink that receives translated V1 notification messages.
        /// </param>
        public V2SubscriptionBridge(ISubscriptionMessageSink messageSink)
        {
            m_messageSink = messageSink
                ?? throw new ArgumentNullException(nameof(messageSink));
        }

        /// <inheritdoc/>
        public ValueTask OnDataChangeNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<DataValueChange> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            ReadOnlySpan<DataValueChange> span = notification.Span;

            var monitoredItems =
                new MonitoredItemNotification[span.Length];
            DiagnosticInfo[]? diagnosticInfos = null;

            for (int i = 0; i < span.Length; i++)
            {
                DataValueChange change = span[i];
                uint clientHandle =
                    change.MonitoredItem?.ClientHandle ?? 0;

                monitoredItems[i] = new MonitoredItemNotification
                {
                    ClientHandle = clientHandle,
                    Value = change.Value
                };

                if (change.DiagnosticInfo != null)
                {
                    diagnosticInfos ??= new DiagnosticInfo[span.Length];
                    diagnosticInfos[i] = change.DiagnosticInfo;
                }
            }

            var dataChange = new DataChangeNotification
            {
                MonitoredItems = new ArrayOf<MonitoredItemNotification>(
                    monitoredItems),
                DiagnosticInfos = diagnosticInfos != null
                    ? new ArrayOf<DiagnosticInfo>(diagnosticInfos)
                    : ArrayOf<DiagnosticInfo>.Empty
            };

            NotificationMessage message = BuildNotificationMessage(
                sequenceNumber, publishTime, stringTable,
                new ExtensionObject(dataChange));

            m_messageSink.SaveMessageInCache(
                ArrayOf<uint>.Empty, message);

            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnEventDataNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            ReadOnlyMemory<EventNotification> notification,
            PublishState publishStateMask,
            IReadOnlyList<string> stringTable)
        {
            ReadOnlySpan<EventNotification> span = notification.Span;

            var events = new EventFieldList[span.Length];

            for (int i = 0; i < span.Length; i++)
            {
                EventNotification evt = span[i];
                uint clientHandle =
                    evt.MonitoredItem?.ClientHandle ?? 0;

                events[i] = new EventFieldList
                {
                    ClientHandle = clientHandle,
                    EventFields = evt.Fields
                };
            }

            var eventList = new EventNotificationList
            {
                Events = new ArrayOf<EventFieldList>(events)
            };

            NotificationMessage message = BuildNotificationMessage(
                sequenceNumber, publishTime, stringTable,
                new ExtensionObject(eventList));

            m_messageSink.SaveMessageInCache(
                ArrayOf<uint>.Empty, message);

            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnKeepAliveNotificationAsync(
            ISubscription subscription,
            uint sequenceNumber,
            DateTime publishTime,
            PublishState publishStateMask)
        {
            // A keep-alive is a NotificationMessage with no
            // notification data entries.
            var message = new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = publishTime
            };

            m_messageSink.SaveMessageInCache(
                ArrayOf<uint>.Empty, message);

            return default;
        }

        /// <summary>
        /// Builds a <see cref="NotificationMessage"/> containing a
        /// single notification data extension object.
        /// </summary>
        private static NotificationMessage BuildNotificationMessage(
            uint sequenceNumber,
            DateTime publishTime,
            IReadOnlyList<string> stringTable,
            ExtensionObject notificationData)
        {
            var message = new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = publishTime,
                NotificationData = new ArrayOf<ExtensionObject>(
                    new[] { notificationData })
            };

            if (stringTable != null && stringTable.Count > 0)
            {
                var table = new string[stringTable.Count];
                for (int i = 0; i < stringTable.Count; i++)
                {
                    table[i] = stringTable[i];
                }
                message.StringTable = new ArrayOf<string>(table);
            }

            return message;
        }
    }
}
#endif
