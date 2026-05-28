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
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using Opc.Ua.Client.Subscriptions.Streaming;

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
        /// yields decoded <see cref="ConditionTypeRecord"/>s (or any
        /// subtype based on which fields are populated, including
        /// <see cref="AlarmConditionTypeRecord"/>,
        /// <see cref="DialogConditionTypeRecord"/>,
        /// <see cref="AcknowledgeableConditionTypeRecord"/>, and the
        /// alarm subtypes generated for the standard NodeSet).
        /// </summary>
        public static IAsyncEnumerable<ConditionTypeRecord> SubscribeAlarmsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }

            EventRecordDecoderRegistry effective = registry ?? EventRecordDecoderRegistry.Default;
            EventFilter filter = AlarmConditionTypeRecord.EventFilters.Build(effective);
            return SubscribeAlarmsImpl(streaming, notifierId, filter, effective, options, ct);
        }

        private static async IAsyncEnumerable<ConditionTypeRecord> SubscribeAlarmsImpl(
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventFilter filter,
            EventRecordDecoderRegistry registry,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            IAsyncEnumerable<EventNotification> source =
                streaming.SubscribeEventsAsync(notifierId, filter, options, ct);
            await foreach (EventNotification notification in source.ConfigureAwait(false))
            {
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? Array.Empty<Variant>();
                if (registry.Decode(fields) is ConditionTypeRecord record)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Subscribes to condition events (any condition type) from
        /// the supplied notifier and yields decoded records.
        /// </summary>
        public static IAsyncEnumerable<ConditionTypeRecord> SubscribeConditionsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }

            EventRecordDecoderRegistry effective = registry ?? EventRecordDecoderRegistry.Default;
            EventFilter filter = ConditionTypeRecord.EventFilters.Build(effective);
            return SubscribeAlarmsImpl(streaming, notifierId, filter, effective, options, ct);
        }

        /// <summary>
        /// Subscribes to dialog events from the supplied notifier and
        /// yields decoded <see cref="DialogConditionTypeRecord"/>s.
        /// </summary>
        public static IAsyncEnumerable<DialogConditionTypeRecord> SubscribeDialogsAsync(
            this IStreamingSubscription streaming,
            NodeId notifierId,
            EventRecordDecoderRegistry? registry = null,
            MonitoringOptions? options = null,
            CancellationToken ct = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }

            EventRecordDecoderRegistry effective = registry ?? EventRecordDecoderRegistry.Default;
            EventFilter filter = DialogConditionTypeRecord.EventFilters.Build(effective);
            return SubscribeDialogsImpl(streaming, notifierId, filter, effective, options, ct);
        }

        private static async IAsyncEnumerable<DialogConditionTypeRecord> SubscribeDialogsImpl(
            IStreamingSubscription streaming,
            NodeId notifierId,
            EventFilter filter,
            EventRecordDecoderRegistry registry,
            MonitoringOptions? options,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (EventNotification notification in streaming
                .SubscribeEventsAsync(notifierId, filter, options, ct)
                .ConfigureAwait(false))
            {
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? Array.Empty<Variant>();
                if (registry.Decode(fields) is DialogConditionTypeRecord dialog)
                {
                    yield return dialog;
                }
            }
        }
    }
}

