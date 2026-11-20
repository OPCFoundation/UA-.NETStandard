// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Client;

namespace Opc.Ua.GranulateDrying.Client
{
    public sealed partial class GranulateDryingClient
    {
        /// <summary>
        /// Observes generated maintenance records through an existing streaming subscription.
        /// Abstract maintenance types are decoded through their known base layout, retaining the concrete EventType.
        /// </summary>
        public IAsyncEnumerable<MaintenanceAlarmTypeRecord> ObserveMaintenanceAsync(
            IStreamingSubscription streaming,
            NodeId notifier,
            CancellationToken cancellationToken = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (notifier.IsNull)
            {
                throw new ArgumentException("An event notifier is required.", nameof(notifier));
            }
            var registry = new EventRecordDecoderRegistry().RegisterGranulateDryingDecoders(Session.NamespaceUris);
            EventFilter filter = MaintenanceAlarmTypeRecord.EventFilters.Build(Session.NamespaceUris, registry);
            NodeId type = ExpandedNodeId.ToNodeId(ObjectTypeIds.MaintenanceAlarmType, Session.NamespaceUris);
            return PlasticsRubberEventStream.ObserveAsync<MaintenanceAlarmTypeRecord>(
                Session, notifier, type, registry, filter, streaming, null, cancellationToken);
        }

        /// <summary>
        /// Observes the common alarm fields and HelpText of device and component errors.
        /// Physical reset remains a generated OperationType method; acknowledging a condition does not clear an error.
        /// </summary>
        public IAsyncEnumerable<HelpOffNormalAlarmTypeRecord> ObserveAlarmsAsync(
            IStreamingSubscription streaming,
            NodeId notifier,
            CancellationToken cancellationToken = default)
        {
            if (streaming == null)
            {
                throw new ArgumentNullException(nameof(streaming));
            }
            if (notifier.IsNull)
            {
                throw new ArgumentException("An event notifier is required.", nameof(notifier));
            }
            var registry = new EventRecordDecoderRegistry().RegisterPlasticsRubberDecoders(Session.NamespaceUris);
            EventFilter filter = HelpOffNormalAlarmTypeRecord.EventFilters.Build(Session.NamespaceUris, registry);
            NodeId type = ExpandedNodeId.ToNodeId(PlasticsRubber.ObjectTypeIds.HelpOffNormalAlarmType,
                Session.NamespaceUris);
            return PlasticsRubberEventStream.ObserveAsync<HelpOffNormalAlarmTypeRecord>(
                Session, notifier, type, registry, filter, streaming, null, cancellationToken);
        }
    }
}
