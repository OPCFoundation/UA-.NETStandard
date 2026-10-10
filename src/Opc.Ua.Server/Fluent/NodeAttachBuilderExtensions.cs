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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Runtime inputs handed to an attach callback.
    /// </summary>
    public interface INodeAttachContext
    {
        /// <summary>
        /// Gets the node manager's system context.
        /// </summary>
        ISystemContext SystemContext { get; }

        /// <summary>
        /// Gets the node manager that owns the behavior.
        /// </summary>
        FluentNodeManagerBase NodeManager { get; }

        /// <summary>
        /// Gets the server time provider. Behaviors must schedule from this rather
        /// than from <see cref="DateTime.UtcNow"/> or <c>PeriodicTimer</c>, so that
        /// tests can drive them from a fake clock.
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the server telemetry context.
        /// </summary>
        ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets a token cancelled immediately before this behavior is disposed.
        /// </summary>
        /// <remarks>
        /// Background loops must capture this token, never the token passed to the
        /// attach callback: the callback's token is valid only for the duration of the
        /// call, whereas this one tracks the behavior's whole lifetime.
        /// </remarks>
        CancellationToken Lifetime { get; }

        /// <summary>
        /// Finds a node owned by the fully indexed node manager.
        /// </summary>
        NodeState? Find(NodeId nodeId);
    }

    /// <summary>
    /// Options controlling how a type-keyed behavior registration matches nodes.
    /// </summary>
    public sealed record NodeAttachOptions
    {
        /// <summary>
        /// Gets whether the registration also matches instances of subtypes of the
        /// declared type definition. Defaults to <c>true</c>.
        /// </summary>
        public bool IncludeSubtypes { get; init; } = true;

        /// <summary>
        /// Gets whether a registration that matches no node is accepted. Defaults to
        /// <c>false</c>, so a registration that matches nothing fails configuration
        /// rather than silently doing nothing.
        /// </summary>
        public bool AllowZeroMatches { get; init; }
    }

    /// <summary>
    /// Attaches resource-owning behaviors to nodes through the fluent builder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These extensions complement the per-node <c>On*</c> handlers on
    /// <see cref="INodeBuilder"/>. Use an <c>On*</c> handler for a stateless delegate on
    /// a known node; use an attach registration when the behavior spans many instances
    /// of a type, must compose with another behavior on the same node, or owns something
    /// that has to be released again.
    /// </para>
    /// <para>
    /// Every registration returns an <see cref="IAsyncDisposable"/> handle, or
    /// <c>null</c> to decline. The node manager owns those handles: it activates them
    /// child-first — a child node's behavior is live before its parent's — and base
    /// type before derived type on one node, and it unwinds them in exact reverse order
    /// when activation fails or the address space is deleted.
    /// </para>
    /// </remarks>
    public static class NodeAttachBuilderExtensions
    {
        /// <summary>
        /// Registers one behavior per instance of <paramref name="typeDefinitionId"/>,
        /// and by default per instance of its subtypes.
        /// </summary>
        /// <typeparam name="TState">
        /// The concrete state type every matching instance is expected to carry.
        /// </typeparam>
        /// <param name="builder">The fluent builder.</param>
        /// <param name="typeDefinitionId">
        /// The target type definition, typically a generated <c>ObjectTypeIds.*</c> or
        /// <c>VariableTypeIds.*</c> constant.
        /// </param>
        /// <param name="attach">
        /// Invoked once per matching instance. Return the handle that releases whatever
        /// the behavior acquired, or <c>null</c> to decline this instance.
        /// </param>
        /// <param name="options">Matching options; defaults are used when omitted.</param>
        /// <exception cref="ServiceResultException">
        /// Raised at configuration time when the manager does not opt into the fluent
        /// surface, and at activation time when the registration matches no node and
        /// <see cref="NodeAttachOptions.AllowZeroMatches"/> was not set.
        /// </exception>
        public static INodeManagerBuilder AttachToType<TState>(
            this INodeManagerBuilder builder,
            NodeId typeDefinitionId,
            Func<TState, INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>> attach,
            NodeAttachOptions? options = null)
            where TState : BaseInstanceState
        {
            if (typeDefinitionId.IsNull)
            {
                throw new ArgumentNullException(nameof(typeDefinitionId));
            }
            if (attach == null)
            {
                throw new ArgumentNullException(nameof(attach));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(builder, "AttachToType");
            concrete.RegisterNodeAttachment(
                NodeAttachRegistration.ForType(
                    typeDefinitionId,
                    static (node, context, ct, state) =>
                        ((Func<TState, INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>>)state)(
                            CastNode<TState>(node),
                            context,
                            ct),
                    attach,
                    options ?? new NodeAttachOptions()));
            return builder;
        }

        /// <summary>
        /// Registers one behavior on the resolved node.
        /// </summary>
        /// <typeparam name="TState">The resolved node's state type.</typeparam>
        /// <param name="node">The node builder.</param>
        /// <param name="attach">
        /// Invoked once for this node. Return the handle that releases whatever the
        /// behavior acquired, or <c>null</c> to decline.
        /// </param>
        public static INodeBuilder<TState> Attach<TState>(
            this INodeBuilder<TState> node,
            Func<TState, INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>> attach)
            where TState : NodeState
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (attach == null)
            {
                throw new ArgumentNullException(nameof(attach));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(node.Builder, "Attach");
            concrete.RegisterNodeAttachment(
                NodeAttachRegistration.ForNode(
                    node.Node,
                    static (target, context, ct, state) =>
                        ((Func<TState, INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>>)state)(
                            CastNode<TState>(target),
                            context,
                            ct),
                    attach));
            return node;
        }

        /// <summary>
        /// Registers one manager-scoped behavior that owns no node.
        /// </summary>
        /// <remarks>
        /// Manager-scoped behaviors activate after every node behavior and unwind before
        /// them, so they may safely drive nodes the node behaviors prepared.
        /// </remarks>
        /// <param name="builder">The fluent builder.</param>
        /// <param name="attach">
        /// Invoked once for the node manager. Return the handle that releases whatever
        /// the behavior acquired, or <c>null</c> to decline.
        /// </param>
        public static INodeManagerBuilder Attach(
            this INodeManagerBuilder builder,
            Func<INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>> attach)
        {
            if (attach == null)
            {
                throw new ArgumentNullException(nameof(attach));
            }

            NodeManagerBuilder concrete =
                FluentNodeManagerBase.ResolveAttachedBuilder(builder, "Attach");
            concrete.RegisterNodeAttachment(
                NodeAttachRegistration.ForManager(
                    static (_, context, ct, state) =>
                        ((Func<INodeAttachContext, CancellationToken, ValueTask<IAsyncDisposable?>>)state)(
                            context,
                            ct),
                    attach));
            return builder;
        }

        private static TState CastNode<TState>(NodeState node)
            where TState : NodeState
        {
            if (node is TState typed)
            {
                return typed;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadTypeMismatch,
                "The behavior registered for '{0}' expects state type '{1}' but the " +
                "node was materialized as '{2}'.",
                node.NodeId,
                typeof(TState).Name,
                node.GetType().Name);
        }
    }
}
