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
    /// Extension methods that create new instance nodes via a user-supplied
    /// factory delegate. Designed to integrate with the source generator's
    /// emitted <c>CreateXxx</c> factory methods for AOT-safe instance
    /// creation, while also accepting hand-rolled factories.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two flavours are exposed:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="CreateInstance{TState}(INodeBuilder, QualifiedName, Func{NodeState, TState})"/>
    ///     — typed factory. Use when the caller already has a
    ///     concrete state class and an instance factory. Pair with
    ///     generated factories like
    ///     <c>context.CreatePumpType(parent, browseName)</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="CreateInstance{TState}(INodeBuilder, QualifiedName, NodeId, Func{NodeState, TState})"/>
    ///     — same as above but also stamps an explicit
    ///     <c>TypeDefinitionId</c> on the new instance.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The new instance is attached to the parent via
    /// <see cref="NodeState.AddChild(BaseInstanceState)"/>; if the
    /// owning manager needs the instance indexed in
    /// <c>PredefinedNodes</c> for direct NodeId lookup, the caller is
    /// responsible for invoking
    /// <c>AsyncCustomNodeManager.AddPredefinedNodeAsync</c> with the
    /// returned <see cref="IInstanceBuilder{TState}.Node"/>.
    /// </para>
    /// <para>
    /// NodeIds for the new instance follow the
    /// <c>{parentIdentifier}_{browseName}</c> pattern used by the
    /// generated NodeIdFactory and the existing
    /// <see cref="ReferenceBuilderExtensions.AddObject(INodeBuilder, QualifiedName, NodeId)"/>
    /// helper.
    /// </para>
    /// </remarks>
    public static class InstanceCreationBuilderExtensions
    {
        /// <summary>
        /// Creates a new instance of <typeparamref name="TState"/> under
        /// the resolved parent.
        /// </summary>
        /// <typeparam name="TState">Concrete instance state class.</typeparam>
        /// <param name="parent">The owning parent builder.</param>
        /// <param name="browseName">Browse name of the new instance.</param>
        /// <param name="factory">
        /// Factory that constructs the state instance, typically a
        /// generated method such as
        /// <c>(p) => context.CreatePumpType(p, browseName)</c>.
        /// </param>
        /// <returns>A typed instance builder for further configuration.</returns>
        public static IInstanceBuilder<TState> CreateInstance<TState>(
            this INodeBuilder parent,
            QualifiedName browseName,
            Func<NodeState, TState> factory)
            where TState : BaseInstanceState
            => CreateInstance(parent, browseName, NodeId.Null, factory);

        /// <summary>
        /// Creates a new instance of <typeparamref name="TState"/> under
        /// the resolved parent and stamps an explicit
        /// <c>TypeDefinitionId</c>.
        /// </summary>
        public static IInstanceBuilder<TState> CreateInstance<TState>(
            this INodeBuilder parent,
            QualifiedName browseName,
            NodeId typeDefinitionId,
            Func<NodeState, TState> factory)
            where TState : BaseInstanceState
        {
            if (parent == null) { throw new ArgumentNullException(nameof(parent)); }
            if (browseName.IsNull)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            if (factory == null) { throw new ArgumentNullException(nameof(factory)); }

            string symbolicName = browseName.Name ?? string.Empty;
            TState instance = factory(parent.Node);
            if (instance == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Factory returned null for instance '{0}'.",
                    browseName);
            }

            instance.SymbolicName = symbolicName;
            instance.BrowseName = browseName;
            instance.DisplayName = new LocalizedText(symbolicName);

            string parentIdentifier = parent.Node.NodeId.IdentifierAsString;
            instance.NodeId = new NodeId(
                string.Concat(parentIdentifier, "_", symbolicName),
                parent.Node.NodeId.NamespaceIndex);

            if (!typeDefinitionId.IsNull)
            {
                instance.TypeDefinitionId = typeDefinitionId;
            }

            parent.Node.AddChild(instance);

            return new InstanceBuilder<TState>(parent, instance);
        }
    }

    /// <summary>
    /// Typed fluent builder for an instance node created via
    /// <see cref="InstanceCreationBuilderExtensions.CreateInstance{TState}(INodeBuilder, QualifiedName, Func{NodeState, TState})"/>.
    /// </summary>
    /// <typeparam name="TState">Concrete instance state class.</typeparam>
    public interface IInstanceBuilder<TState>
        where TState : BaseInstanceState
    {
        /// <summary>The newly created instance.</summary>
        TState Node { get; }

        /// <summary>The owning parent builder.</summary>
        INodeBuilder Parent { get; }

        /// <summary>
        /// Returns a <see cref="INodeBuilder{TState}"/> view of the new
        /// instance for further fluent wiring (e.g.
        /// <c>WithProperty</c>, <c>Organizes</c>, <c>CreateLimitAlarm</c>).
        /// </summary>
        INodeBuilder<TState> AsNode();

        /// <summary>
        /// Invokes <paramref name="configure"/> with the typed
        /// <see cref="INodeBuilder{TState}"/> view of the new instance.
        /// Use for inline configuration:
        /// <code>
        /// parent.CreateInstance&lt;PumpTypeState&gt;("Pump #2", factory)
        ///     .Configure(b => b.WithProperty("Manufacturer", "..."));
        /// </code>
        /// </summary>
        IInstanceBuilder<TState> Configure(Action<INodeBuilder<TState>> configure);

        /// <summary>
        /// Returns control to the parent builder for further chaining.
        /// </summary>
        INodeBuilder Done();
    }

    /// <summary>
    /// Internal implementation of <see cref="IInstanceBuilder{TState}"/>.
    /// Reuses the same ad-hoc builder used by
    /// <see cref="ReferenceBuilderExtensions.AddObject(INodeBuilder, QualifiedName, NodeId)"/>
    /// so chained fluent calls work uniformly.
    /// </summary>
    internal sealed class InstanceBuilder<TState> : IInstanceBuilder<TState>
        where TState : BaseInstanceState
    {
        public InstanceBuilder(INodeBuilder parent, TState node)
        {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Node = node ?? throw new ArgumentNullException(nameof(node));
        }

        public TState Node { get; }
        public INodeBuilder Parent { get; }

        public INodeBuilder<TState> AsNode()
        {
            return new AdHocInstanceNodeBuilder<TState>(Parent.Builder, Node);
        }

        public IInstanceBuilder<TState> Configure(Action<INodeBuilder<TState>> configure)
        {
            if (configure == null) { throw new ArgumentNullException(nameof(configure)); }
            configure(AsNode());
            return this;
        }

        public INodeBuilder Done() => Parent;
    }

    /// <summary>
    /// Minimal typed node builder for instances that don't live in the
    /// manager's <c>PredefinedNodes</c> dictionary. Mirrors the structure
    /// of <c>ReferenceBuilderExtensions.AdHocNodeBuilder</c> but exposes
    /// a strongly-typed <see cref="Node"/> property.
    /// </summary>
    internal sealed class AdHocInstanceNodeBuilder<TState> : INodeBuilder<TState>
        where TState : NodeState
    {
        internal AdHocInstanceNodeBuilder(INodeManagerBuilder owner, TState node)
        {
            Builder = owner ?? throw new ArgumentNullException(nameof(owner));
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
            return new AdHocInstanceNodeBuilder<TOther>(Builder, typed);
        }

        public INodeBuilder OnRead(NodeValueEventHandler handler) =>
            SetVariable(v => v.OnReadValue = handler);
        public INodeBuilder OnRead(NodeValueSimpleEventHandler handler) =>
            SetVariable(v => v.OnSimpleReadValue = handler);
        public INodeBuilder OnWrite(NodeValueEventHandler handler) =>
            SetVariable(v => v.OnWriteValue = handler);
        public INodeBuilder OnWrite(NodeValueSimpleEventHandler handler) =>
            SetVariable(v => v.OnSimpleWriteValue = handler);
        public INodeBuilder OnRead(NodeValueEventHandlerAsync handler) =>
            SetVariable(v => v.OnReadValueAsync = handler);
        public INodeBuilder OnRead(NodeValueSimpleEventHandlerAsync handler) =>
            SetVariable(v => v.OnSimpleReadValueAsync = handler);
        public INodeBuilder OnWrite(NodeValueWriteEventHandlerAsync handler) =>
            SetVariable(v => v.OnWriteValueAsync = handler);
        public INodeBuilder OnWrite(NodeValueSimpleWriteEventHandlerAsync handler) =>
            SetVariable(v => v.OnSimpleWriteValueAsync = handler);
        public INodeBuilder OnCall(GenericMethodCalledEventHandler2 handler) =>
            SetMethod(m => m.OnCallMethod2 = handler);
        public INodeBuilder OnCall(GenericMethodCalledEventHandler2Async handler) =>
            SetMethod(m => m.OnCallMethod2Async = handler);

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

        public INodeBuilder Child(QualifiedName browseName)
        {
            NodeState? c = Node.FindChild(Builder.Context, browseName);
            if (c == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Child '{0}' not found on '{1}'.",
                    browseName, Node.BrowseName);
            }
            return new AdHocInstanceNodeBuilder<NodeState>(Builder, c);
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
            return new AdHocInstanceNodeBuilder<TChild>(Builder, typed);
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

        private INodeBuilder SetVariable(Action<BaseVariableState> wire)
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

        private INodeBuilder SetMethod(Action<MethodState> wire)
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
