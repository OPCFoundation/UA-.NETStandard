/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Invoked when a node has been added to the address space of the node
    /// manager that exposed the fluent builder. Fires once per matched node
    /// after wiring is applied.
    /// </summary>
    public delegate void NodeLifecycleHandler(
        ISystemContext context,
        NodeState node);

    /// <summary>
    /// Per-node handler invoked from <c>CustomNodeManager2.HistoryRead</c>.
    /// The implementation is expected to populate <paramref name="result"/>
    /// for the supplied <paramref name="nodeToRead"/> and return the
    /// operation status.
    /// </summary>
    public delegate ServiceResult HistoryReadHandler(
        ISystemContext context,
        NodeState source,
        HistoryReadDetails details,
        TimestampsToReturn timestampsToReturn,
        bool releaseContinuationPoints,
        HistoryReadValueId nodeToRead,
        HistoryReadResult result);

    /// <summary>
    /// Per-node handler invoked from <c>CustomNodeManager2.HistoryUpdate</c>.
    /// The implementation is expected to populate <paramref name="result"/>
    /// for the supplied <paramref name="nodeToUpdate"/> and return the
    /// operation status.
    /// </summary>
    public delegate ServiceResult HistoryUpdateHandler(
        ISystemContext context,
        NodeState source,
        HistoryUpdateDetails nodeToUpdate,
        HistoryUpdateResult result);

    /// <summary>
    /// Per-source handler that mirrors <see cref="NodeState.OnConditionRefresh"/>.
    /// The fluent builder wires this delegate directly to that slot, so the
    /// signature matches the existing dispatch path used by
    /// <c>CustomNodeManager2.ConditionRefresh</c>.
    /// </summary>
    public delegate void ConditionRefreshHandler(
        ISystemContext context,
        NodeState source,
        List<IFilterTarget> refreshEvents);

    /// <summary>
    /// Invoked once a sampled data-change <see cref="ISampledDataChangeMonitoredItem"/>
    /// has been successfully created for the wired source node. Allows the
    /// user to attach extra state, sampling sources, or custom queues to
    /// the item.
    /// </summary>
    public delegate void MonitoredItemCreatedHandler(
        ISystemContext context,
        NodeState source,
        ISampledDataChangeMonitoredItem monitoredItem);

    /// <summary>
    /// Invoked after a monitored item has been modified successfully.
    /// </summary>
    public delegate ValueTask MonitoredItemModifiedHandler(
        ISystemContext context,
        NodeState source,
        ISampledDataChangeMonitoredItem monitoredItem,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked after a monitored item has been deleted successfully.
    /// </summary>
    public delegate ValueTask MonitoredItemDeletedHandler(
        ISystemContext context,
        NodeState source,
        ISampledDataChangeMonitoredItem monitoredItem,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when an existing monitored item attaches to or detaches from a
    /// source during node-manager lifecycle handoff.
    /// </summary>
    /// <param name="context">The system context.</param>
    /// <param name="source">The node state.</param>
    /// <param name="monitoredItem">The monitored item.</param>
    /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
    public delegate ValueTask MonitoredItemAttachmentHandler(
        ISystemContext context,
        NodeState source,
        ISampledDataChangeMonitoredItem monitoredItem,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked after a monitored item's monitoring mode changes.
    /// </summary>
    public delegate ValueTask MonitoringModeChangedHandler(
        ISystemContext context,
        NodeState source,
        ISampledDataChangeMonitoredItem monitoredItem,
        MonitoringMode previousMode,
        MonitoringMode monitoringMode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked before the stack creates a data-change monitored item.
    /// </summary>
    public delegate ValueTask<MonitoredItemCreateDecision> MonitoredItemCreatingHandler(
        MonitoredItemCreateContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when a node transitions between zero and one active
    /// data-change subscribers.
    /// </summary>
    public delegate ValueTask MonitoredSourceLifecycleHandler(
        ISystemContext context,
        NodeState source,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked after a successful monitored-item create or delete batch.
    /// </summary>
    public delegate ValueTask MonitoredItemsBatchHandler(
        ISystemContext context,
        ArrayOf<IMonitoredItem> monitoredItems,
        CancellationToken cancellationToken);

    /// <summary>
    /// Invoked when an event is reported by the wired source node. The
    /// runtime hooks this delegate up via
    /// <see cref="NodeState.OnReportEvent"/>.
    /// </summary>
    public delegate void EventNotificationHandler(
        ISystemContext context,
        NodeState source,
        IFilterTarget @event);
}
