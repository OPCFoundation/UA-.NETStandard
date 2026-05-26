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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using MItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Streaming extensions that decode raw event notifications into
    /// typed condition/alarm/dialog records.
    /// </summary>
    public static class AlarmStreamExtensions
    {
        /// <summary>
        /// Subscribes to alarm events from the supplied notifier and
        /// yields decoded <see cref="ConditionRecord"/>s (or any
        /// subtype based on which fields are populated).
        /// </summary>
        public static IAsyncEnumerable<ConditionRecord> SubscribeAlarmsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            AlarmEventFilterBuilder? filterBuilder = null,
            MItemOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }

            EventFilter filter = (filterBuilder ?? new AlarmEventFilterBuilder().ForAlarms()).Build();
            return SubscribeAlarmsImpl(streaming, notifierId, filter, options, ct);
        }

        private static async IAsyncEnumerable<ConditionRecord> SubscribeAlarmsImpl(
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventFilter filter,
            MItemOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            IAsyncEnumerable<EventNotification> source =
                streaming.SubscribeEventsAsync(notifierId, filter, options, ct);
            await foreach (EventNotification notification in source.ConfigureAwait(false))
            {
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? Array.Empty<Variant>();
                ConditionRecord? record = AlarmEventDecoder.Decode(fields);
                if (record != null)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Subscribes to condition events (any condition type) from
        /// the supplied notifier and yields decoded records.
        /// </summary>
        public static IAsyncEnumerable<ConditionRecord> SubscribeConditionsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            MItemOptions? options = null,
            CancellationToken ct = default)
        {
            return SubscribeAlarmsAsync(
                streaming,
                notifierId,
                new AlarmEventFilterBuilder().ForConditions(),
                options,
                ct);
        }

        /// <summary>
        /// Subscribes to dialog events from the supplied notifier and
        /// yields decoded <see cref="DialogRecord"/>s.
        /// </summary>
        public static IAsyncEnumerable<DialogRecord> SubscribeDialogsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            MItemOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            EventFilter filter = new AlarmEventFilterBuilder().ForDialogs().Build();
            return SubscribeDialogsImpl(streaming, notifierId, filter, options, ct);
        }

        private static async IAsyncEnumerable<DialogRecord> SubscribeDialogsImpl(
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventFilter filter,
            MItemOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (EventNotification notification in streaming
                .SubscribeEventsAsync(notifierId, filter, options, ct)
                .ConfigureAwait(false))
            {
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? Array.Empty<Variant>();
                ConditionRecord? record = AlarmEventDecoder.Decode(fields);
                if (record is DialogRecord dialog)
                {
                    yield return dialog;
                }
            }
        }
    }
}

