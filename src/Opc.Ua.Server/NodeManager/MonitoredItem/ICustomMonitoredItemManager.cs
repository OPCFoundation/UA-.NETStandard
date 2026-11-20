// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Server
{
    internal interface ICustomMonitoredItemManager
    {
        /// <summary>
        /// Creates a custom monitored item from the factory.
        /// </summary>
        /// <remarks>
        /// <paramref name="initialValueQueued"/> is true when the node manager reads
        /// and queues the initial value itself after the item is created, so the
        /// manager must not take an additional immediate sample.
        /// </remarks>
        ISampledDataChangeMonitoredItem CreateCustomMonitoredItem(
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
            bool initialValueQueued);
    }

    internal static class CustomMonitoredItemValidation
    {
        public static void Validate(
            MonitoredItemFactoryContext context,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The custom monitored-item factory returned null.");
            }
            if (monitoredItem.Id != context.MonitoredItemId ||
                monitoredItem.SubscriptionId != context.SubscriptionId ||
                monitoredItem.NodeId != context.Handle.NodeId ||
                !ReferenceEquals(monitoredItem.NodeManager, context.NodeManager) ||
                !ReferenceEquals(monitoredItem.ManagerHandle, context.Handle) ||
                (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.DataChange) == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The custom monitored-item factory returned an item whose identity or ownership " +
                    "does not match the stack-supplied creation context.");
            }
        }
    }
}
