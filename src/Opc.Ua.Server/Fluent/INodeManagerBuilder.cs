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
        /// The namespace index a browse name given as a plain string is
        /// qualified with.
        /// </summary>
        /// <remarks>
        /// Exposed so that the child helpers qualify a string browse name the
        /// same way <see cref="AddObject(string, NodeId, NodeId)"/> does,
        /// rather than each inventing a namespace of its own.
        /// </remarks>
        ushort DefaultNamespaceIndex { get; }

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
        /// Imports a NodeSet2 document into the manager being configured.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every document imported during one <c>Configure</c> pass forms a
        /// single batch. Parent-child relationships are linked once when the
        /// pass completes, so a node may declare a parent which lives in
        /// another document of the same batch, or a node the manager already
        /// owns - a NodeSet overlay can therefore extend a generated model.
        /// The imported nodes are registered with the manager after
        /// <c>Configure</c> returns, but they resolve through
        /// <see cref="Node(NodeId)"/> and the other lookups immediately, so
        /// they can be wired in the same pass.
        /// </para>
        /// <para>
        /// When the manager (or the supplied
        /// <paramref name="factoryProvider"/>) implements
        /// <see cref="Nodes.INodeSetImportFactoryProvider"/>, matching nodes
        /// are imported into the concrete generated state types instead of the
        /// generic <c>BaseObjectState</c>/<c>BaseDataVariableState</c> shapes.
        /// The source-generated node manager implements that contract for its
        /// own model.
        /// </para>
        /// </remarks>
        /// <param name="nodeSet">The parsed NodeSet2 document.</param>
        /// <param name="factoryProvider">
        /// Supplies the typed import factories for this batch. When
        /// <c>null</c>, the node manager itself is used if it implements
        /// <see cref="Nodes.INodeSetImportFactoryProvider"/>.
        /// </param>
        /// <exception cref="System.ArgumentNullException">
        /// <paramref name="nodeSet"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// Thrown when the builder is sealed or the import batch is already
        /// completed.
        /// </exception>
        void Import(
            Export.UANodeSet nodeSet,
            Nodes.INodeSetImportFactoryProvider? factoryProvider = null);

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
        /// <typeparam name="TState">
        /// Expected concrete <see cref="NodeState"/> derivative.
        /// </typeparam>
        INodeBuilder<TState> Node<TState>(NodeId nodeId)
            where TState : NodeState;

        /// <summary>
        /// Resolves the unique node whose <c>TypeDefinitionId</c>
        /// matches <paramref name="typeDefinitionId"/>. Useful for singleton
        /// instances such as <c>HistoryServerCapabilities</c> where the
        /// well-known type id is far more stable than the deployment-specific
        /// browse path.
        /// </summary>
        /// <param name="typeDefinitionId">
        /// The type definition id of the instance to locate (typically a
        /// generated <c>ObjectTypeIds.*</c> or <c>VariableTypeIds.*</c>
        /// constant).
        /// </param>
        /// <exception cref="ServiceResultException">
        /// <list type="bullet">
        ///   <item><description><see cref="StatusCodes.BadNodeIdInvalid"/> — the id is null.</description></item>
        ///   <item><description><see cref="StatusCodes.BadNodeIdUnknown"/> —
        ///   no instance carries that type definition.</description></item>
        ///   <item><description><see cref="StatusCodes.BadBrowseNameDuplicated"/> —
        ///   more than one instance matches; supply a <see cref="QualifiedName"/>
        ///   disambiguator via the <see cref="NodeFromTypeId(NodeId, QualifiedName)"/>
        ///   overload.</description></item>
        /// </list>
        /// </exception>
        INodeBuilder NodeFromTypeId(NodeId typeDefinitionId);

        /// <summary>
        /// Like <see cref="NodeFromTypeId(NodeId)"/> but disambiguates among
        /// multiple instances by matching <paramref name="browseName"/>
        /// against <see cref="NodeState.BrowseName"/>.
        /// </summary>
        /// <param name="typeDefinitionId">See <see cref="NodeFromTypeId(NodeId)"/>.</param>
        /// <param name="browseName">
        /// Browse name of the instance to pick out. May be <c>null</c>, in
        /// which case the call behaves identically to the single-argument
        /// overload.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Same conditions as <see cref="NodeFromTypeId(NodeId)"/> plus
        /// <see cref="StatusCodes.BadNodeIdUnknown"/> when the disambiguator
        /// matches no candidate.
        /// </exception>
        INodeBuilder NodeFromTypeId(NodeId typeDefinitionId, QualifiedName browseName);

        /// <summary>
        /// Strongly-typed sibling of <see cref="NodeFromTypeId(NodeId)"/>.
        /// </summary>
        /// <typeparam name="TState">
        /// Expected concrete <see cref="NodeState"/> derivative the resolved
        /// instance must be assignable to.
        /// </typeparam>
        /// <exception cref="ServiceResultException">
        /// As <see cref="NodeFromTypeId(NodeId)"/>, plus
        /// <see cref="StatusCodes.BadTypeMismatch"/> if the instance is not
        /// assignable to <typeparamref name="TState"/>.
        /// </exception>
        INodeBuilder<TState> NodeFromTypeId<TState>(NodeId typeDefinitionId)
            where TState : NodeState;

        /// <summary>
        /// Strongly-typed sibling of
        /// <see cref="NodeFromTypeId(NodeId, QualifiedName)"/>.
        /// </summary>
        /// <typeparam name="TState">See <see cref="NodeFromTypeId{TState}(NodeId)"/>.</typeparam>
        INodeBuilder<TState> NodeFromTypeId<TState>(NodeId typeDefinitionId, QualifiedName browseName)
            where TState : NodeState;

        /// <summary>
        /// Resolves a variable node by browse path and returns a typed
        /// <see cref="IVariableBuilder{TValue}"/> view that exposes
        /// simple <c>Func</c> / <c>Action</c> shaped
        /// <c>OnRead</c>/<c>OnWrite</c> overloads.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="browsePath">See <see cref="Node(string)"/>.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown if the path does not resolve, or resolves to a node
        /// that is not a <see cref="BaseVariableState"/>.
        /// </exception>
        IVariableBuilder<TValue> Variable<TValue>(string browsePath);

        /// <summary>
        /// Resolves a variable node by absolute <see cref="NodeId"/>
        /// and returns a typed <see cref="IVariableBuilder{TValue}"/>
        /// view.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        IVariableBuilder<TValue> Variable<TValue>(NodeId nodeId);

        /// <summary>
        /// Resolves the unique variable instance whose
        /// <c>TypeDefinitionId</c> matches <paramref name="typeDefinitionId"/>
        /// and returns a typed <see cref="IVariableBuilder{TValue}"/>
        /// view. Same disambiguation semantics as
        /// <see cref="NodeFromTypeId(NodeId)"/>.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        IVariableBuilder<TValue> VariableFromTypeId<TValue>(NodeId typeDefinitionId);

        /// <summary>
        /// Like <see cref="VariableFromTypeId{TValue}(NodeId)"/> but
        /// disambiguates among multiple instances by browse name.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        IVariableBuilder<TValue> VariableFromTypeId<TValue>(NodeId typeDefinitionId, QualifiedName browseName);

        /// <summary>
        /// Resolves the unique variable instance whose
        /// <c>DataType</c> attribute equals <paramref name="dataTypeId"/>
        /// and returns a typed <see cref="IVariableBuilder{TValue}"/>
        /// view. Useful for singleton variables whose well-known DataType
        /// is more stable than the deployment-specific browse path.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="dataTypeId">
        /// The DataType id of the variable to locate (typically a
        /// generated <c>DataTypeIds.*</c> constant).
        /// </param>
        /// <exception cref="ServiceResultException">
        /// <list type="bullet">
        ///   <item><description><see cref="StatusCodes.BadNodeIdInvalid"/> — the id is null.</description></item>
        ///   <item><description><see cref="StatusCodes.BadNodeIdUnknown"/> —
        ///   no variable carries that DataType.</description></item>
        ///   <item><description><see cref="StatusCodes.BadBrowseNameDuplicated"/> —
        ///   more than one variable matches; supply a <see cref="QualifiedName"/>
        ///   disambiguator via the
        ///   <see cref="VariableFromDataTypeId{TValue}(NodeId, QualifiedName)"/>
        ///   overload.</description></item>
        ///   <item><description><see cref="StatusCodes.BadTypeMismatch"/> —
        ///   the resolved variable's <c>Value</c> is not assignable to
        ///   <typeparamref name="TValue"/>.</description></item>
        /// </list>
        /// </exception>
        IVariableBuilder<TValue> VariableFromDataTypeId<TValue>(NodeId dataTypeId);

        /// <summary>
        /// Like <see cref="VariableFromDataTypeId{TValue}(NodeId)"/> but
        /// disambiguates among multiple instances by matching
        /// <paramref name="browseName"/> against
        /// <see cref="NodeState.BrowseName"/>.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <param name="dataTypeId">See <see cref="VariableFromDataTypeId{TValue}(NodeId)"/>.</param>
        /// <param name="browseName">
        /// Browse name of the instance to pick out.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Same conditions as <see cref="VariableFromDataTypeId{TValue}(NodeId)"/>
        /// plus <see cref="StatusCodes.BadNodeIdUnknown"/> when the
        /// disambiguator matches no candidate.
        /// </exception>
        IVariableBuilder<TValue> VariableFromDataTypeId<TValue>(NodeId dataTypeId, QualifiedName browseName);

        /// <summary>
        /// Adds an already constructed state to the manager's graph and
        /// preserves any caller-assigned NodeIds.
        /// </summary>
        /// <remarks>
        /// The node — and every child in its subtree — receives a final
        /// NodeId from the manager's <see cref="INodeIdFactory"/> before this
        /// method returns, so callbacks wired onto the returned builder key
        /// off ids that survive registration unchanged.
        /// </remarks>
        /// <typeparam name="TState">The concrete state type.</typeparam>
        /// <param name="node">The node or node subtree to add.</param>
        /// <param name="parentId">
        /// The parent NodeId. The default places instance nodes below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        /// <returns>A typed fluent builder for the added node.</returns>
        /// <exception cref="ServiceResultException">
        /// <list type="bullet">
        ///   <item><description><see cref="StatusCodes.BadBrowseNameInvalid"/> —
        ///   the node has no browse name.</description></item>
        ///   <item><description><see cref="StatusCodes.BadNodeIdUnknown"/> —
        ///   <paramref name="parentId"/> names a node in one of this manager's
        ///   own namespaces that has not been added yet.</description></item>
        ///   <item><description><see cref="StatusCodes.BadNodeIdExists"/> —
        ///   the assigned NodeId collides with an existing node.</description></item>
        /// </list>
        /// </exception>
        INodeBuilder<TState> Add<TState>(TState node, NodeId parentId = default)
            where TState : NodeState;

        /// <summary>
        /// Creates and adds a typed state after resolving the parent identity,
        /// so a custom <see cref="INodeIdFactory"/> can derive the child's id
        /// from its final parent.
        /// </summary>
        /// <typeparam name="TState">The concrete state type.</typeparam>
        /// <param name="factory">
        /// Creates the state from the resolved parent, or from an
        /// identity-only proxy when the parent belongs to another node
        /// manager.
        /// </param>
        /// <param name="parentId">
        /// The parent NodeId. The default places instance nodes below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        /// <returns>A typed fluent builder for the added node.</returns>
        /// <exception cref="ServiceResultException">
        /// Same conditions as <see cref="Add{TState}(TState, NodeId)"/>.
        /// </exception>
        INodeBuilder<TState> Add<TState>(
            Func<NodeState?, TState> factory,
            NodeId parentId = default)
            where TState : NodeState;

        /// <summary>
        /// Adds a pre-built root without synthesizing an Objects-folder
        /// parent. References already present on the root are preserved.
        /// </summary>
        /// <typeparam name="TState">The concrete state type.</typeparam>
        /// <param name="node">The root node or subtree to add.</param>
        /// <returns>A typed fluent builder for the added root.</returns>
        INodeBuilder<TState> AddRoot<TState>(TState node)
            where TState : NodeState;

        /// <summary>
        /// Attempts to resolve a node that was added through this builder but
        /// is not registered with the manager yet, falling back to the
        /// manager's predefined nodes.
        /// </summary>
        /// <param name="nodeId">The NodeId to look up.</param>
        /// <param name="node">The resolved node, or <c>null</c>.</param>
        /// <returns><c>true</c> when a node was found.</returns>
        bool TryGetNode(NodeId nodeId, out NodeState? node);

        /// <summary>
        /// Creates a folder in the manager's default namespace.
        /// </summary>
        /// <param name="browseName">
        /// Browse name, qualified with the builder's default namespace index.
        /// </param>
        /// <param name="parentId">
        /// The parent NodeId. The default places the folder below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        INodeBuilder<FolderState> AddFolder(
            string browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates a folder using an explicitly namespaced browse name.
        /// </summary>
        /// <param name="browseName">
        /// Browse name carrying a nonzero namespace index.
        /// </param>
        /// <param name="parentId">See <see cref="AddFolder(string, NodeId)"/>.</param>
        INodeBuilder<FolderState> AddFolder(
            QualifiedName browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates an object in the manager's default namespace.
        /// </summary>
        /// <param name="browseName">
        /// Browse name, qualified with the builder's default namespace index.
        /// </param>
        /// <param name="parentId">
        /// The parent NodeId. The default places the object below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        /// <param name="typeDefinitionId">
        /// Type definition to apply; defaults to
        /// <see cref="ObjectTypeIds.BaseObjectType"/>.
        /// </param>
        INodeBuilder<BaseObjectState> AddObject(
            string browseName,
            NodeId parentId = default,
            NodeId typeDefinitionId = default);

        /// <summary>
        /// Creates an object using an explicitly namespaced browse name.
        /// </summary>
        /// <param name="browseName">
        /// Browse name carrying a nonzero namespace index.
        /// </param>
        /// <param name="parentId">See <see cref="AddObject(string, NodeId, NodeId)"/>.</param>
        /// <param name="typeDefinitionId">See <see cref="AddObject(string, NodeId, NodeId)"/>.</param>
        INodeBuilder<BaseObjectState> AddObject(
            QualifiedName browseName,
            NodeId parentId = default,
            NodeId typeDefinitionId = default);

        /// <summary>
        /// Creates a data variable in the manager's default namespace whose
        /// DataType and ValueRank are derived from
        /// <typeparamref name="TValue"/>.
        /// </summary>
        /// <typeparam name="TValue">The CLR type of the variable value.</typeparam>
        /// <param name="browseName">
        /// Browse name, qualified with the builder's default namespace index.
        /// </param>
        /// <param name="parentId">
        /// The parent NodeId. The default places the variable below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        IVariableBuilder<TValue> AddVariable<TValue>(
            string browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates a data variable using an explicitly namespaced browse name.
        /// </summary>
        /// <typeparam name="TValue">The CLR type of the variable value.</typeparam>
        /// <param name="browseName">
        /// Browse name carrying a nonzero namespace index.
        /// </param>
        /// <param name="parentId">See <see cref="AddVariable{TValue}(string, NodeId)"/>.</param>
        IVariableBuilder<TValue> AddVariable<TValue>(
            QualifiedName browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates an executable method in the manager's default namespace.
        /// </summary>
        /// <param name="browseName">
        /// Browse name, qualified with the builder's default namespace index.
        /// </param>
        /// <param name="parentId">
        /// The parent NodeId. The default places the method below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        INodeBuilder<MethodState> AddMethod(
            string browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates an executable method using an explicitly namespaced browse
        /// name.
        /// </summary>
        /// <param name="browseName">
        /// Browse name carrying a nonzero namespace index.
        /// </param>
        /// <param name="parentId">See <see cref="AddMethod(string, NodeId)"/>.</param>
        INodeBuilder<MethodState> AddMethod(
            QualifiedName browseName,
            NodeId parentId = default);
    }
}
