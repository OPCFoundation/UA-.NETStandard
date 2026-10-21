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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An object that manages the sampling groups for a node manager.
    /// </summary>
    public class SamplingGroupManager : IDisposable
    {
        /// <summary>
        /// Creates a new instance of a sampling group manager.
        /// </summary>
        public SamplingGroupManager(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            uint maxQueueSize,
            uint maxDurableQueueSize,
            IEnumerable<SamplingRateGroup> samplingRates)
            : this(
                server,
                nodeManager,
                maxQueueSize,
                maxDurableQueueSize,
                samplingRates,
                timeProvider: null)
        {
        }

        /// <summary>
        /// Creates a new instance of a sampling group manager with an explicit
        /// <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="nodeManager">The owning node manager.</param>
        /// <param name="maxQueueSize">The maximum non-durable monitored-item queue size.</param>
        /// <param name="maxDurableQueueSize">The maximum durable monitored-item queue size.</param>
        /// <param name="samplingRates">The supported sampling-rate groups.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> propagated to the <see cref="SamplingGroup"/>
        /// instances created by this manager. When <c>null</c>, the time provider exposed by
        /// the server (via <see cref="ITimeProviderProvider"/>) is used, falling back to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public SamplingGroupManager(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            uint maxQueueSize,
            uint maxDurableQueueSize,
            IEnumerable<SamplingRateGroup> samplingRates,
            TimeProvider? timeProvider)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_samplingGroups = [];
            m_sampledItems = [];
            m_maxQueueSize = maxQueueSize;
            m_maxDurableQueueSize = maxDurableQueueSize;

            if (samplingRates != null)
            {
                m_samplingRates = [.. samplingRates];

                if (m_samplingRates.Count == 0)
                {
                    m_samplingRates = [.. s_defaultSamplingRates];
                }
            }

            m_samplingRates ??= [.. s_defaultSamplingRates];
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_semanticResampleWork.Dispose();
                List<SamplingGroup>? samplingGroups;
                List<ISampledDataChangeMonitoredItem>? monitoredItems;
                lock (m_lock)
                {
                    samplingGroups = [.. m_samplingGroups];
                    m_samplingGroups.Clear();

                    monitoredItems = [.. m_sampledItems.Keys];
                    m_sampledItems.Clear();
                }

                foreach (SamplingGroup samplingGroup in samplingGroups)
                {
                    samplingGroup?.Dispose();
                }

                foreach (ISampledDataChangeMonitoredItem monitoredItem in monitoredItems)
                {
                    monitoredItem?.Dispose();
                }
            }
        }

        /// <summary>
        /// Stops all sampling groups and clears all items.
        /// </summary>
        public virtual void Shutdown()
        {
            lock (m_lock)
            {
                // stop sampling groups.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    samplingGroup.Shutdown();
                    samplingGroup?.Dispose();
                }

                m_samplingGroups.Clear();
                m_sampledItems.Clear();
            }
        }

        /// <summary>
        /// Schedules one subscriber-specific sample through work owned by this manager's lifetime.
        /// </summary>
        /// <param name="source">The exact source node whose semantics changed.</param>
        /// <param name="item">The monitored item that must be resampled.</param>
        /// <returns><c>true</c> when the current lifecycle owns and scheduled the request.</returns>
        internal bool TryQueueSemanticResample(
            NodeState source,
            ISampledDataChangeMonitoredItem item)
        {
            if (source == null || item == null || !IsExactSource(source, item))
            {
                return false;
            }

            long id;
            TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_lock)
            {
                if (!m_sampledItems.ContainsKey(item) ||
                    item.MonitoringMode == MonitoringMode.Disabled)
                {
                    return false;
                }

                id = Interlocked.Increment(ref m_nextSemanticResampleId);
                m_semanticResamples.Add(id, completion);
                item.SetSemanticsChanged();
            }

            if (m_semanticResampleWork.Run(
                nameof(TryQueueSemanticResample),
                async cancellationToken =>
                {
                    try
                    {
                        await ResampleSemanticsAsync(source, item, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        lock (m_lock)
                        {
                            m_semanticResamples.Remove(id);
                        }
                        completion.TrySetResult(true);
                    }
                }))
            {
                return true;
            }

            lock (m_lock)
            {
                m_semanticResamples.Remove(id);
            }
            completion.TrySetResult(true);
            return false;
        }

        /// <summary>
        /// Waits for semantic resamples admitted before this call to finish.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when the admitted work has finished.</returns>
        internal async ValueTask WaitForPendingSemanticResamplesAsync(CancellationToken cancellationToken)
        {
            Task[] pending;
            lock (m_lock)
            {
                pending = m_semanticResamples.Values.Select(completion => completion.Task).ToArray();
            }

            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ResampleSemanticsAsync(
            NodeState source,
            ISampledDataChangeMonitoredItem item,
            CancellationToken cancellationToken)
        {
            while (IsSemanticResampleCurrent(source, item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ISession? session = item.Session;
                IUserIdentity? identity = session?.EffectiveIdentity ?? item.EffectiveIdentity;
                long identityGeneration = session?.CaptureIdentityRefreshSnapshot().Generation ?? 0;
                EndpointDescription? endpoint = MonitoredReadAuthorization.GetEndpoint(session);

                using OperationContext context = MonitoredReadAuthorization.CreateContext(
                    item,
                    DiagnosticsMasks.OperationAll);
                ReadValueId readValueId = item.GetReadValueId();
                DataValue sampledValue = default;
                ServiceResult? readError = null;
                ServiceResult permission = await m_nodeManager.ValidateRolePermissionsAsync(
                    context,
                    item.NodeId,
                    PermissionType.Read,
                    cancellationToken).ConfigureAwait(false);
                if (!ServiceResult.IsBad(permission))
                {
                    permission = await MonitoredReadAuthorization.ValidateAsync(
                        m_nodeManager,
                        source,
                        new ServerSystemContext(m_server, context),
                        item.AttributeId,
                        cancellationToken).ConfigureAwait(false);
                }
                if (ServiceResult.IsGood(permission))
                {
                    (readError, sampledValue) = await source.ReadAttributeAsync(
                        new ServerSystemContext(m_server, context),
                        readValueId.AttributeId,
                        readValueId.ParsedIndexRange,
                        readValueId.DataEncoding,
                        new DataValue(default, StatusCodes.Good, DateTime.MinValue,
                            m_timeProvider.GetUtcNow().UtcDateTime),
                        cancellationToken).ConfigureAwait(false);

                    permission = await m_nodeManager.ValidateRolePermissionsAsync(
                        context, item.NodeId, PermissionType.Read, cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsGood(permission))
                    {
                        permission = await MonitoredReadAuthorization.ValidateAsync(
                            m_nodeManager,
                            source,
                            new ServerSystemContext(m_server, context),
                            item.AttributeId,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!IsSemanticResampleCurrent(source, item))
                {
                    return;
                }

                EndpointDescription? currentEndpoint = MonitoredReadAuthorization.GetEndpoint(session);
                if (!ReferenceEquals(session, item.Session) ||
                    !ReferenceEquals(identity, session?.EffectiveIdentity ?? item.EffectiveIdentity) ||
                    identityGeneration != (session?.CaptureIdentityRefreshSnapshot().Generation ?? 0) ||
                    session?.IsIdentityStale == true ||
                    !ReferenceEquals(endpoint, currentEndpoint))
                {
                    if (session?.IsIdentityStale == true &&
                        m_server.SessionManager is SessionManager sessionManager)
                    {
                        _ = sessionManager.TryRefreshEffectiveIdentity(session, context.ChannelContext);
                    }
                    continue;
                }

                DataValue queuedValue;
                ServiceResult? queuedError;
                if (ServiceResult.IsBad(permission))
                {
                    queuedValue = DataValue.FromStatusCode(
                        permission.StatusCode, m_timeProvider.GetUtcNow().UtcDateTime);
                    queuedError = permission;
                }
                else if (sampledValue.IsNull)
                {
                    queuedError = readError ?? new ServiceResult(StatusCodes.BadInternalError);
                    queuedValue = DataValue.FromStatusCode(
                        queuedError.StatusCode, m_timeProvider.GetUtcNow().UtcDateTime);
                }
                else
                {
                    queuedValue = sampledValue;
                    queuedError = readError;
                }
                if (item is MonitoredItem concreteItem)
                {
                    concreteItem.QueueValueIfCurrent(context, queuedValue, queuedError, ignoreFilters: true);
                }
                else
                {
                    item.QueueValue(queuedValue, queuedError, ignoreFilters: true);
                }
                return;
            }
        }

        private bool IsSemanticResampleCurrent(NodeState source, ISampledDataChangeMonitoredItem item)
        {
            lock (m_lock)
            {
                return m_sampledItems.ContainsKey(item) &&
                    item.MonitoringMode != MonitoringMode.Disabled &&
                    IsExactSource(source, item);
            }
        }

        private static bool IsExactSource(NodeState source, ISampledDataChangeMonitoredItem item)
        {
            return item.ManagerHandle switch
            {
                NodeHandle handle => ReferenceEquals(handle.Node, source),
                NodeState node => ReferenceEquals(node, source),
                _ => false
            };
        }

        /// <summary>
        /// Creates a new monitored item with the server-revised filter and calls StartMonitoring().
        /// </summary>
        internal ISampledDataChangeMonitoredItem CreateMonitoredItem(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            uint monitoredItemId,
            object managerHandle,
            MonitoredItemCreateRequest itemToCreate,
            MonitoringFilter filterToUse,
            Range range,
            double minimumSamplingInterval,
            bool createDurable,
            double? sourceSamplingInterval = null)
        {
            _ = itemToCreate.RequestedParameters.Filter.TryGetValue(
                out MonitoringFilter? originalFilter);

            // use publishing interval as sampling interval.
            double samplingInterval = SubscriptionManager.CalculateRevisedSamplingInterval(
                itemToCreate.RequestedParameters.SamplingInterval, publishingInterval, minimumSamplingInterval, 0);

            // calculate queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                createDurable,
                itemToCreate.RequestedParameters.QueueSize,
                m_maxQueueSize,
                m_maxDurableQueueSize);

            if (originalFilter is EventFilter)
            {
                // update limits for event filters.
                if (revisedQueueSize == 0)
                {
                    revisedQueueSize = int.MaxValue;
                }

                samplingInterval = 0;
            }

            // check if the queue size was not specified.
            if (revisedQueueSize == 0)
            {
                revisedQueueSize = 1;
            }

            // create monitored item.
            var monitoredItem = new MonitoredItem(
                m_server,
                m_nodeManager,
                managerHandle,
                subscriptionId,
                monitoredItemId,
                itemToCreate.ItemToMonitor,
                context.DiagnosticsMask,
                timestampsToReturn,
                itemToCreate.MonitoringMode,
                itemToCreate.RequestedParameters.ClientHandle,
                originalFilter,
                filterToUse,
                range,
                samplingInterval,
                revisedQueueSize,
                itemToCreate.RequestedParameters.DiscardOldest,
                originalFilter is EventFilter ? 0 : sourceSamplingInterval ?? minimumSamplingInterval,
                createDurable);

            // the node manager reads and queues the initial value after creating the
            // item, so the group does not take another immediate sample.
            MarkInitialValueQueued(monitoredItem);

            // start sampling.
            StartMonitoring(context, monitoredItem);

            // return item.
            return monitoredItem;
        }

        /// <summary>
        /// Restores a monitored item after a server restart and calls StartMonitoring().
        /// </summary>
        public virtual ISampledDataChangeMonitoredItem RestoreMonitoredItem(
            object managerHandle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity)
        {
            // create monitored item.
            ISampledDataChangeMonitoredItem monitoredItem = new MonitoredItem(
                m_server,
                m_nodeManager,
                managerHandle,
                storedMonitoredItem);

            // start sampling.
            using (var startContext = new OperationContext(monitoredItem))
            {
                StartMonitoring(startContext, monitoredItem, savedOwnerIdentity);
            }

            // return item.
            return monitoredItem;
        }

        /// <summary>
        /// Modifies a monitored item with the server-revised filter and calls ModifyMonitoring().
        /// </summary>
        internal ServiceResult? ModifyMonitoredItem(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify,
            MonitoringFilter filterToUse,
            Range range,
            double? revisedSamplingInterval = null)
        {
            _ = itemToModify.RequestedParameters.Filter.TryGetValue(
                out MonitoringFilter? originalFilter);

            // use existing interval as sampling interval.
            double samplingInterval = SubscriptionManager.CalculateRevisedSamplingInterval(
                revisedSamplingInterval ?? itemToModify.RequestedParameters.SamplingInterval,
                monitoredItem.SamplingInterval, monitoredItem.MinimumSamplingInterval, 0);

            // calculate queue size.
            uint revisedQueueSize = SubscriptionManager.CalculateRevisedQueueSize(
                monitoredItem.IsDurable,
                itemToModify.RequestedParameters.QueueSize,
                m_maxQueueSize,
                m_maxDurableQueueSize);

            // Part 4 7.21: 0 selects the default queue size of 1 for data items,
            // the same as on create; it does not keep the previous size.
            if (revisedQueueSize == 0)
            {
                revisedQueueSize = 1;
            }

            if (originalFilter is EventFilter)
            {
                // update limits for event filters.
                samplingInterval = 0;
            }

            // modify the item attributes.
            ServiceResult? error = monitoredItem.ModifyAttributes(
                context.DiagnosticsMask,
                timestampsToReturn,
                itemToModify.RequestedParameters.ClientHandle,
                originalFilter!,
                filterToUse,
                range,
                samplingInterval,
                revisedQueueSize,
                itemToModify.RequestedParameters.DiscardOldest);

            // state of item did not change if an error returned here.
            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // update sampling.
            ModifyMonitoring(context, monitoredItem);

            // everything is ok.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Starts monitoring the item.
        /// </summary>
        /// <remarks>
        /// It will use the external source for monitoring if the source accepts the item.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// </remarks>
        public virtual void StartMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            IUserIdentity? savedOwnerIdentity = null)
        {
            lock (m_lock)
            {
                bool initialValueQueued = m_initialValueQueued.Remove(monitoredItem);

                // do nothing for disabled or exception based items.
                if (monitoredItem.MonitoringMode == MonitoringMode.Disabled ||
                    monitoredItem.MinimumSamplingInterval == 0)
                {
                    m_sampledItems.Add(monitoredItem, null!);
                    return;
                }

                // find a suitable sampling group.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    if (samplingGroup.StartMonitoring(
                        context, monitoredItem, savedOwnerIdentity, initialValueQueued))
                    {
                        m_sampledItems.Add(monitoredItem, samplingGroup);
                        return;
                    }
                }

                // create a new sampling group.
                SamplingGroup? tempSamplingGroup = null;
                try
                {
                    tempSamplingGroup = new SamplingGroup(
                        m_server,
                        m_nodeManager,
                        m_samplingRates,
                        context,
                        monitoredItem.SamplingInterval,
                        savedOwnerIdentity,
                        m_timeProvider);

                    tempSamplingGroup.StartMonitoring(
                        context, monitoredItem, savedOwnerIdentity, initialValueQueued);

                    m_samplingGroups.Add(tempSamplingGroup);
                    m_sampledItems.Add(monitoredItem, tempSamplingGroup);
                    tempSamplingGroup = null; // ownership transferred to collection
                }
                finally
                {
                    tempSamplingGroup?.Dispose();
                }
            }
        }

        /// <summary>
        /// Returns the sampling interval a sampled item with the revised sampling interval
        /// gets once it is assigned to a sampling group.
        /// </summary>
        internal double GetGroupSamplingInterval(double samplingInterval)
        {
            return SamplingGroup.AdjustSamplingInterval(m_samplingRates, samplingInterval);
        }

        /// <summary>
        /// Records that the caller queues the initial value of the item itself, so the
        /// next <see cref="StartMonitoring"/> of the item (directly or through
        /// <see cref="ModifyMonitoring"/>) does not take an additional immediate sample.
        /// </summary>
        internal void MarkInitialValueQueued(ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_initialValueQueued.Add(monitoredItem);
            }
        }

        /// <summary>
        /// Changes monitoring attributes the item.
        /// </summary>
        /// <remarks>
        /// It will call the external source to change the monitoring if an external source was provided originally.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// An item removed by StopMonitoring is not registered again by a concurrent modification.
        /// </remarks>
        public virtual void ModifyMonitoring(
            OperationContext context,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                if (!m_sampledItems.TryGetValue(monitoredItem, out SamplingGroup? samplingGroup))
                {
                    return;
                }

                if (samplingGroup != null &&
                    samplingGroup.ModifyMonitoring(context, monitoredItem))
                {
                    return;
                }

                m_sampledItems.Remove(monitoredItem);

                // assign to a new sampling group.
                StartMonitoring(context, monitoredItem);
                return;
            }
        }

        /// <summary>
        /// Stops monitoring the item.
        /// </summary>
        /// <remarks>
        /// It will call the external source to stop the monitoring if an external source was provided originally.
        /// The changes will not take affect until the ApplyChanges() method is called.
        /// </remarks>
        public virtual void StopMonitoring(ISampledDataChangeMonitoredItem monitoredItem)
        {
            lock (m_lock)
            {
                m_initialValueQueued.Remove(monitoredItem);

                // check for sampling group.
                if (m_sampledItems.TryGetValue(monitoredItem, out SamplingGroup? samplingGroup))
                {
                    samplingGroup?.StopMonitoring(monitoredItem);

                    m_sampledItems.Remove(monitoredItem);
                    return;
                }
            }
        }

        /// <summary>
        /// Applies any pending changes caused by adding,changing or removing monitored items.
        /// </summary>
        public virtual void ApplyChanges()
        {
            lock (m_lock)
            {
                m_initialValueQueued.Clear();
                var unusedGroups = new List<SamplingGroup>();

                // apply changes to groups.
                foreach (SamplingGroup samplingGroup in m_samplingGroups)
                {
                    if (samplingGroup.ApplyChanges())
                    {
                        unusedGroups.Add(samplingGroup);
                    }
                }

                // remove unused groups.
                foreach (SamplingGroup samplingGroup in unusedGroups)
                {
                    m_samplingGroups.Remove(samplingGroup);
                    samplingGroup?.Dispose();
                }
            }
        }

        private readonly Lock m_lock = new();
        private readonly IServerInternal m_server;
        private readonly TimeProvider m_timeProvider;
        private readonly IAsyncNodeManager m_nodeManager;
        private readonly List<SamplingGroup> m_samplingGroups;
        private readonly Dictionary<ISampledDataChangeMonitoredItem, SamplingGroup> m_sampledItems;
        private readonly HashSet<ISampledDataChangeMonitoredItem> m_initialValueQueued = [];
        private readonly List<SamplingRateGroup> m_samplingRates;
        private readonly BackgroundTaskScope m_semanticResampleWork =
            new(nameof(SamplingGroupManager));
        private readonly Dictionary<long, TaskCompletionSource<bool>> m_semanticResamples = [];
        private long m_nextSemanticResampleId;
        private readonly uint m_maxQueueSize;
        private readonly uint m_maxDurableQueueSize;

        /// <summary>
        /// The default sampling rates.
        /// </summary>
        private static readonly SamplingRateGroup[] s_defaultSamplingRates =
        [
            new SamplingRateGroup(100, 100, 4),
            new SamplingRateGroup(500, 250, 2),
            new SamplingRateGroup(1000, 1000, 4),
            new SamplingRateGroup(5000, 2500, 2),
            new SamplingRateGroup(10000, 10000, 4),
            new SamplingRateGroup(60000, 30000, 10),
            new SamplingRateGroup(300000, 60000, 15),
            new SamplingRateGroup(900000, 300000, 9),
            new SamplingRateGroup(3600000, 900000, 0)
        ];
    }
}
