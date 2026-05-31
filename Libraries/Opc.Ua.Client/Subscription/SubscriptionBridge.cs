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
    /// <remarks>
    /// <para>
    /// The classic <c>Opc.Ua.Client.Subscription</c> already exposes a
    /// public <c>SaveMessageInCache(ArrayOf&lt;uint&gt;, NotificationMessage)</c>
    /// with this exact signature; a thin <c>: ISubscriptionMessageSink</c>
    /// declaration on the classic type is sufficient to plug it into the
    /// bridge.
    /// </para>
    /// </remarks>
    public interface ISubscriptionMessageSink
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
    /// <para>
    /// The bridge converts V2 record-based notifications
    /// (<see cref="DataValueChange"/>, <see cref="EventNotification"/>)
    /// into V1 <see cref="NotificationMessage"/> instances and forwards
    /// them to an <see cref="ISubscriptionMessageSink"/>, which is
    /// typically implemented by the V1 <c>Subscription</c> class.
    /// </para>
    /// <para>
    /// <b>Caller responsibility (production wiring not yet integrated):</b>
    /// constructing a <see cref="SubscriptionBridge"/> is not enough on
    /// its own. The bridge must be registered with the V2
    /// <see cref="ISubscriptionManager"/> so the publish loop routes
    /// notifications for the corresponding server-side subscription id
    /// through it. As of this revision the
    /// <see cref="ISubscriptionManager"/> does not expose a registration
    /// API, so the classic <c>Session.AddSubscription(Subscription)</c>
    /// path is supported only when the session's engine is
    /// <see cref="Opc.Ua.Client.ClassicSubscriptionEngine"/>. A V2-engine
    /// session that adds a classic <c>Subscription</c> today will silently
    /// drop publish responses (and the V2 publish loop will delete the
    /// "unknown" subscription on the server). See
    /// <c>plans/26-v2-subscription-parity.md</c> §6 "Bridge wiring TODO"
    /// for the design of the routing hook.
    /// </para>
    /// <para>
    /// The <c>availableSequenceNumbers</c> argument is forwarded as an
    /// empty array today because the V2 handler API does not surface the
    /// server's retransmission-queue list to the handler. Once the bridge
    /// wiring is in place the same change must extend
    /// <see cref="ISubscriptionNotificationHandler"/> (or expose the
    /// list on <see cref="ISubscription"/>) so classic republish /
    /// gap-detection logic continues to operate correctly. Without that,
    /// classic consumers will not republish across packet loss.
    /// </para>
    /// </remarks>
    public sealed class SubscriptionBridge : ISubscriptionNotificationHandler
    {
        private readonly ISubscriptionMessageSink m_messageSink;

        /// <summary>
        /// Creates a new bridge for the specified V1 message sink.
        /// </summary>
        /// <param name="messageSink">
        /// The sink that receives translated V1 notification messages.
        /// </param>
        public SubscriptionBridge(ISubscriptionMessageSink messageSink)
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
                    : []
            };

            NotificationMessage message = BuildNotificationMessage(
                sequenceNumber, publishTime, stringTable,
                new ExtensionObject(dataChange));

            m_messageSink.SaveMessageInCache(
                [], message);

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
                [], message);

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
                [], message);

            return default;
        }

        /// <inheritdoc/>
        public ValueTask OnSubscriptionStateChangedAsync(
            ISubscription subscription,
            SubscriptionState state,
            PublishState publishStateMask,
            System.Threading.CancellationToken ct = default)
        {
            // The bridge translates V2 notifications into V1 cache
            // updates; the V1 subscription class drives its own
            // state-change events via its existing PublishStateChanged /
            // StateChanged event pipeline, so we have nothing to forward
            // here.
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
                string[] table = new string[stringTable.Count];
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
