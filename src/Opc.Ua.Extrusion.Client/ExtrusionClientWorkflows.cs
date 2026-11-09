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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Extrusion.Cutter;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.Machinery;
using Opc.Ua.PlasticsRubber;
using Opc.Ua.PlasticsRubber.Client;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// An event decoded with generated records, retaining the exact original fields.
    /// </summary>
    /// <param name="Record">The most-specific known generated record, or null for an unknown server event.</param>
    /// <param name="Fields">The unmodified field values selected by the registry.</param>
    /// <param name="FieldPaths">Namespace-qualified paths corresponding to Fields.</param>
    public sealed record ExtrusionEvent(
        EventRecord? Record,
        ArrayOf<Variant> Fields,
        ArrayOf<ArrayOf<QualifiedName>> FieldPaths);

    /// <summary>
    /// Typed composition workflows over the generated extrusion proxies.
    /// </summary>
    public static partial class ExtrusionClientWorkflows
    {
        /// <summary>
        /// Opens the actual extrusion-specific Machinery item state, including its execution submachine.
        /// </summary>
        public static async ValueTask<ExtrusionMachineryItemState_StateMachineTypeClient> ItemStateAsync(
            this ExtrusionDeviceTypeClient device,
            CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            var blocks = await device.GetMachineryBuildingBlocksAsync(device.Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw Missing("MachineryBuildingBlocks");
            NodeId node = await ClientModelAccess.ResolveAsync(
                blocks, Machinery.Namespaces.Machinery, Machinery.BrowseNames.MachineryItemState, cancellationToken)
                .ConfigureAwait(false);
            return !node.IsNull
                ? new ExtrusionMachineryItemState_StateMachineTypeClient(device.Session, node, device.Telemetry)
                : throw Missing("MachineryItemState");
        }

        /// <summary>
        /// Opens the independent Machinery operation-mode state machine.
        /// </summary>
        public static async ValueTask<MachineryOperationModeStateMachineTypeClient> OperationModeAsync(
            this ExtrusionDeviceTypeClient device,
            CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            var blocks = await device.GetMachineryBuildingBlocksAsync(device.Telemetry, cancellationToken)
                .ConfigureAwait(false) ?? throw Missing("MachineryBuildingBlocks");
            NodeId node = await ClientModelAccess.ResolveAsync(
                blocks, Machinery.Namespaces.Machinery, Machinery.BrowseNames.MachineryOperationMode, cancellationToken)
                .ConfigureAwait(false);
            return !node.IsNull
                ? new MachineryOperationModeStateMachineTypeClient(device.Session, node, device.Telemetry)
                : throw Missing("MachineryOperationMode");
        }

        /// <summary>
        /// Opens the complete OPC 40083 production-dataset protocol on this device's existing session.
        /// </summary>
        public static async ValueTask<ProductionDatasetClient> ProductionDatasetsAsync(
            this ExtrusionDeviceTypeClient device,
            CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            ProductionDatasetManagementTypeClient management = await device.GetProductionDatasetManagementAsync(
                device.Telemetry, cancellationToken).ConfigureAwait(false)
                    ?? throw Missing("ProductionDatasetManagement");
            return new ProductionDatasetClient(
                ClientModelAccess.RequireSession(device), management.ObjectId);
        }

        /// <summary>
        /// Resets selected common maintenance through the generated method proxy.
        /// </summary>
        public static async ValueTask ResetMaintenanceAsync(
            this ExtrusionDeviceTypeClient device,
            CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            MaintenanceTypeClient maintenance = await device.GetMaintenanceAsync(
                device.Telemetry, cancellationToken).ConfigureAwait(false) ?? throw Missing("Maintenance");
            await maintenance.ResetAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports a missing optional capability explicitly.
        /// </summary>
        private static ServiceResultException Missing(string member)
        {
            return ServiceResultException.Create(StatusCodes.BadNotFound, "The server does not expose '{0}'.", member);
        }
    }

    public sealed partial class ExtrusionClient
    {
        /// <summary>
        /// Observes line, cutter, common alarms/logbook and transition events using generated decoders.
        /// Unknown concrete server subtypes are resolved asynchronously through the session type cache.
        /// </summary>
        public async IAsyncEnumerable<ExtrusionEvent> ObserveEventsAsync(
            NodeId notifier,
            IStreamingSubscription? streaming = null,
            MonitoredItemOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Require(notifier);
            IStreamingSubscription effective = streaming ?? (Session as ManagedSession)?.DefaultStreaming ??
                throw new InvalidOperationException("Supply a streaming subscription or use a ManagedSession.");
            var registry = new EventRecordDecoderRegistry();
            registry.TryRegister(Opc.Ua.ObjectTypeIds.BaseEventType,
                BaseEventTypeRecord.Decoder.StandardFields, BaseEventTypeRecord.Decoder.Decode);
            registry.TryRegister(Opc.Ua.ObjectTypeIds.TransitionEventType,
                TransitionEventTypeRecord.Decoder.StandardFields, TransitionEventTypeRecord.Decoder.Decode);
            registry.RegisterPlasticsRubberDecoders(Session.NamespaceUris);
            registry.RegisterExtrusionLineDecoders(Session.NamespaceUris);
            registry.RegisterExtrusionCutterDecoders(Session.NamespaceUris);
            EventFilter filter = BaseEventTypeRecord.EventFilters.Build(registry);
            var paths = new List<ArrayOf<QualifiedName>>();
            int eventTypeIndex = -1;
            foreach (QualifiedName[] path in registry.StandardFields)
            {
                if (path.Length == 1 && path[0] == QualifiedName.From(Opc.Ua.BrowseNames.EventType))
                {
                    eventTypeIndex = paths.Count;
                }
                paths.Add(path.ToArrayOf());
            }
            ArrayOf<ArrayOf<QualifiedName>> fieldPaths = paths.ToArrayOf();
            await foreach (EventNotification notification in effective.SubscribeEventsAsync(
                notifier, filter, options, cancellationToken).ConfigureAwait(false))
            {
                Variant[] fields = notification.Fields.Span.ToArray();
                EventRecord? decoded = registry.Decode(fields);
                if (decoded == null && eventTypeIndex >= 0 && eventTypeIndex < fields.Length &&
                    fields[eventTypeIndex].TryGetValue(out NodeId type))
                {
                    var visited = new HashSet<NodeId>();
                    while (!type.IsNull && visited.Add(type) && visited.Count <= 64)
                    {
                        type = await Session.NodeCache.FindSuperTypeAsync(type,
                            cancellationToken).ConfigureAwait(false);
                        decoded = registry.DecodeAs(type, fields);
                        if (decoded != null)
                        {
                            break;
                        }
                    }
                }
                yield return new ExtrusionEvent(decoded, notification.Fields, fieldPaths);
            }
        }
    }
}
