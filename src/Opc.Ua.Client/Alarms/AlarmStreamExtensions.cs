// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

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
        /// <exception cref="ArgumentNullException"><paramref name="streaming"/> is <c>null</c>.</exception>
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
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? [];
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
        /// <exception cref="ArgumentNullException"><paramref name="streaming"/> is <c>null</c>.</exception>
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
        /// <exception cref="ArgumentNullException"><paramref name="streaming"/> is <c>null</c>.</exception>
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
                IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? [];
                if (registry.Decode(fields) is DialogConditionTypeRecord dialog)
                {
                    yield return dialog;
                }
            }
        }
    }
}
