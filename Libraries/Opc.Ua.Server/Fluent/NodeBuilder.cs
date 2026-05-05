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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Default <see cref="INodeBuilder"/> implementation. Wires variable,
    /// method, event, and condition-refresh callbacks directly onto the
    /// resolved <see cref="NodeState"/>; routes manager-level callbacks
    /// (history, monitored-item, lifecycle) through the parent
    /// <see cref="NodeManagerBuilder"/>'s dispatcher.
    /// </summary>
    internal class NodeBuilder : INodeBuilder
    {
        public NodeBuilder(NodeManagerBuilder parent, NodeState node)
        {
            m_parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Node = node ?? throw new ArgumentNullException(nameof(node));
        }

        /// <inheritdoc/>
        public NodeState Node { get; }

        /// <inheritdoc/>
        public INodeManagerBuilder Builder => m_parent;

        /// <inheritdoc/>
        public INodeBuilder<TState> As<TState>() where TState : NodeState
        {
            if (Node is not TState typed)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Node '{0}' is of type {1}, which is not assignable to {2}.",
                    Node.BrowseName,
                    Node.GetType().Name,
                    typeof(TState).Name);
            }

            return new NodeBuilder<TState>(m_parent, typed);
        }

        /// <inheritdoc/>
        public INodeBuilder OnRead(NodeValueEventHandler handler)
        {
            BaseVariableState v = RequireVariable("OnRead");
            ThrowIfSlotOccupied(v.OnReadValue, "OnRead");
            v.OnReadValue = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnRead(NodeValueSimpleEventHandler handler)
        {
            BaseVariableState v = RequireVariable("OnSimpleRead");
            ThrowIfSlotOccupied(v.OnSimpleReadValue, "OnSimpleRead");
            v.OnSimpleReadValue = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnWrite(NodeValueEventHandler handler)
        {
            BaseVariableState v = RequireVariable("OnWrite");
            ThrowIfSlotOccupied(v.OnWriteValue, "OnWrite");
            v.OnWriteValue = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnWrite(NodeValueSimpleEventHandler handler)
        {
            BaseVariableState v = RequireVariable("OnSimpleWrite");
            ThrowIfSlotOccupied(v.OnSimpleWriteValue, "OnSimpleWrite");
            v.OnSimpleWriteValue = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnCall(GenericMethodCalledEventHandler2 handler)
        {
            MethodState m = RequireMethod("OnCall");
            ThrowIfSlotOccupied(m.OnCallMethod2, "OnCall");
            m.OnCallMethod2 = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler)
        {
            MethodState m = RequireMethod("OnCallAsync");
            ThrowIfSlotOccupied(m.OnCallMethod2Async, "OnCallAsync");
            m.OnCallMethod2Async = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnNodeAdded(NodeLifecycleHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_parent.RegisterNodeAdded(Node, handler);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnNodeRemoved(NodeLifecycleHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_parent.RegisterNodeRemoved(Node, handler);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnHistoryRead(HistoryReadHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_parent.RegisterHistoryRead(Node, handler);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnHistoryUpdate(HistoryUpdateHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_parent.RegisterHistoryUpdate(Node, handler);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnConditionRefresh(ConditionRefreshHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            ThrowIfSlotOccupied(Node.OnConditionRefresh, "OnConditionRefresh");
            // Direct wire — signature matches NodeStateConditionRefreshEventHandler.
            Node.OnConditionRefresh = (ctx, n, evts) => handler(ctx, n, evts);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnMonitoredItemCreated(MonitoredItemCreatedHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            m_parent.RegisterMonitoredItemCreated(Node, handler);
            return this;
        }

        /// <inheritdoc/>
        public INodeBuilder OnEvent(EventNotificationHandler handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            // OnReportEvent is also used by CustomNodeManager2 for root notifier
            // wiring — replacing it would silently break server event propagation.
            ThrowIfSlotOccupied(Node.OnReportEvent, "OnEvent (NodeState.OnReportEvent)");
            Node.OnReportEvent = (ctx, n, e) => handler(ctx, n, e);
            return this;
        }

        private BaseVariableState RequireVariable(string what)
        {
            if (Node is not BaseVariableState v)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Cannot wire {0} on node '{1}': not a BaseVariableState (actual: {2}).",
                    what,
                    Node.BrowseName,
                    Node.GetType().Name);
            }

            return v;
        }

        private MethodState RequireMethod(string what)
        {
            if (Node is not MethodState m)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Cannot wire {0} on node '{1}': not a MethodState (actual: {2}).",
                    what,
                    Node.BrowseName,
                    Node.GetType().Name);
            }

            return m;
        }

        private void ThrowIfSlotOccupied(Delegate existing, string what)
        {
            if (existing != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node '{0}' (id '{1}') already has a {2} handler assigned.",
                    Node.BrowseName,
                    Node.NodeId,
                    what);
            }
        }

        private readonly NodeManagerBuilder m_parent;
    }

    /// <summary>
    /// Strongly-typed <see cref="INodeBuilder{TState}"/> wrapper. Inherits
    /// all <c>On*</c> behavior from the non-generic
    /// <see cref="NodeBuilder"/>; the only added value is a typed
    /// <see cref="Node"/> property.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    internal sealed class NodeBuilder<TState> : NodeBuilder, INodeBuilder<TState>
        where TState : NodeState
    {
        public NodeBuilder(NodeManagerBuilder parent, TState node)
            : base(parent, node)
        {
            Node = node;
        }

        /// <inheritdoc/>
        public new TState Node { get; }
    }
}
