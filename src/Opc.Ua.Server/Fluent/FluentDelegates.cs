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

using System.Collections.Generic;

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
    /// Invoked when an event is reported by the wired source node. The
    /// runtime hooks this delegate up via
    /// <see cref="NodeState.OnReportEvent"/>.
    /// </summary>
    public delegate void EventNotificationHandler(
        ISystemContext context,
        NodeState source,
        IFilterTarget @event);
}
