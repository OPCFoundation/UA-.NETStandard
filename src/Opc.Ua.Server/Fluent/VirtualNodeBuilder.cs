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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Determines whether a virtual-node family recognizes a
    /// <see cref="NodeId"/>.
    /// </summary>
    public delegate bool VirtualNodeIdPredicate(NodeId nodeId);

    /// <summary>
    /// Materializes a virtual node for one service operation.
    /// </summary>
    public delegate ValueTask<NodeState?> VirtualNodeResolver(
        ISystemContext context,
        NodeId nodeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Configures callbacks shared by a family of nodes that are
    /// materialized on demand.
    /// </summary>
    public interface IVirtualNodeBuilder
    {
        /// <summary>
        /// Wires <see cref="BaseVariableState.OnReadValue"/>.
        /// </summary>
        IVirtualNodeBuilder OnRead(NodeValueEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleReadValue"/>.
        /// </summary>
        IVirtualNodeBuilder OnRead(NodeValueSimpleEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnReadValueAsync"/>.
        /// </summary>
        IVirtualNodeBuilder OnRead(NodeValueEventHandlerAsync handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleReadValueAsync"/>.
        /// </summary>
        IVirtualNodeBuilder OnRead(NodeValueSimpleEventHandlerAsync handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnWriteValue"/>.
        /// </summary>
        IVirtualNodeBuilder OnWrite(NodeValueEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleWriteValue"/>.
        /// </summary>
        IVirtualNodeBuilder OnWrite(NodeValueSimpleEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnWriteValueAsync"/>.
        /// </summary>
        IVirtualNodeBuilder OnWrite(NodeValueWriteEventHandlerAsync handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleWriteValueAsync"/>.
        /// </summary>
        IVirtualNodeBuilder OnWrite(NodeValueSimpleWriteEventHandlerAsync handler);

        /// <summary>
        /// Wires <see cref="MethodState.OnCallMethod2"/>.
        /// </summary>
        IVirtualNodeBuilder OnCall(GenericMethodCalledEventHandler2 handler);

        /// <summary>
        /// Wires <see cref="MethodState.OnCallMethod2Async"/>.
        /// </summary>
        IVirtualNodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler);

        /// <summary>
        /// Routes history reads for the virtual family.
        /// </summary>
        IVirtualNodeBuilder OnHistoryRead(HistoryReadHandler handler);

        /// <summary>
        /// Routes history updates for the virtual family.
        /// </summary>
        IVirtualNodeBuilder OnHistoryUpdate(HistoryUpdateHandler handler);

        /// <summary>
        /// Wires <see cref="NodeState.OnConditionRefresh"/>.
        /// </summary>
        IVirtualNodeBuilder OnConditionRefresh(ConditionRefreshHandler handler);

        /// <summary>
        /// Invoked after a monitored item is created for a virtual node.
        /// </summary>
        IVirtualNodeBuilder OnMonitoredItemCreated(MonitoredItemCreatedHandler handler);

        /// <summary>
        /// Participates in monitored-item creation for the virtual family.
        /// </summary>
        IVirtualNodeBuilder OnCreateMonitoredItem(MonitoredItemCreatingHandler handler);

        /// <summary>
        /// Invoked after a monitored item has been modified successfully.
        /// </summary>
        IVirtualNodeBuilder OnMonitoredItemModified(MonitoredItemModifiedHandler handler);

        /// <summary>
        /// Invoked after a monitored item has been deleted successfully.
        /// </summary>
        IVirtualNodeBuilder OnMonitoredItemDeleted(MonitoredItemDeletedHandler handler);

        /// <summary>
        /// Invoked after a monitored item's monitoring mode changes.
        /// </summary>
        IVirtualNodeBuilder OnMonitoringModeChanged(MonitoringModeChangedHandler handler);

        /// <summary>
        /// Wires <see cref="NodeState.OnReportEvent"/>.
        /// </summary>
        IVirtualNodeBuilder OnEvent(EventNotificationHandler handler);

        /// <summary>
        /// Wires <see cref="NodeState.OnCreateBrowser"/>.
        /// </summary>
        IVirtualNodeBuilder OnCreateBrowser(NodeStateCreateBrowserEventHandler handler);
    }

    /// <summary>
    /// Registers virtual-node families on a fluent node manager.
    /// </summary>
    public static class VirtualNodeBuilderExtensions
    {
        /// <summary>
        /// Registers a family of nodes recognized cheaply by
        /// <paramref name="predicate"/> and materialized asynchronously by
        /// <paramref name="resolver"/>.
        /// </summary>
        public static IVirtualNodeBuilder ResolveNodes(
            this INodeManagerBuilder builder,
            VirtualNodeIdPredicate predicate,
            VirtualNodeResolver resolver)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(builder, "ResolveNodes");
            return concrete.RegisterVirtualNodes(predicate, resolver);
        }
    }

    internal sealed class VirtualNodeRegistration : IVirtualNodeBuilder
    {
        public VirtualNodeRegistration(
            NodeManagerBuilder owner,
            VirtualNodeIdPredicate predicate,
            VirtualNodeResolver resolver)
        {
            Owner = owner;
            Predicate = predicate;
            Resolver = resolver;
        }

        public NodeManagerBuilder Owner { get; }

        public VirtualNodeIdPredicate Predicate { get; }

        public VirtualNodeResolver Resolver { get; }

        public HistoryReadHandler? HistoryRead { get; private set; }

        public HistoryUpdateHandler? HistoryUpdate { get; private set; }

        public MonitoredItemCreatedHandler? MonitoredItemCreated { get; private set; }

        public MonitoredItemCreatingHandler? MonitoredItemCreating { get; private set; }

        public MonitoredItemModifiedHandler? MonitoredItemModified { get; private set; }

        public MonitoredItemDeletedHandler? MonitoredItemDeleted { get; private set; }

        public MonitoringModeChangedHandler? MonitoringModeChanged { get; private set; }

        public IVirtualNodeBuilder OnRead(NodeValueEventHandler handler)
        {
            m_read = SetOnce(m_read, handler, "OnRead");
            return this;
        }

        public IVirtualNodeBuilder OnRead(NodeValueSimpleEventHandler handler)
        {
            m_simpleRead = SetOnce(m_simpleRead, handler, "OnSimpleRead");
            return this;
        }

        public IVirtualNodeBuilder OnRead(NodeValueEventHandlerAsync handler)
        {
            m_readAsync = SetOnce(m_readAsync, handler, "OnReadAsync");
            return this;
        }

        public IVirtualNodeBuilder OnRead(NodeValueSimpleEventHandlerAsync handler)
        {
            m_simpleReadAsync = SetOnce(m_simpleReadAsync, handler, "OnSimpleReadAsync");
            return this;
        }

        public IVirtualNodeBuilder OnWrite(NodeValueEventHandler handler)
        {
            m_write = SetOnce(m_write, handler, "OnWrite");
            return this;
        }

        public IVirtualNodeBuilder OnWrite(NodeValueSimpleEventHandler handler)
        {
            m_simpleWrite = SetOnce(m_simpleWrite, handler, "OnSimpleWrite");
            return this;
        }

        public IVirtualNodeBuilder OnWrite(NodeValueWriteEventHandlerAsync handler)
        {
            m_writeAsync = SetOnce(m_writeAsync, handler, "OnWriteAsync");
            return this;
        }

        public IVirtualNodeBuilder OnWrite(NodeValueSimpleWriteEventHandlerAsync handler)
        {
            m_simpleWriteAsync = SetOnce(m_simpleWriteAsync, handler, "OnSimpleWriteAsync");
            return this;
        }

        public IVirtualNodeBuilder OnCall(GenericMethodCalledEventHandler2 handler)
        {
            m_call = SetOnce(m_call, handler, "OnCall");
            return this;
        }

        public IVirtualNodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler)
        {
            m_callAsync = SetOnce(m_callAsync, handler, "OnCallAsync");
            return this;
        }

        public IVirtualNodeBuilder OnHistoryRead(HistoryReadHandler handler)
        {
            HistoryRead = SetOnce(HistoryRead, handler, "OnHistoryRead");
            return this;
        }

        public IVirtualNodeBuilder OnHistoryUpdate(HistoryUpdateHandler handler)
        {
            HistoryUpdate = SetOnce(HistoryUpdate, handler, "OnHistoryUpdate");
            return this;
        }

        public IVirtualNodeBuilder OnConditionRefresh(ConditionRefreshHandler handler)
        {
            m_conditionRefresh = SetOnce(
                m_conditionRefresh,
                handler,
                "OnConditionRefresh");
            return this;
        }

        public IVirtualNodeBuilder OnMonitoredItemCreated(
            MonitoredItemCreatedHandler handler)
        {
            MonitoredItemCreated = SetOnce(
                MonitoredItemCreated,
                handler,
                "OnMonitoredItemCreated");
            return this;
        }

        public IVirtualNodeBuilder OnCreateMonitoredItem(
            MonitoredItemCreatingHandler handler)
        {
            MonitoredItemCreating = SetOnce(
                MonitoredItemCreating,
                handler,
                "OnCreateMonitoredItem");
            return this;
        }

        public IVirtualNodeBuilder OnMonitoredItemModified(
            MonitoredItemModifiedHandler handler)
        {
            MonitoredItemModified = SetOnce(
                MonitoredItemModified,
                handler,
                "OnMonitoredItemModified");
            return this;
        }

        public IVirtualNodeBuilder OnMonitoredItemDeleted(
            MonitoredItemDeletedHandler handler)
        {
            MonitoredItemDeleted = SetOnce(
                MonitoredItemDeleted,
                handler,
                "OnMonitoredItemDeleted");
            return this;
        }

        public IVirtualNodeBuilder OnMonitoringModeChanged(
            MonitoringModeChangedHandler handler)
        {
            MonitoringModeChanged = SetOnce(
                MonitoringModeChanged,
                handler,
                "OnMonitoringModeChanged");
            return this;
        }

        public IVirtualNodeBuilder OnEvent(EventNotificationHandler handler)
        {
            m_event = SetOnce(m_event, handler, "OnEvent");
            return this;
        }

        public IVirtualNodeBuilder OnCreateBrowser(
            NodeStateCreateBrowserEventHandler handler)
        {
            m_createBrowser = SetOnce(
                m_createBrowser,
                handler,
                "OnCreateBrowser");
            return this;
        }

        public void Apply(NodeState node)
        {
            lock (m_applyLock)
            {
                if (m_applied.TryGetValue(node, out _))
                {
                    return;
                }

                if (HasVariableHandlers())
                {
                    if (node is not BaseVariableState variable)
                    {
                        throw CreateTypeMismatch(node, "variable");
                    }

                    SetSlot(ref variable.OnReadValue, m_read, node, "OnRead");
                    SetSlot(
                        ref variable.OnSimpleReadValue,
                        m_simpleRead,
                        node,
                        "OnSimpleRead");
                    SetSlot(
                        ref variable.OnReadValueAsync,
                        m_readAsync,
                        node,
                        "OnReadAsync");
                    SetSlot(
                        ref variable.OnSimpleReadValueAsync,
                        m_simpleReadAsync,
                        node,
                        "OnSimpleReadAsync");
                    SetSlot(ref variable.OnWriteValue, m_write, node, "OnWrite");
                    SetSlot(
                        ref variable.OnSimpleWriteValue,
                        m_simpleWrite,
                        node,
                        "OnSimpleWrite");
                    SetSlot(
                        ref variable.OnWriteValueAsync,
                        m_writeAsync,
                        node,
                        "OnWriteAsync");
                    SetSlot(
                        ref variable.OnSimpleWriteValueAsync,
                        m_simpleWriteAsync,
                        node,
                        "OnSimpleWriteAsync");
                }

                if (m_call != null || m_callAsync != null)
                {
                    if (node is not MethodState method)
                    {
                        throw CreateTypeMismatch(node, "method");
                    }

                    SetSlot(ref method.OnCallMethod2, m_call, node, "OnCall");
                    SetSlot(
                        ref method.OnCallMethod2Async,
                        m_callAsync,
                        node,
                        "OnCallAsync");
                }

                if (m_conditionRefresh != null)
                {
                    if (node.OnConditionRefresh != null)
                    {
                        throw CreateOccupiedSlot(node, "OnConditionRefresh");
                    }
                    node.OnConditionRefresh = (context, source, events) =>
                        m_conditionRefresh(context, source, events);
                }

                if (m_event != null)
                {
                    if (node.OnReportEvent != null)
                    {
                        throw CreateOccupiedSlot(node, "OnEvent");
                    }
                    node.OnReportEvent = (context, source, @event) =>
                        m_event(context, source, @event);
                }

                if (m_createBrowser != null)
                {
                    if (node.OnCreateBrowser != null)
                    {
                        throw CreateOccupiedSlot(node, "OnCreateBrowser");
                    }
                    node.OnCreateBrowser = m_createBrowser;
                }

                m_applied.Add(node, AppliedMarker.Instance);
            }
        }

        private bool HasVariableHandlers()
        {
            return m_read != null ||
                m_simpleRead != null ||
                m_readAsync != null ||
                m_simpleReadAsync != null ||
                m_write != null ||
                m_simpleWrite != null ||
                m_writeAsync != null ||
                m_simpleWriteAsync != null;
        }

        private static T SetOnce<T>(T? current, T handler, string name)
            where T : Delegate
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (current != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The virtual-node family already has a {0} handler registered.",
                    name);
            }
            return handler;
        }

        private static void SetSlot<T>(
            ref T? slot,
            T? handler,
            NodeState node,
            string name)
            where T : Delegate
        {
            if (handler == null)
            {
                return;
            }
            if (slot != null)
            {
                throw CreateOccupiedSlot(node, name);
            }
            slot = handler;
        }

        private static ServiceResultException CreateOccupiedSlot(
            NodeState node,
            string name)
        {
            return ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Virtual node '{0}' (id '{1}') already has a {2} handler.",
                node.BrowseName,
                node.NodeId,
                name);
        }

        private static ServiceResultException CreateTypeMismatch(
            NodeState node,
            string expected)
        {
            return ServiceResultException.Create(
                StatusCodes.BadTypeMismatch,
                "Virtual node '{0}' (id '{1}') is not a {2} node.",
                node.BrowseName,
                node.NodeId,
                expected);
        }

        private sealed class AppliedMarker
        {
            public static readonly AppliedMarker Instance = new();
        }

        private readonly ConditionalWeakTable<NodeState, AppliedMarker> m_applied = new();
        private readonly Lock m_applyLock = new();
        private NodeValueEventHandler? m_read;
        private NodeValueSimpleEventHandler? m_simpleRead;
        private NodeValueEventHandlerAsync? m_readAsync;
        private NodeValueSimpleEventHandlerAsync? m_simpleReadAsync;
        private NodeValueEventHandler? m_write;
        private NodeValueSimpleEventHandler? m_simpleWrite;
        private NodeValueWriteEventHandlerAsync? m_writeAsync;
        private NodeValueSimpleWriteEventHandlerAsync? m_simpleWriteAsync;
        private GenericMethodCalledEventHandler2? m_call;
        private GenericMethodCalledEventHandler2Async? m_callAsync;
        private ConditionRefreshHandler? m_conditionRefresh;
        private EventNotificationHandler? m_event;
        private NodeStateCreateBrowserEventHandler? m_createBrowser;
    }
}
