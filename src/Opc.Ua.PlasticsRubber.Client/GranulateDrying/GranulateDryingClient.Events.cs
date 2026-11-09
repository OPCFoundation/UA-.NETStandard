/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
