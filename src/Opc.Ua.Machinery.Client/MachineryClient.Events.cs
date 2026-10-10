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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.Machinery.Result;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;
using ResultBrowseNames = Opc.Ua.Machinery.Result.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Resolves a machine's OPC 40001-1 <c>Notifications</c> add-in, the
        /// notifier it publishes its own events on, or
        /// <see cref="NodeId.Null"/> when it publishes none.
        /// </summary>
        /// <param name="machine">The machine to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<NodeId> ResolveNotificationsAsync(
            NodeId machine,
            CancellationToken cancellationToken = default)
        {
            return ResolveMachineryChildAsync(
                machine,
                MachineryBrowseNames.Notifications,
                cancellationToken);
        }

        /// <summary>
        /// Streams the events a machine publishes on its <c>Notifications</c>
        /// add-in.
        /// </summary>
        /// <remarks>
        /// OPC 40001-1 fixes the notifier but not the event types, so the
        /// notifications arrive undecoded: the filter selects the standard
        /// <c>BaseEventType</c> fields, and a caller that knows the vendor's
        /// event types decodes further from there.
        /// </remarks>
        /// <param name="machine">The machine to observe.</param>
        /// <param name="streaming">
        /// The streaming subscription. Pass <see langword="null"/> to use the
        /// session's default, which requires a <see cref="ManagedSession"/>.
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<BaseEventTypeRecord> ObserveNotificationsAsync(
            NodeId machine,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId notifier = await ResolveNotificationsAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            if (notifier.IsNull)
            {
                yield break;
            }

            EventRecordDecoderRegistry registry =
                EventRecordDecoderRegistry.Default.CreateChildScope();
            EventFilter filter = BaseEventTypeRecord.EventFilters.Build(registry);

            await foreach (EventNotification notification in
                (streaming ?? GetDefaultStreaming(Session))
                    .SubscribeEventsAsync(notifier, filter, options, cancellationToken)
                    .ConfigureAwait(false))
            {
                if (registry.Decode(notification.Fields.ToArray() ?? []) is
                    BaseEventTypeRecord record)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Streams the OPC 40001-101 result-ready events of a machine.
        /// </summary>
        /// <remarks>
        /// <c>ResultReadyEventType</c> is abstract, so a conformant server
        /// reports its events with a concrete subtype of its own. The
        /// <c>OfType</c> clause of the filter still selects them — it matches
        /// subtypes — but the decoder registry is keyed by the exact event
        /// type, so an event that does not decode directly is re-decoded
        /// against the abstract type once the session's type cache confirms
        /// the subtype relationship.
        /// </remarks>
        /// <param name="machine">The machine to observe.</param>
        /// <param name="streaming">
        /// The streaming subscription. Pass <see langword="null"/> to use the
        /// session's default.
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<ResultReadyEventTypeRecord> ObserveResultsAsync(
            NodeId machine,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId management = await ResolveResultManagementAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            if (management.IsNull)
            {
                yield break;
            }

            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default
                .CreateChildScope()
                .RegisterMachineryResultDecoders(Session.NamespaceUris);
            EventFilter filter = ResultReadyEventTypeRecord.EventFilters.Build(
                Session.NamespaceUris,
                registry);
            NodeId declaredType = ExpandedNodeId.ToNodeId(
                Opc.Ua.Machinery.Result.ObjectTypeIds.ResultReadyEventType,
                Session.NamespaceUris);

            await foreach (EventNotification notification in
                (streaming ?? GetDefaultStreaming(Session))
                    .SubscribeEventsAsync(management, filter, options, cancellationToken)
                    .ConfigureAwait(false))
            {
                ResultReadyEventTypeRecord? record = await DecodeAsync(
                    registry,
                    notification,
                    declaredType,
                    cancellationToken).ConfigureAwait(false)
                    as ResultReadyEventTypeRecord;
                if (record != null)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Streams the OPC 40001-2 zero-point-adjustment events of a process
        /// value. <c>ZeroPointAdjustmentEventType</c> is abstract and is
        /// handled the same way as the result-ready event.
        /// </summary>
        /// <param name="processValue">The process value to observe.</param>
        /// <param name="streaming">
        /// The streaming subscription. Pass <see langword="null"/> to use the
        /// session's default.
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<ZeroPointAdjustmentEventTypeRecord>
            ObserveZeroPointAdjustmentsAsync(
                NodeId processValue,
                IStreamingSubscription? streaming = null,
                MonitoringOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (processValue.IsNull)
            {
                yield break;
            }
            EventRecordDecoderRegistry registry = EventRecordDecoderRegistry.Default
                .CreateChildScope()
                .RegisterMachineryProcessValuesDecoders(Session.NamespaceUris);
            EventFilter filter = ZeroPointAdjustmentEventTypeRecord.EventFilters.Build(
                Session.NamespaceUris,
                registry);
            NodeId declaredType = ExpandedNodeId.ToNodeId(
                Opc.Ua.Machinery.ProcessValues.ObjectTypeIds.ZeroPointAdjustmentEventType,
                Session.NamespaceUris);

            await foreach (EventNotification notification in
                (streaming ?? GetDefaultStreaming(Session))
                    .SubscribeEventsAsync(processValue, filter, options, cancellationToken)
                    .ConfigureAwait(false))
            {
                ZeroPointAdjustmentEventTypeRecord? record = await DecodeAsync(
                    registry,
                    notification,
                    declaredType,
                    cancellationToken).ConfigureAwait(false)
                    as ZeroPointAdjustmentEventTypeRecord;
                if (record != null)
                {
                    yield return record;
                }
            }
        }

        /// <summary>
        /// Reads the result variables a machine publishes in the
        /// OPC 40001-101 <c>Results</c> folder. Returns nothing when the
        /// machine publishes no folder or no variables in it.
        /// </summary>
        /// <param name="machine">The machine to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<ResultDataType> ReadPublishedResultsAsync(
            NodeId machine,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId management = await ResolveResultManagementAsync(machine, cancellationToken)
                .ConfigureAwait(false);
            if (management.IsNull)
            {
                yield break;
            }
            ushort resultNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
            NodeId folder = await ResolveChildAsync(
                management,
                new QualifiedName(ResultBrowseNames.Results, resultNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (folder.IsNull)
            {
                yield break;
            }

            var names = new List<QualifiedName>();
            await foreach (MachineEntry entry in EnumerateChildVariablesAsync(
                    folder,
                    cancellationToken).ConfigureAwait(false))
            {
                names.Add(entry.BrowseName);
            }
            if (names.Count == 0)
            {
                yield break;
            }

            Dictionary<QualifiedName, Variant> values = await ReadChildValuesAsync(
                folder,
                names,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);
            foreach (QualifiedName name in names)
            {
                if (values.TryGetValue(name, out Variant value) &&
                    value.TryGetStructure<ResultDataType>(
                        Session.MessageContext,
                        out ResultDataType? result) &&
                    !string.IsNullOrEmpty(result?.ResultMetaData?.ResultId))
                {
                    yield return result!;
                }
            }
        }

        /// <summary>
        /// Decodes an event notification, falling back to the declared
        /// abstract type when the concrete subtype a conformant server reports
        /// with has no decoder of its own.
        /// </summary>
        private async ValueTask<BaseEventTypeRecord?> DecodeAsync(
            EventRecordDecoderRegistry registry,
            EventNotification notification,
            NodeId declaredType,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Variant> fields = notification.Fields.ToArray() ?? [];
            if (registry.Decode(fields) is BaseEventTypeRecord decoded)
            {
                return decoded;
            }
            if (declaredType.IsNull ||
                !TryGetEventType(registry.StandardFields, fields, out NodeId eventType) ||
                !await Session.NodeCache
                    .IsTypeOfAsync(eventType, declaredType, cancellationToken)
                    .ConfigureAwait(false))
            {
                return null;
            }
            return registry.DecodeAs(declaredType, fields) as BaseEventTypeRecord;
        }

        private static bool TryGetEventType(
            QualifiedName[][] standardFields,
            IReadOnlyList<Variant> fields,
            out NodeId eventType)
        {
            for (int ii = 0; ii < standardFields.Length && ii < fields.Count; ii++)
            {
                QualifiedName[] path = standardFields[ii];
                if (path.Length > 0 &&
                    string.Equals(
                        path[^1].Name,
                        Opc.Ua.BrowseNames.EventType,
                        System.StringComparison.Ordinal) &&
                    fields[ii].TryGetValue(out eventType) &&
                    !eventType.IsNull)
                {
                    return true;
                }
            }
            eventType = NodeId.Null;
            return false;
        }
    }
}
