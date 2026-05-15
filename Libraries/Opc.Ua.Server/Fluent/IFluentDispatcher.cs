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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Manager-level dispatch surface populated by the fluent
    /// <see cref="INodeManagerBuilder"/>. A node manager's overrides for
    /// <c>HistoryRead</c> and <c>HistoryUpdate</c> route per-node calls
    /// through this interface; for nodes the dispatcher does not own a
    /// handler for, callers fall back to the base behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatcher is intentionally additive — it does not modify
    /// <c>CustomNodeManager2</c>. Generated <c>NodeManagerBase</c> classes
    /// (Phase 2) override the relevant virtuals and delegate per-node
    /// dispatch here. Hand-written managers can do the same to opt in.
    /// </para>
    /// <para>
    /// History-related <c>TryHandle*</c> methods follow the standard
    /// <see langword="bool"/> + <c>out</c> pattern: return <c>false</c>
    /// when no handler is registered for the node so that the caller can
    /// invoke base behavior.
    /// </para>
    /// <para>
    /// <c>OnConditionRefresh</c> is intentionally absent: the per-node
    /// handler wires directly to <see cref="NodeState.OnConditionRefresh"/>
    /// at <c>Configure</c> time, so the base manager's existing
    /// <c>ConditionRefresh</c> dispatch invokes it without any extra hop.
    /// </para>
    /// <para>
    /// Async history is intentionally deferred until the stack ships an
    /// async-aware history dispatch path; v1 only exposes the synchronous
    /// surface.
    /// </para>
    /// </remarks>
    public interface IFluentDispatcher
    {
        /// <summary>
        /// Dispatches a per-node history read. Returns <c>false</c> when
        /// no handler is registered for the supplied node.
        /// </summary>
        bool TryHandleHistoryRead(
            ISystemContext context,
            NodeState node,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            out ServiceResult status);

        /// <summary>
        /// Dispatches a per-node history update. Returns <c>false</c> when
        /// no handler is registered for the supplied node.
        /// </summary>
        bool TryHandleHistoryUpdate(
            ISystemContext context,
            NodeState node,
            HistoryUpdateDetails nodeToUpdate,
            HistoryUpdateResult result,
            out ServiceResult status);

        /// <summary>
        /// Notifies registered <c>OnMonitoredItemCreated</c> handlers for
        /// the supplied source node. Invoke from a manager's
        /// <c>OnMonitoredItemCreated</c> override (which fires for both
        /// initial create and restore paths). No-op when no handler is
        /// registered for the source.
        /// </summary>
        void NotifyMonitoredItemCreated(
            ISystemContext context,
            NodeState source,
            ISampledDataChangeMonitoredItem monitoredItem);

        /// <summary>
        /// Notifies registered <c>OnNodeAdded</c> handlers for the
        /// supplied node. The generated <c>NodeManagerBase</c> replays
        /// this for every predefined node loaded prior to <c>Configure</c>
        /// completing, then forwards from <c>AddPredefinedNode</c> for
        /// later dynamic adds.
        /// </summary>
        void NotifyNodeAdded(ISystemContext context, NodeState node);

        /// <summary>
        /// Notifies registered <c>OnNodeRemoved</c> handlers for the
        /// supplied node. Forwarded from <c>RemovePredefinedNode</c>.
        /// </summary>
        void NotifyNodeRemoved(ISystemContext context, NodeState node);
    }
}
