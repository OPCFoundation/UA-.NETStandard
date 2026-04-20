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
    /// Per-node fluent surface returned by
    /// <see cref="INodeManagerBuilder.Node(string)"/>. All <c>On*</c>
    /// methods return the same builder so calls can be chained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Variable-only callbacks (e.g. <see cref="OnRead"/>) and method-only
    /// callbacks (e.g. <see cref="OnCall"/>) throw
    /// <see cref="ServiceResultException"/> with
    /// <see cref="StatusCodes.BadInvalidArgument"/> when invoked on a node
    /// of an incompatible class. Use <see cref="As{TState}"/> to obtain a
    /// strongly-typed view that surfaces the right callbacks at compile
    /// time.
    /// </para>
    /// <para>
    /// All callbacks replace any previously assigned handler on the
    /// underlying <see cref="NodeState"/>; they do not add to a list.
    /// Wiring the same node twice with the same callback category is an
    /// error and throws.
    /// </para>
    /// </remarks>
    public interface INodeBuilder
    {
        /// <summary>
        /// The resolved <see cref="NodeState"/>. Exposed so callers can
        /// reach attributes that are not surfaced by the builder (for
        /// example, setting <c>UserAccessLevel</c> programmatically).
        /// </summary>
        NodeState Node { get; }

        /// <summary>
        /// The owning builder. Useful when you want to terminate a chain
        /// and resolve another node without storing the parent reference.
        /// </summary>
        INodeManagerBuilder Builder { get; }

        /// <summary>
        /// Returns a strongly-typed view of the same node. Throws if the
        /// resolved node is not assignable to <typeparamref name="TState"/>.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        INodeBuilder<TState> As<TState>() where TState : NodeState;

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnReadValue"/>.
        /// </summary>
        INodeBuilder OnRead(NodeValueEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleReadValue"/>.
        /// </summary>
        INodeBuilder OnRead(NodeValueSimpleEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnWriteValue"/>.
        /// </summary>
        INodeBuilder OnWrite(NodeValueEventHandler handler);

        /// <summary>
        /// Wires <see cref="BaseVariableState.OnSimpleWriteValue"/>.
        /// </summary>
        INodeBuilder OnWrite(NodeValueSimpleEventHandler handler);

        /// <summary>
        /// Wires <see cref="MethodState.OnCallMethod2"/>.
        /// </summary>
        INodeBuilder OnCall(GenericMethodCalledEventHandler2 handler);

        /// <summary>
        /// Wires <see cref="MethodState.OnCallMethod2Async"/>.
        /// </summary>
        INodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler);

        /// <summary>
        /// Invoked exactly once after the node has been added to the
        /// address space and all sibling builder wiring has been applied.
        /// </summary>
        INodeBuilder OnNodeAdded(NodeLifecycleHandler handler);

        /// <summary>
        /// Invoked when the node manager removes the node from the address
        /// space (including manager dispose).
        /// </summary>
        INodeBuilder OnNodeRemoved(NodeLifecycleHandler handler);

        /// <summary>
        /// Routes <c>CustomNodeManager2.HistoryRead</c> requests targeting
        /// the resolved node to <paramref name="handler"/>.
        /// </summary>
        INodeBuilder OnHistoryRead(HistoryReadHandler handler);

        /// <summary>
        /// Routes <c>CustomNodeManager2.HistoryUpdate</c> requests targeting
        /// the resolved node to <paramref name="handler"/>.
        /// </summary>
        INodeBuilder OnHistoryUpdate(HistoryUpdateHandler handler);

        /// <summary>
        /// Routes <c>CustomNodeManager2.ConditionRefresh</c> invocations
        /// whose monitored item targets the resolved node to
        /// <paramref name="handler"/>.
        /// </summary>
        INodeBuilder OnConditionRefresh(ConditionRefreshHandler handler);

        /// <summary>
        /// Invoked once a monitored item has been successfully created for
        /// the resolved source node. Useful for attaching push-style
        /// sampling sources.
        /// </summary>
        INodeBuilder OnMonitoredItemCreated(MonitoredItemCreatedHandler handler);

        /// <summary>
        /// Wires the resolved node's <see cref="NodeState.OnReportEvent"/>
        /// hook to forward events to <paramref name="handler"/>.
        /// </summary>
        INodeBuilder OnEvent(EventNotificationHandler handler);
    }

    /// <summary>
    /// Strongly-typed view of <see cref="INodeBuilder"/> that narrows the
    /// <see cref="Node"/> property to <typeparamref name="TState"/>. All
    /// inherited <c>On*</c> methods continue to return the non-generic
    /// builder; use <see cref="INodeBuilder.As{TState}"/> chaining when
    /// you need to restore the typed view.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public interface INodeBuilder<out TState> : INodeBuilder
        where TState : NodeState
    {
        /// <summary>
        /// The resolved node, narrowed to <typeparamref name="TState"/>.
        /// </summary>
        new TState Node { get; }
    }
}
