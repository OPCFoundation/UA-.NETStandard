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
    /// Extension methods that add references and child objects to the
    /// node currently focused by the fluent builder. These cover the
    /// common "FunctionalGroup" wiring pattern from OPC UA DI
    /// (OPC 10000-100):
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><see cref="Organizes(INodeBuilder, NodeId)"/>
    ///     — adds an <c>Organizes</c> reference (DI uses this to gather
    ///     unrelated variables under a FunctionalGroup).</description></item>
    ///   <item><description><see cref="HasComponent(INodeBuilder, NodeId)"/>
    ///     / <see cref="HasProperty(INodeBuilder, NodeId)"/>
    ///     — common hierarchical references.</description></item>
    ///   <item><description><see cref="AddReference(INodeBuilder, NodeId, bool, NodeId)"/>
    ///     — generic escape hatch for any
    ///     reference type.</description></item>
    ///   <item><description><see cref="AddObject(INodeBuilder, QualifiedName, NodeId)"/>
    ///     — creates a new <see cref="BaseObjectState"/> child (typically
    ///     of <c>Opc.Ua.Di.ObjectTypes.FunctionalGroupType</c>) for grouping
    ///     other nodes.</description></item>
    /// </list>
    /// <para>
    /// Newly created child objects are attached to the parent via
    /// <see cref="NodeState.AddChild"/>; their NodeIds are generated from
    /// the parent's NodeId by appending the child browse name (matching
    /// the pattern used by the generated state classes and the
    /// PumpDeviceIntegrationServer NodeIdFactory). Direct NodeId lookup on a newly
    /// created child requires the owning node manager to index the new
    /// node via <c>AddPredefinedNodeAsync</c>; until then the child is
    /// reachable through navigation from the parent.
    /// </para>
    /// </remarks>
    public static class ReferenceBuilderExtensions
    {
        /// <summary>
        /// Adds an <see cref="ReferenceTypeIds.Organizes"/> reference
        /// from the current node to <paramref name="targetId"/>.
        /// </summary>
        /// <param name="builder">The owning node builder.</param>
        /// <param name="targetId">NodeId of the organized target.</param>
        /// <returns>The same builder, for chaining.</returns>
        public static INodeBuilder Organizes(
            this INodeBuilder builder,
            NodeId targetId)
            => builder.AddReference(ReferenceTypeIds.Organizes, isInverse: false, targetId);

        /// <summary>
        /// Convenience overload that takes a <see cref="NodeState"/> for
        /// the target.
        /// </summary>
        public static INodeBuilder Organizes(
            this INodeBuilder builder,
            NodeState target)
        {
            if (target == null) { throw new ArgumentNullException(nameof(target)); }
            return builder.Organizes(target.NodeId);
        }

        /// <summary>
        /// Adds a <see cref="ReferenceTypeIds.HasComponent"/> reference
        /// from the current node to <paramref name="targetId"/>.
        /// </summary>
        public static INodeBuilder HasComponent(
            this INodeBuilder builder,
            NodeId targetId)
            => builder.AddReference(ReferenceTypeIds.HasComponent, isInverse: false, targetId);

        /// <summary>
        /// Adds a <see cref="ReferenceTypeIds.HasProperty"/> reference
        /// from the current node to <paramref name="targetId"/>.
        /// </summary>
        public static INodeBuilder HasProperty(
            this INodeBuilder builder,
            NodeId targetId)
            => builder.AddReference(ReferenceTypeIds.HasProperty, isInverse: false, targetId);

        /// <summary>
        /// Adds an arbitrary reference from the current node to
        /// <paramref name="targetId"/>. Equivalent to
        /// <see cref="NodeState.AddReference(NodeId, bool, ExpandedNodeId)"/>.
        /// </summary>
        /// <param name="builder">The owning node builder.</param>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">
        /// <see langword="true"/> for an inverse reference;
        /// <see langword="false"/> for a forward reference.
        /// </param>
        /// <param name="targetId">NodeId of the target node.</param>
        /// <returns>The same builder, for chaining.</returns>
        public static INodeBuilder AddReference(
            this INodeBuilder builder,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            if (builder == null) { throw new ArgumentNullException(nameof(builder)); }
            if (referenceTypeId.IsNull)
            {
                throw new ArgumentNullException(nameof(referenceTypeId));
            }
            if (targetId.IsNull)
            {
                throw new ArgumentNullException(nameof(targetId));
            }

            builder.Node.AddReference(referenceTypeId, isInverse, targetId);
            return builder;
        }

        /// <summary>
        /// Creates and attaches a new <see cref="BaseObjectState"/> child
        /// to the resolved parent. Useful for synthesising
        /// FunctionalGroups (DI) or other grouping objects in code rather
        /// than declaring them in the NodeSet XML.
        /// </summary>
        /// <param name="parent">The owning parent builder.</param>
        /// <param name="browseName">
        /// Browse name of the new child (qualified with the desired
        /// namespace index).
        /// </param>
        /// <param name="typeDefinitionId">
        /// Optional <c>TypeDefinitionId</c> for the new object. When
        /// <see cref="NodeId.Null"/> (the default), the
        /// <see cref="ObjectTypeIds.BaseObjectType"/> is used. Pass
        /// <c>Opc.Ua.Di.ObjectTypes.FunctionalGroupType</c> (resolved
        /// through the NamespaceTable) when the group should appear as
        /// a DI FunctionalGroup.
        /// </param>
        /// <returns>
        /// A typed <see cref="INodeBuilder{BaseObjectState}"/> for the
        /// newly created child, ready for further wiring (e.g.
        /// <see cref="Organizes(INodeBuilder, NodeId)"/>).
        /// </returns>
        public static INodeBuilder<BaseObjectState> AddObject(
            this INodeBuilder parent,
            QualifiedName browseName,
            NodeId typeDefinitionId = default)
        {
            if (parent == null) { throw new ArgumentNullException(nameof(parent)); }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }

            NodeId typeDef = typeDefinitionId.IsNull ? ObjectTypeIds.BaseObjectType : typeDefinitionId;

            string symbolicName = browseName.Name ?? string.Empty;
            var child = new BaseObjectState(parent.Node)
            {
                BrowseName = browseName,
                DisplayName = new LocalizedText(symbolicName),
                SymbolicName = symbolicName,
                TypeDefinitionId = typeDef
            };

            // Generate a NodeId that mirrors the parent's identifier scope.
            // Pattern matches PumpDeviceIntegrationServer's NodeIdFactory:
            // "{parentIdentifier}_{childBrowseName}" in the parent's namespace.
            string parentIdentifier = parent.Node.NodeId.IdentifierAsString;
            child.NodeId = new NodeId(
                string.Concat(parentIdentifier, "_", symbolicName),
                parent.Node.NodeId.NamespaceIndex);

            parent.Node.AddChild(child);

            // Return a typed builder pointing at the new child. Reuse the
            // existing NodeManagerBuilder.AsTyped path by going through a
            // small adapter that doesn't require re-registration.
            return new AdHocNodeBuilder<BaseObjectState>(parent.Builder, child);
        }

        /// <summary>
        /// Minimal <see cref="INodeBuilder{TState}"/> wrapper for nodes
        /// that don't live in the manager's <c>PredefinedNodes</c>
        /// dictionary. Provides direct access to the underlying node and
        /// the parent builder, plus pass-through of all <c>On*</c> /
        /// child wiring through the same mechanisms used by the
        /// production <c>NodeBuilder</c>.
        /// </summary>
        /// <typeparam name="TState">Concrete node state class.</typeparam>
        private sealed class AdHocNodeBuilder<TState> :
            INodeBuilder<TState>
            where TState : NodeState
        {
            internal AdHocNodeBuilder(INodeManagerBuilder ownerBuilder, TState node)
            {
                Builder = ownerBuilder ?? throw new ArgumentNullException(nameof(ownerBuilder));
                Node = node ?? throw new ArgumentNullException(nameof(node));
            }

            public TState Node { get; }
            NodeState INodeBuilder.Node => Node;
            public INodeManagerBuilder Builder { get; }

            public INodeBuilder<TOther> As<TOther>() where TOther : NodeState
            {
                if (Node is not TOther typed)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Node '{0}' is not assignable to '{1}'.",
                        Node.BrowseName,
                        typeof(TOther).Name);
                }
                return new AdHocNodeBuilder<TOther>(Builder, typed);
            }

            public INodeBuilder OnRead(NodeValueEventHandler handler)
                => SetVariable(v => v.OnReadValue = handler);
            public INodeBuilder OnRead(NodeValueSimpleEventHandler handler)
                => SetVariable(v => v.OnSimpleReadValue = handler);
            public INodeBuilder OnWrite(NodeValueEventHandler handler)
                => SetVariable(v => v.OnWriteValue = handler);
            public INodeBuilder OnWrite(NodeValueSimpleEventHandler handler)
                => SetVariable(v => v.OnSimpleWriteValue = handler);
            public INodeBuilder OnRead(NodeValueEventHandlerAsync handler)
                => SetVariable(v => v.OnReadValueAsync = handler);
            public INodeBuilder OnRead(NodeValueSimpleEventHandlerAsync handler)
                => SetVariable(v => v.OnSimpleReadValueAsync = handler);
            public INodeBuilder OnWrite(NodeValueWriteEventHandlerAsync handler)
                => SetVariable(v => v.OnWriteValueAsync = handler);
            public INodeBuilder OnWrite(NodeValueSimpleWriteEventHandlerAsync handler)
                => SetVariable(v => v.OnSimpleWriteValueAsync = handler);
            public INodeBuilder OnCall(GenericMethodCalledEventHandler2 handler)
                => SetMethod(m => m.OnCallMethod2 = handler);
            public INodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler)
                => SetMethod(m => m.OnCallMethod2Async = handler);

            public INodeBuilder OnNodeAdded(NodeLifecycleHandler handler)
            {
                handler(Builder.Context, Node);
                return this;
            }
            public INodeBuilder OnNodeRemoved(NodeLifecycleHandler handler) => this;
            public INodeBuilder OnHistoryRead(HistoryReadHandler handler) => this;
            public INodeBuilder OnHistoryUpdate(HistoryUpdateHandler handler) => this;
            public INodeBuilder OnConditionRefresh(ConditionRefreshHandler handler) => this;
            public INodeBuilder OnMonitoredItemCreated(MonitoredItemCreatedHandler handler) => this;
            public INodeBuilder OnEvent(EventNotificationHandler handler)
            {
                Node.OnReportEvent = (ctx, n, ev) => handler(ctx, n, ev);
                return this;
            }

            public INodeBuilder AllowMultipleEventConsumers(bool enable = true)
            {
                if (Builder is NodeManagerBuilder nmb)
                {
                    nmb.RegisterMultiConsumerNode(Node, enable);
                }
                return this;
            }

            public INodeBuilder Child(QualifiedName browseName)
            {
                NodeState? c = Node.FindChild(Builder.Context, browseName);
                if (c == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Child '{0}' not found on '{1}'.",
                        browseName,
                        Node.BrowseName);
                }
                return new AdHocNodeBuilder<NodeState>(Builder, c);
            }

            public INodeBuilder<TChild> Child<TChild>(QualifiedName browseName)
                where TChild : NodeState
            {
                NodeState? c = Node.FindChild(Builder.Context, browseName);
                if (c is not TChild typed)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Child '{0}' on '{1}' is not '{2}'.",
                        browseName, Node.BrowseName, typeof(TChild).Name);
                }
                return new AdHocNodeBuilder<TChild>(Builder, typed);
            }

            public IVariableBuilder<TValue> Variable<TValue>(QualifiedName browseName)
            {
                NodeState? c = Node.FindChild(Builder.Context, browseName);
                if (c is not BaseVariableState variable)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "Child '{0}' on '{1}' is not a variable.",
                        browseName, Node.BrowseName);
                }
                return new VariableBuilder<TValue>(Builder, variable);
            }

            private AdHocNodeBuilder<TState> SetVariable(Action<BaseVariableState> wire)
            {
                if (Node is not BaseVariableState v)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Cannot wire variable callback on '{0}' (not a BaseVariableState).",
                        Node.BrowseName);
                }
                wire(v);
                return this;
            }

            private AdHocNodeBuilder<TState> SetMethod(Action<MethodState> wire)
            {
                if (Node is not MethodState m)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadInvalidArgument,
                        "Cannot wire method callback on '{0}' (not a MethodState).",
                        Node.BrowseName);
                }
                wire(m);
                return this;
            }
        }
    }
}
