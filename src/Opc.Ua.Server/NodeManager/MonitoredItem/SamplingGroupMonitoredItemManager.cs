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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Manages the montioredItems for a NodeManager
    /// </summary>
    public class SamplingGroupMonitoredItemManager :
        IMonitoredItemManager,
        IMonitoredItemManagerLifecycle,
        ICustomMonitoredItemManager
    {
        /// <inheritdoc/>
        public SamplingGroupMonitoredItemManager(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            m_samplingGroupManager = new SamplingGroupManager(
                server,
                nodeManager,
                (uint)configuration.ServerConfiguration!.MaxNotificationQueueSize,
                (uint)configuration.ServerConfiguration.MaxDurableNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates.ToArray()!);

            m_nodeManager = nodeManager;
            m_server = server;
            MonitoredNodes = [];
            MonitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }

        /// <summary>
        /// Creates a monitored-item manager with a supplied sampling-group manager.
        /// </summary>
        internal SamplingGroupMonitoredItemManager(
            IAsyncNodeManager nodeManager,
            IServerInternal server,
            SamplingGroupManager samplingGroupManager)
        {
            m_nodeManager = nodeManager;
            m_server = server;
            m_samplingGroupManager = samplingGroupManager;
            MonitoredNodes = [];
            MonitoredItems = new ConcurrentDictionary<uint, IMonitoredItem>();
        }

        /// <inheritdoc/>
        public NodeIdDictionary<MonitoredNode2> MonitoredNodes { get; }

        /// <inheritdoc/>
        public ConcurrentDictionary<uint, IMonitoredItem> MonitoredItems { get; }

        /// <summary>
        /// Queues a semantic resample through the lifecycle-owned sampling groups.
        /// </summary>
        internal bool TryQueueSemanticResample(NodeState source, ISampledDataChangeMonitoredItem item)
        {
            return m_samplingGroupManager.TryQueueSemanticResample(source, item);
        }

        /// <summary>
        /// Waits for semantic resamples already admitted by the sampling groups.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when pending resamples finish.</returns>
        internal ValueTask WaitForPendingSemanticResamplesAsync(CancellationToken cancellationToken)
        {
            return m_samplingGroupManager.WaitForPendingSemanticResamplesAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ISampledDataChangeMonitoredItem CreateMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            Range euRange,
            MonitoringFilter filterToUse,
            double samplingInterval,
            uint revisedQueueSize,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache)
        {
            // set min sampling interval if 0
            if (samplingInterval.CompareTo(0.0) == 0)
            {
                samplingInterval = 1;
            }

            uint monitoredItemId = monitoredItemIdFactory.GetNextId();

            // create monitored item.
            ISampledDataChangeMonitoredItem monitoredItem =
                m_samplingGroupManager.CreateMonitoredItem(
                    context.OperationContext!,
                    subscriptionId,
                    publishingInterval,
                    timestampsToReturn,
                    monitoredItemId,
                    handle,
                    itemToCreate,
                    filterToUse,
                    euRange,
                    samplingInterval,
                    createDurable,
                    sourceSamplingInterval: Math.Max(1, SubscriptionManager.CalculateRevisedSamplingInterval(
                        0, 1, handle.Node, itemToCreate.ItemToMonitor.AttributeId, 0)));

            // save the monitored item.
            MonitoredItems.AddOrUpdate(
                monitoredItemId,
                monitoredItem,
                (key, oldValue) => monitoredItem);

            return monitoredItem;
        }

        /// <inheritdoc/>
        ISampledDataChangeMonitoredItem ICustomMonitoredItemManager.CreateCustomMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            uint subscriptionId,
            double publishingInterval,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequest itemToCreate,
            Range euRange,
            MonitoringFilter filterToUse,
            double samplingInterval,
            uint revisedQueueSize,
            bool createDurable,
            MonitoredItemIdFactory monitoredItemIdFactory,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache,
            MonitoredItemFactory factory,
            bool initialValueQueued)
        {
            _ = addNodeToComponentCache;
            _ = removeNodeFromComponentCache;

            if (samplingInterval.CompareTo(0.0) == 0)
            {
                samplingInterval = 1;
            }

            uint monitoredItemId;
            do
            {
                monitoredItemId = monitoredItemIdFactory.GetNextId();
            } while (!MonitoredItems.TryAdd(monitoredItemId, null!));

            ISampledDataChangeMonitoredItem? monitoredItem = null;
            bool monitoringStarted = false;
            try
            {
                var factoryContext = new MonitoredItemFactoryContext(
                    server,
                    nodeManager,
                    context,
                    handle,
                    subscriptionId,
                    monitoredItemId,
                    publishingInterval,
                    diagnosticsMasks,
                    timestampsToReturn,
                    itemToCreate,
                    euRange,
                    filterToUse,
                    samplingInterval,
                    revisedQueueSize,
                    createDurable);
                monitoredItem = factory(factoryContext);
                CustomMonitoredItemValidation.Validate(factoryContext, monitoredItem);

                // the node manager reads and queues the initial value after creating
                // the item, so the group does not take another immediate sample.
                if (initialValueQueued)
                {
                    m_samplingGroupManager.MarkInitialValueQueued(monitoredItem);
                }

                m_samplingGroupManager.StartMonitoring(
                    context.OperationContext!,
                    monitoredItem);
                monitoringStarted = true;
                MonitoredItems[monitoredItemId] = monitoredItem;
                return monitoredItem;
            }
            catch
            {
                if (monitoringStarted)
                {
                    m_samplingGroupManager.StopMonitoring(monitoredItem!);
                }
                MonitoredItems.TryRemove(monitoredItemId, out _);
                monitoredItem?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Returns the sampling interval a data item created or modified with the revised
        /// sampling interval reports once its sampling group rounds it to a supported rate.
        /// </summary>
        internal double GetGroupSamplingInterval(double samplingInterval)
        {
            // the same minimum the create path applies.
            if (samplingInterval.CompareTo(0.0) == 0)
            {
                samplingInterval = 1;
            }

            return m_samplingGroupManager.GetGroupSamplingInterval(samplingInterval);
        }

        /// <inheritdoc/>
        public void ApplyChanges()
        {
            // update all groups with any new items.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <inheritdoc/>
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
                m_samplingGroupManager?.Dispose();
            }
        }

        /// <inheritdoc/>
        public StatusCode DeleteMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeHandle handle)
        {
            (ServiceResult result, _) = RemoveRegistration(
                monitoredItem, handle is null ? NodeId.Null : handle.NodeId, sourceRelease: false);
            return result.StatusCode;
        }

        /// <inheritdoc/>
        public ServiceResult? ModifyMonitoredItem(
            ServerSystemContext context,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringFilter filterToUse,
            Range euRange,
            double samplingInterval,
            uint revisedQueueSize,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoredItemModifyRequest itemToModify)
        {
            // validate monitored item.
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem? existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return StatusCodes.BadMonitoredItemIdInvalid;
            }

            return m_samplingGroupManager.ModifyMonitoredItem(
                context.OperationContext!,
                timestampsToReturn,
                monitoredItem,
                itemToModify,
                filterToUse,
                euRange,
                revisedSamplingInterval: samplingInterval);
        }

        /// <inheritdoc/>
        /// <param name="context">The server system context.</param>
        /// <param name="monitoredItem">The monitored item.</param>
        /// <param name="monitoringMode">The monitoring mode.</param>
        /// <param name="handle">The node handle.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        public async ValueTask<(ServiceResult, MonitoringMode?)> SetMonitoringModeAsync(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode monitoringMode,
            NodeHandle handle,
            CancellationToken cancellationToken = default)
        {
            if (!MonitoredItems.TryGetValue(
                monitoredItem.Id,
                out IMonitoredItem? existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            if (!ReferenceEquals(monitoredItem, existingMonitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, null);
            }

            // update monitoring mode.
            MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);
            bool enabled = previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled;

            // the value read below is the immediate update after enabling, so the
            // sampling group does not take another one.
            if (enabled)
            {
                m_samplingGroupManager.MarkInitialValueQueued(monitoredItem);
            }

            m_samplingGroupManager.ModifyMonitoring(context.OperationContext!, monitoredItem);

            // need to provide an immediate update after enabling. For an item whose node
            // was deleted SetMonitoringMode already queued Bad_NodeIdUnknown, and the stale
            // node must not override it with a Good value.
            if (enabled &&
                monitoredItem is not IDetachableMonitoredItem { IsDeleted: true } &&
                monitoredItem is not MonitoredItem { UsesExternalValueSource: true })
            {
                var initialValue = new DataValue(
                    Variant.Null,
                    StatusCodes.BadWaitingForInitialData,
                    DateTimeUtc.MinValue,
                    DateTime.UtcNow);

                if (handle?.Node is NodeState node)
                {
                    ReadValueId read = monitoredItem.GetReadValueId();
                    (ServiceResult error, DataValue value) = await node.ReadAttributeAsync(
                        context,
                        monitoredItem.AttributeId,
                        read.ParsedIndexRange,
                        read.DataEncoding,
                        initialValue,
                        cancellationToken).ConfigureAwait(false);
                    initialValue = value;

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue = initialValue
                            .WithWrappedValue(default)
                            .WithStatus(error.StatusCode);
                    }
                    using OperationContext operation = MonitoredReadAuthorization.CreateContext(monitoredItem);
                    ServerSystemContext subscriber = context.Copy(operation);
                    ServiceResult authorization = await m_nodeManager.ValidateRolePermissionsAsync(
                        operation, node.NodeId, MonitoredReadAuthorization.PermissionFor(monitoredItem.AttributeId),
                        cancellationToken).ConfigureAwait(false);
                    if (!ServiceResult.IsBad(authorization))
                    {
                        authorization = await MonitoredReadAuthorization.ValidateAsync(
                            m_nodeManager, node, subscriber, monitoredItem.AttributeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (ServiceResult.IsBad(authorization))
                    {
                        initialValue = DataValue.FromStatusCode(authorization.StatusCode);
                    }
                }

                if (monitoredItem is MonitoredItem concreteItem &&
                    context.OperationContext is OperationContext operationContext)
                {
                    concreteItem.QueueValueIfCurrent(operationContext, initialValue, null);
                }
                else
                {
                    monitoredItem.QueueValue(initialValue, null);
                }
            }

            return (StatusCodes.Good, previousMode);
        }

        /// <inheritdoc/>
        public bool RestoreMonitoredItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerSystemContext context,
            NodeHandle handle,
            IStoredMonitoredItem storedMonitoredItem,
            IUserIdentity savedOwnerIdentity,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            out ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (MonitoredItems.ContainsKey(storedMonitoredItem.Id))
            {
                monitoredItem = null!;
                return false;
            }

            // create monitored item.
            monitoredItem = m_samplingGroupManager.RestoreMonitoredItem(
                handle,
                storedMonitoredItem,
                savedOwnerIdentity);

            ((IDetachableMonitoredItem)monitoredItem).Rebind(m_nodeManager, handle);

            // save monitored item.
            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem))
            {
                m_samplingGroupManager.StopMonitoring(monitoredItem);
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public (MonitoredNode2?, ServiceResult) SubscribeToEvents(
            ServerSystemContext context,
            NodeState source,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            MonitoredNode2? monitoredNode;
            // handle unsubscribe.
            if (unsubscribe)
            {
                // check for existing monitored node.
                if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
                {
                    return (null, StatusCodes.BadNodeIdUnknown);
                }

                monitoredNode.Remove(monitoredItem);

                // an all-events item can stay linked to other root notifiers; any
                // other event item is only linked to its own node.
                if (!monitoredItem.MonitoringAllEvents ||
                    !IsEventMonitoredItemLinked(monitoredItem.Id))
                {
                    MonitoredItems.TryRemove(monitoredItem.Id, out _);
                }

                // check if node is no longer being monitored.
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(source.NodeId);
                    monitoredNode.Dispose();
                }

                return (monitoredNode, ServiceResult.Good);
            }

            // only objects or views can be subscribed to.
            if (source is not BaseObjectState instance ||
                (instance.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
            {
                if (source is not ViewState view ||
                    (view.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return (null, StatusCodes.BadNotSupported);
                }
            }

            // check for existing monitored node.
            if (!MonitoredNodes.TryGetValue(source.NodeId, out monitoredNode))
            {
                MonitoredNodes[source.NodeId]
                    = monitoredNode = new MonitoredNode2(m_nodeManager, m_server, source,
                        IsMultiConsumerNode(source.NodeId));
            }

            // remove existing monitored items with the same Id prior to insertion in order to avoid duplicates
            // this is necessary since the SubscribeToEvents method is called also from ModifyMonitoredItemsForEvents
            monitoredNode.EventMonitoredItems.TryRemove(monitoredItem.Id, out _);

            // this links the node to specified monitored item and ensures all events
            // reported by the node are added to the monitored item's queue.
            monitoredNode.Add(monitoredItem);
            MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem);

            return (monitoredNode, ServiceResult.Good);
        }

        /// <inheritdoc/>
        (ServiceResult Result, bool Changed) IMonitoredItemManagerLifecycle.DetachMonitoredItem(
            ServerSystemContext context,
            ISampledDataChangeMonitoredItem monitoredItem,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache)
        {
            if (monitoredItem is not IDetachableMonitoredItem)
            {
                return (StatusCodes.BadNotSupported, false);
            }
            return RemoveRegistration(monitoredItem, monitoredItem.NodeId, sourceRelease: true);
        }

        /// <summary>
        /// Removes exact native ownership for both client deletion and generation source release.
        /// </summary>
        private (ServiceResult Result, bool Changed) RemoveRegistration(
            ISampledDataChangeMonitoredItem monitoredItem,
            NodeId sourceId,
            bool sourceRelease)
        {
            if (!MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existing))
            {
                return sourceRelease && monitoredItem is IDetachableMonitoredItem { IsDetached: true }
                    ? (ServiceResult.Good, false)
                    : (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            if (!ReferenceEquals(existing, monitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            bool isEvent = (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) != 0;
            if (isEvent)
            {
                if (!MonitoredNodes.TryGetValue(
                    sourceId,
                    out MonitoredNode2? monitoredNode) ||
                    !monitoredNode.EventMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IEventMonitoredItem? eventItem) ||
                    !ReferenceEquals(eventItem, monitoredItem))
                {
                    return (StatusCodes.BadInvalidState, false);
                }

                monitoredNode.Remove(eventItem);
                if (!monitoredNode.HasMonitoredItems)
                {
                    MonitoredNodes.Remove(sourceId);
                    monitoredNode.Dispose();
                }
            }
            else
            {
                m_samplingGroupManager.StopMonitoring(monitoredItem);
                if (sourceRelease)
                {
                    // The caller's manager gate preserves the verified native entry until removal below.
                    try
                    {
                        m_samplingGroupManager.ApplyChanges();
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        try
                        {
                            using var operation = new OperationContext(monitoredItem);
                            m_samplingGroupManager.MarkInitialValueQueued(monitoredItem);
                            m_samplingGroupManager.StartMonitoring(
                                operation, monitoredItem, monitoredItem.EffectiveIdentity);
                            m_samplingGroupManager.ApplyChanges();
                        }
                        catch (Exception restorationFailure) when (restorationFailure is not OutOfMemoryException)
                        {
                            throw new AggregateException(
                                "Sampling source release and restoration both failed.", failure, restorationFailure);
                        }
                        throw;
                    }
                }
            }

            bool keepAllEventsOwner = !sourceRelease &&
                monitoredItem is IEventMonitoredItem { MonitoringAllEvents: true } &&
                IsEventMonitoredItemLinked(monitoredItem.Id);
            if (!keepAllEventsOwner &&
                !((ICollection<KeyValuePair<uint, IMonitoredItem>>)MonitoredItems).Remove(
                    new KeyValuePair<uint, IMonitoredItem>(monitoredItem.Id, monitoredItem)))
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (sourceRelease)
            {
                ((IDetachableMonitoredItem)monitoredItem).BeginDetach();
            }
            return (ServiceResult.Good, true);
        }

        /// <inheritdoc/>
        (ServiceResult Result, bool Changed) IMonitoredItemManagerLifecycle.AttachMonitoredItem(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            Func<ISystemContext, NodeHandle, NodeState, NodeState> addNodeToComponentCache,
            Action<ISystemContext, NodeHandle> removeNodeFromComponentCache,
            long? sourceRevision)
        {
            if (monitoredItem is not IDetachableMonitoredItem lifecycle ||
                monitoredItem is not IMonitoredItemGenerationBinding binding)
            {
                return (StatusCodes.BadNotSupported, false);
            }
            long expectedRevision = sourceRevision ?? binding.SourceRevision;

            if (MonitoredItems.TryGetValue(monitoredItem.Id, out IMonitoredItem? existing))
            {
                if (!ReferenceEquals(existing, monitoredItem))
                {
                    return (StatusCodes.BadMonitoredItemIdInvalid, false);
                }

                return lifecycle.IsDetached
                    ? (StatusCodes.BadInvalidState, false)
                    : (ServiceResult.Good, false);
            }
            if (binding.SourceRevision != expectedRevision)
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (!lifecycle.IsDetached)
            {
                return (StatusCodes.BadInvalidState, false);
            }

            if (!MonitoredItems.TryAdd(monitoredItem.Id, monitoredItem))
            {
                return (StatusCodes.BadMonitoredItemIdInvalid, false);
            }

            bool isEvent = (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) != 0;
            MonitoredNode2? monitoredNode = null;
            bool monitoredNodeCreated = false;
            bool monitoredNodeLinked = false;
            bool monitoringStarted = false;
            bool attached = false;
            bool rollbackAttempted = false;
            bool sourceBound = false;
            long boundRevision = 0;
            try
            {
                if (!binding.TryBindSource(expectedRevision, m_nodeManager, handle, out boundRevision))
                {
                    return (StatusCodes.BadInvalidState, false);
                }
                sourceBound = true;
                if (isEvent)
                {
                    if (!MonitoredNodes.TryGetValue(handle.NodeId, out monitoredNode))
                    {
                        MonitoredNodes[handle.NodeId] = monitoredNode = new MonitoredNode2(
                            m_nodeManager,
                            m_server,
                            handle.Node,
                            IsMultiConsumerNode(handle.NodeId));
                        monitoredNodeCreated = true;
                    }
                    else
                    {
                        ServiceResult rebindResult = monitoredNode.Rebind(m_nodeManager, handle.Node);
                        if (ServiceResult.IsBad(rebindResult))
                        {
                            return (rebindResult, false);
                        }
                    }

                    handle.MonitoredNode = monitoredNode;
                    if (monitoredNode.EventMonitoredItems.TryGetValue(
                        monitoredItem.Id,
                        out IEventMonitoredItem? linkedEventItem))
                    {
                        return ReferenceEquals(linkedEventItem, monitoredItem)
                            ? (StatusCodes.BadInvalidState, false)
                            : (StatusCodes.BadMonitoredItemIdInvalid, false);
                    }

                    monitoredNode.Add((IEventMonitoredItem)monitoredItem);
                    monitoredNodeLinked = true;
                }
                else
                {
                    m_samplingGroupManager.StartMonitoring(
                        // Either borrowed from the caller or created for the sampling group, which
                        // keeps using it, so it is not disposed here. It tracks no request.
#pragma warning disable CA2000
                        context.OperationContext ?? new OperationContext(monitoredItem),
#pragma warning restore CA2000
                        monitoredItem,
                        monitoredItem.EffectiveIdentity);
                    monitoringStarted = true;
                }

                if (!isEvent)
                {
                    m_samplingGroupManager.ApplyChanges();
                }
                attached = true;
                return (ServiceResult.Good, true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                rollbackAttempted = true;
                try
                {
                    RollbackRegistration();
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(failure, cleanupFailure);
                }
                throw;
            }
            finally
            {
                if (!attached && !rollbackAttempted)
                {
                    RollbackRegistration();
                }
            }

            void RollbackRegistration()
            {
                try
                {
                    if (monitoringStarted)
                    {
                        m_samplingGroupManager.StopMonitoring(monitoredItem);
                        m_samplingGroupManager.ApplyChanges();
                    }
                }
                finally
                {
                    if (monitoredNodeLinked)
                    {
                        monitoredNode!.Remove((IEventMonitoredItem)monitoredItem);
                    }

                    MonitoredItems.TryRemove(monitoredItem.Id, out _);
                    if (sourceBound)
                    {
                        binding.TryClearSource(boundRevision);
                    }

                    if (monitoredNodeCreated)
                    {
                        MonitoredNodes.Remove(handle.NodeId);
                        monitoredNode?.Dispose();
                    }
                }
            }
        }

        private bool IsEventMonitoredItemLinked(uint monitoredItemId)
        {
            foreach (MonitoredNode2 monitoredNode in MonitoredNodes.Values)
            {
                if (monitoredNode.EventMonitoredItems.ContainsKey(monitoredItemId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsMultiConsumerNode(NodeId nodeId)
        {
            return m_nodeManager.IsMultipleEventConsumerNode(nodeId);
        }

        private readonly IAsyncNodeManager m_nodeManager;
        private readonly IServerInternal m_server;
        private readonly SamplingGroupManager m_samplingGroupManager;
    }
}
