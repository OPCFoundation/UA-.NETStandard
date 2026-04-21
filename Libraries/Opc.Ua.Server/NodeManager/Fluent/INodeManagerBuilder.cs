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
    /// Fluent surface used to wire callbacks into a node manager's
    /// predefined-node graph. An instance of this builder is created once
    /// per node manager activation by the source-generated
    /// <c>NodeManagerBase.Configure</c> hook (or supplied directly to
    /// hand-written managers via
    /// <c>CustomNodeManager2.Configure(Action&lt;INodeManagerBuilder&gt;)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// All node lookups are resolved eagerly against the address space at
    /// the time the user delegate executes; lookup failures throw
    /// <see cref="ServiceResultException"/> so wiring errors surface at
    /// startup rather than at first request. This keeps the runtime path
    /// reflection-free and AOT/trim safe.
    /// </para>
    /// <para>
    /// Browse paths are forward-slash separated <see cref="QualifiedName"/>
    /// chains rooted at the manager's predefined nodes. Each segment may
    /// optionally carry a <c>ns=N;</c> namespace prefix to cross into a
    /// namespace other than the manager's own; segments without a prefix
    /// inherit the manager's first registered namespace index.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Node("Boilers/Boiler #1/PipeX001/ValveX001")
    ///        .OnSimpleWrite(MyValveWriteHandler);
    ///
    /// builder.Node("ns=2;Methods/Increment")
    ///        .As&lt;MethodState&gt;()
    ///        .OnCallAsync(MyIncrementAsync);
    /// </code>
    /// </example>
    /// </remarks>
    public interface INodeManagerBuilder
    {
        /// <summary>
        /// The system context active while the user's
        /// <c>Configure</c> delegate is executing. Useful for passing into
        /// any address-space helpers (e.g. <c>NodeState.FindChild</c>)
        /// that the user might call directly.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// The node manager whose graph is being wired. Exposed to support
        /// rare cases that need direct access (e.g. registering a
        /// dynamically generated node) without breaking out of the fluent
        /// chain. Use <see cref="IAsyncNodeManager.SyncNodeManager"/> to
        /// obtain the synchronous <see cref="INodeManager"/> facade when
        /// needed (e.g. when interacting with legacy callers).
        /// </summary>
        IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// Manager-level dispatch surface populated by the <c>On*</c>
        /// methods on the per-node builders. The owning node manager
        /// invokes this from its <c>HistoryRead</c>, <c>HistoryUpdate</c>,
        /// <c>ConditionRefresh</c>, and <c>CreateMonitoredItems</c>
        /// overrides; for nodes the dispatcher does not own a handler for,
        /// callers fall back to the base behavior.
        /// </summary>
        IFluentDispatcher Dispatcher { get; }

        /// <summary>
        /// Resolves a node by browse path against the manager's predefined
        /// nodes. Throws if the path does not resolve to exactly one node.
        /// </summary>
        /// <param name="browsePath">
        /// Forward-slash separated <see cref="QualifiedName"/> chain. See
        /// the type-level remarks for the namespace-prefix syntax.
        /// </param>
        /// <returns>A non-generic node builder for the resolved node.</returns>
        /// <exception cref="ServiceResultException">
        /// Thrown when the path is empty, ambiguous, or does not resolve.
        /// </exception>
        INodeBuilder Node(string browsePath);

        /// <summary>
        /// Strongly-typed sibling of <see cref="Node(string)"/> that
        /// validates the resolved node is assignable to
        /// <typeparamref name="TState"/>.
        /// </summary>
        /// <typeparam name="TState">
        /// Expected concrete <see cref="NodeState"/> derivative.
        /// </typeparam>
        /// <param name="browsePath">See <see cref="Node(string)"/>.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown if the node does not resolve or is not assignable to
        /// <typeparamref name="TState"/>.
        /// </exception>
        INodeBuilder<TState> Node<TState>(string browsePath)
            where TState : NodeState;

        /// <summary>
        /// Resolves a node by absolute <see cref="NodeId"/>. Useful when
        /// the caller already holds an id (for example, one returned from
        /// the source-generated <c>{Namespace}.NodeIds</c> constants).
        /// </summary>
        INodeBuilder Node(NodeId nodeId);

        /// <summary>
        /// Strongly-typed sibling of <see cref="Node(NodeId)"/>.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        INodeBuilder<TState> Node<TState>(NodeId nodeId)
            where TState : NodeState;
    }
}
