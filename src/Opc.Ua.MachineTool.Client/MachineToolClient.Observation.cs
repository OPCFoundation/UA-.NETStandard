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
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

namespace Opc.Ua.MachineTool.Client
{
    public sealed partial class MachineToolClient
    {
        /// <summary>
        /// Observes the same monitoring and active-program variables as ReadSnapshotAsync using an owned subscription.
        /// Window expiry returns a bounded result; cancellation, failed setup and communication errors remain failures.
        /// </summary>
        /// <param name="machineId">The actual machine instance identifier.</param>
        /// <param name="options">The time and notification limits.</param>
        /// <param name="cancellationToken">Cancels observation without closing the caller's session.</param>
        /// <returns>Notifications with their original quality and acquisition timestamps.</returns>
        public ValueTask<MachineToolDataObservation> ObserveDataAsync(
            NodeId machineId,
            MachineToolObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            RequireNode(machineId);
            MachineToolObservationOptions settings = options ?? new MachineToolObservationOptions();
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            return ObserveOwnedValuesAsync(
                ct => ResolveSnapshotNodesAsync(machineId, ct), settings, cancellationToken);
        }

        /// <summary>
        /// Observes a bounded selection of native companion variables through the same owned subscription and lifetime.
        /// Window expiry returns a bounded result; caller cancellation and service errors remain failures.
        /// </summary>
        /// <param name="nodeIds">One through 256 distinct, non-null variable identifiers.</param>
        /// <param name="options">The existing window, notification and decoded-payload limits.</param>
        /// <param name="cancellationToken">Cancels only this observation.</param>
        /// <returns>Original DataValues and explicit count/overflow information.</returns>
        public ValueTask<MachineToolDataObservation> ObserveValuesAsync(
            ArrayOf<NodeId> nodeIds,
            MachineToolObservationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            if (nodeIds.Count is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeIds), "Select one through 256 variable identifiers.");
            }
            MachineToolObservationOptions settings = options ?? new MachineToolObservationOptions();
            settings.Validate();
            var distinct = new HashSet<NodeId>();
            var copy = new NodeId[nodeIds.Count];
            for (int index = 0; index < nodeIds.Count; index++)
            {
                RequireNode(nodeIds[index]);
                if (!distinct.Add(nodeIds[index]))
                {
                    throw new ArgumentException("Observation identifiers must be distinct.", nameof(nodeIds));
                }
                copy[index] = nodeIds[index];
            }
            var selected = new ArrayOf<NodeId>(copy);
            return ObserveOwnedValuesAsync(
                _ => new ValueTask<ArrayOf<NodeId>>(selected), settings, cancellationToken);
        }

        /// <summary>
        /// Admits both resolved native selections and generated snapshot navigation to the same facade lifetime.
        /// </summary>
        private async ValueTask<MachineToolDataObservation> ObserveOwnedValuesAsync(
            Func<CancellationToken, ValueTask<ArrayOf<NodeId>>> resolveNodes,
            MachineToolObservationOptions settings,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource operation = BeginObservation(cancellationToken);
            try
            {
                return await ObserveDataCoreAsync(resolveNodes, settings, operation.Token).ConfigureAwait(false);
            }
            finally
            {
                EndObservation();
            }
        }

        /// <summary>
        /// Creates and always releases the admitted observation's private subscription.
        /// </summary>
        private async ValueTask<MachineToolDataObservation> ObserveDataCoreAsync(
            Func<CancellationToken, ValueTask<ArrayOf<NodeId>>> resolveNodes,
            MachineToolObservationOptions settings,
            CancellationToken cancellationToken)
        {
            if (!Session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "MachineTool observation requires an options-based subscription session.");
            }
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(settings.Window);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            ISubscription? subscription = null;
            Exception? failure = null;
            MachineToolDataObservation? result = null;
            try
            {
                ArrayOf<NodeId> nodes = await resolveNodes(linked.Token).ConfigureAwait(false);
                var names = new Dictionary<string, NodeId>(StringComparer.Ordinal);
                for (int index = 0; index < nodes.Count; index++)
                {
                    if (!nodes[index].IsNull)
                    {
                        names.Add(index.ToString(CultureInfo.InvariantCulture), nodes[index]);
                    }
                }
                var collector = new DataObservationCollector(names, settings, Session.MessageContext);
                subscription = manager.Add(collector, new OptionsMonitor<SubscriptionOptions>(new SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(100),
                    KeepAliveCount = 10,
                    LifetimeCount = 300,
                    DisableUnboundedItemMode = true
                }));
                var items = new List<IMonitoredItem>();
                foreach (KeyValuePair<string, NodeId> item in names)
                {
                    if (!subscription.MonitoredItems.TryAdd(
                        item.Key,
                        new OptionsMonitor<MonitoredItemOptions>(new MonitoredItemOptions
                        {
                            StartNodeId = item.Value,
                            AttributeId = Attributes.Value,
                            QueueSize = 100,
                            DiscardOldest = false
                        }),
                        out IMonitoredItem? monitoredItem) || monitoredItem == null)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                            "The observation monitored item could not be registered.");
                    }
                    items.Add(monitoredItem);
                }
                foreach (IMonitoredItem item in items)
                {
                    while (!item.Created)
                    {
                        collector.ThrowIfFailed();
                        if (ServiceResult.IsBad(item.Error))
                        {
                            throw new ServiceResultException(item.Error);
                        }
                        await m_timeProvider.Delay(TimeSpan.FromMilliseconds(20), linked.Token).ConfigureAwait(false);
                    }
                }
                try
                {
                    await collector.Completion.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // Only expiry after successful setup is a normal bounded observation.
                }
                cancellationToken.ThrowIfCancellationRequested();
                result = collector.Snapshot();
            }
            catch (OperationCanceledException error) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                failure = new TimeoutException("The observation window expired before subscription setup completed.",
                    error);
            }
            catch (Exception error)
            {
                failure = error;
            }
            if (subscription != null)
            {
                try
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    failure = failure == null ? cleanup :
                        new AggregateException("Observation and subscription cleanup both failed.", failure, cleanup);
                }
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return result ?? throw new InvalidOperationException("The observation produced no result.");
        }

        /// <summary>
        /// Retains bounded copies of data changes delivered by the shared subscription engine.
        /// </summary>
        private sealed class DataObservationCollector : ISubscriptionNotificationHandler
        {
            /// <summary>
            /// Creates a collector with stable item names before any subscription can deliver data.
            /// </summary>
            public DataObservationCollector(
                Dictionary<string, NodeId> nodes,
                MachineToolObservationOptions options,
                IServiceMessageContext messageContext)
            {
                m_nodes = nodes;
                m_options = options;
                m_messageContext = messageContext;
            }

            /// <summary>
            /// Gets the notification-limit or failure signal.
            /// </summary>
            public Task Completion => m_completion.Task;

            /// <summary>
            /// Captures the bounded sample buffer.
            /// </summary>
            public MachineToolDataObservation Snapshot()
            {
                lock (m_gate)
                {
                    ThrowIfFailed();
                    return new MachineToolDataObservation(
                        m_values.ToArrayOf(), m_values.Count == m_options.MaximumNotifications, m_dropped);
                }
            }

            /// <summary>
            /// Rejects a failed stream instead of returning an empty successful observation.
            /// </summary>
            public void ThrowIfFailed()
            {
                lock (m_gate)
                {
                    if (m_failure != null)
                    {
                        ExceptionDispatchInfo.Capture(m_failure).Throw();
                    }
                }
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
                foreach (DataValueChange change in notification.Span)
                {
                    lock (m_gate)
                    {
                        if (m_failure != null)
                        {
                            continue;
                        }
                        if (change.Value.StatusCode.Overflow)
                        {
                            m_dropped++;
                        }
                        if (m_values.Count == m_options.MaximumNotifications)
                        {
                            m_dropped++;
                            continue;
                        }
                        try
                        {
                            if (change.MonitoredItem == null ||
                                !m_nodes.TryGetValue(change.MonitoredItem.Name, out NodeId nodeId))
                            {
                                throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                                    "The observation received an unrecognized monitored item.");
                            }
                            using var encoder = new JsonEncoder(m_messageContext);
                            encoder.WriteDataValue("Value", change.Value);
                            m_bytes += Encoding.UTF8.GetByteCount(encoder.CloseAndReturnText());
                            if (m_bytes > 1024 * 1024)
                            {
                                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                    "Observation exceeded the one-megabyte decoded payload limit.");
                            }
                            m_values.Add(new MachineToolObservedValue(
                                nodeId, change.Value, publishTime, sequenceNumber));
                            if (m_values.Count == m_options.MaximumNotifications)
                            {
                                m_completion.TrySetResult(true);
                            }
                        }
                        catch (Exception error)
                        {
                            m_failure = error;
                            m_completion.TrySetException(error);
                        }
                    }
                }
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
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                if ((publishStateMask & PublishState.Completed) == 0 &&
                    (state == SubscriptionState.Error ||
                        (publishStateMask & (PublishState.Stopped | PublishState.Timeout | PublishState.Transferred)) != 0))
                {
                    lock (m_gate)
                    {
                        m_failure ??= new ServiceResultException(StatusCodes.BadNoCommunication,
                            "The observation subscription stopped or failed.");
                        m_completion.TrySetException(m_failure);
                    }
                }
                return default;
            }

            /// <summary>Protects the short, synchronous sample-buffer updates.</summary>
            private readonly Lock m_gate = new();

            /// <summary>The monitored-item names and their exact instance variables.</summary>
            private readonly Dictionary<string, NodeId> m_nodes;

            /// <summary>The validated observation limits.</summary>
            private readonly MachineToolObservationOptions m_options;

            /// <summary>The endpoint's namespace-aware encoder context.</summary>
            private readonly IServiceMessageContext m_messageContext;

            /// <summary>The retained data samples.</summary>
            private readonly List<MachineToolObservedValue> m_values = [];

            /// <summary>The completion signal; consumers never run under the buffer gate.</summary>
            private readonly TaskCompletionSource<bool> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The stream or payload failure, retained even if it races normal window expiry.</summary>
            private Exception? m_failure;

            /// <summary>The encoded sample bytes retained by this call.</summary>
            private long m_bytes;

            /// <summary>The known local discards and observed server overflow markers.</summary>
            private long m_dropped;
        }
    }
}
