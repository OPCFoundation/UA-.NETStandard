/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Registers complete-message handlers without changing the existing manager interface.
    /// </summary>
    public static class SubscriptionNotificationMessageExtensions
    {
        /// <summary>
        /// Adds a subscription that delivers complete messages instead of individual payload callbacks.
        /// </summary>
        /// <param name="manager">The subscription manager.</param>
        /// <param name="handler">The complete-message handler.</param>
        /// <param name="options">The subscription's options.</param>
        public static ISubscription Add(
            this ISubscriptionManager manager,
            ISubscriptionNotificationMessageHandler handler,
            IOptionsMonitor<SubscriptionOptions> options)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            return manager.Add(new NotificationMessageDispatch.HandlerAdapter(handler), options);
        }
    }

    /// <summary>
    /// Shared projection and compatibility adapters for complete notification messages.
    /// </summary>
    internal static class NotificationMessageDispatch
    {
        /// <summary>
        /// Projects all wire payloads without retaining pooled notification containers.
        /// </summary>
        internal static SubscriptionNotificationMessage Create(
            NotificationMessage message,
            AcknowledgementHandle handle,
            ArrayOf<string> stringTable,
            Func<DataChangeNotification, ReadOnlyMemory<DataValueChange>> dataChanges,
            Func<EventNotificationList, ReadOnlyMemory<EventNotification>> events)
        {
            var payloads = new SubscriptionNotificationPayload[message.NotificationData.Count];
            for (int i = 0; i < payloads.Length; i++)
            {
                ExtensionObject payload = message.NotificationData[i];
                if (payload.TryGetValue(out DataChangeNotification? data))
                {
                    payloads[i] = new SubscriptionNotificationPayload(
                        SubscriptionNotificationKind.DataChange, new(dataChanges(data)), [], default, null);
                }
                else if (payload.TryGetValue(out EventNotificationList? eventList))
                {
                    payloads[i] = new SubscriptionNotificationPayload(
                        SubscriptionNotificationKind.Event, [], new(events(eventList)), default, null);
                }
                else if (payload.TryGetValue(out StatusChangeNotification? status))
                {
                    payloads[i] = new SubscriptionNotificationPayload(
                        SubscriptionNotificationKind.StatusChange, [], [], status.Status, status.DiagnosticInfo);
                }
                else
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "A complete notification message contains an unsupported payload.");
                }
            }
            return new SubscriptionNotificationMessage(
                handle, (DateTime)message.PublishTime, payloads, stringTable);
        }

        /// <summary>
        /// Projects a classic message using its wire client handles.
        /// </summary>
        internal static SubscriptionNotificationMessage CreateClassic(
            NotificationMessage message,
            AcknowledgementHandle handle)
        {
            return Create(message, handle, message.StringTable, DataChanges, Events);

            ReadOnlyMemory<DataValueChange> DataChanges(DataChangeNotification data)
            {
                var values = new DataValueChange[data.MonitoredItems.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    MonitoredItemNotification item = data.MonitoredItems[i];
                    values[i] = new DataValueChange(null, item.Value, item.DiagnosticInfo)
                    {
                        ClientHandle = item.ClientHandle,
                        PartitionServerId = handle.SubscriptionId,
                        Acknowledgement = handle
                    };
                }
                return values;
            }

            ReadOnlyMemory<EventNotification> Events(EventNotificationList events)
            {
                var values = new EventNotification[events.Events.Count];
                for (int i = 0; i < values.Length; i++)
                {
                    EventFieldList item = events.Events[i];
                    values[i] = new EventNotification(null, item.EventFields)
                    {
                        ClientHandle = item.ClientHandle,
                        PartitionServerId = handle.SubscriptionId,
                        Acknowledgement = handle
                    };
                }
                return values;
            }
        }

        /// <summary>
        /// Adapts message-only handlers to the existing subscription registration contract.
        /// </summary>
        internal sealed class HandlerAdapter(ISubscriptionNotificationMessageHandler handler) :
            ISubscriptionNotificationHandler, ISubscriptionNotificationMessageHandler
        {
            /// <inheritdoc/>
            public ValueTask OnNotificationMessageAsync(
                SubscriptionNotificationMessage message, CancellationToken ct = default)
            {
                return handler.OnNotificationMessageAsync(message, ct);
            }

            /// <inheritdoc/>
            public ValueTask OnDataChangeNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                throw new InvalidOperationException("A whole-message handler cannot receive a partial payload.");
            }

            /// <inheritdoc/>
            public ValueTask OnEventDataNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                throw new InvalidOperationException("A whole-message handler cannot receive a partial payload.");
            }

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(ISubscription subscription, uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                return handler is ISubscriptionNotificationHandler callbacks
                    ? callbacks.OnKeepAliveNotificationAsync(
                        subscription, sequenceNumber, publishTime, publishStateMask)
                    : default;
            }

            /// <inheritdoc/>
            public ValueTask OnSubscriptionStateChangedAsync(ISubscription subscription,
                SubscriptionState state, PublishState publishStateMask, CancellationToken ct = default)
            {
                return handler is ISubscriptionNotificationHandler callbacks
                    ? callbacks.OnSubscriptionStateChangedAsync(subscription, state, publishStateMask, ct)
                    : default;
            }
        }
    }
}
