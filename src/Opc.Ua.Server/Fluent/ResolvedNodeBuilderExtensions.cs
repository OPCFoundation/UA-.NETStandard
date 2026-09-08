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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Fluent entry point for a <see cref="NodeState"/> the caller
    /// already holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>Node(...)</c> overloads on <see cref="INodeManagerBuilder"/>
    /// look their target up in the manager's predefined-node graph, which
    /// only works for nodes that were part of the address space when
    /// <c>Configure</c> ran. Managers that materialize nodes later — a
    /// service instance created on demand, a subtree replaced during
    /// <c>AddBehaviourToPredefinedNodeAsync</c>, a device discovered at
    /// runtime — have the node in hand but nothing to resolve it by, and
    /// would otherwise have to drop out of the fluent surface and poke
    /// the <see cref="NodeState"/> hooks directly.
    /// </para>
    /// <para>
    /// This overload closes that gap: it wraps the node in the same
    /// builder the resolving overloads return, so the identical wiring
    /// recipe can be applied to statically declared and runtime-created
    /// nodes alike.
    /// </para>
    /// <example>
    /// <code>
    /// NodeManagerBuilder builder = CreateFluentBuilder(namespaceIndex);
    /// builder.Node(service)
    ///        .OnReadRolePermissions(AddSelfAdminRole);
    /// await builder.SealAsync(cancellationToken);
    /// </code>
    /// </example>
    /// </remarks>
    public static class ResolvedNodeBuilderExtensions
    {
        /// <summary>
        /// Returns a typed fluent builder for an already-resolved
        /// <paramref name="node"/> without consulting the manager's
        /// predefined-node graph.
        /// </summary>
        /// <typeparam name="TState">
        /// The node's concrete <see cref="NodeState"/> type.
        /// </typeparam>
        /// <param name="builder">The owning node-manager builder.</param>
        /// <param name="node">The node to wire.</param>
        /// <returns>A typed node builder for <paramref name="node"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="node"/> is
        /// <c>null</c>.
        /// </exception>
        public static INodeBuilder<TState> Node<TState>(
            this INodeManagerBuilder builder,
            TState node)
            where TState : NodeState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            // The concrete builder gives the node full access to the
            // manager-level dispatcher (history, monitored items,
            // lifecycle); the ad-hoc fallback covers facades that are
            // not backed by one, where those registrations have nowhere
            // to go anyway.
            return builder is NodeManagerBuilder concrete
                ? new NodeBuilder<TState>(concrete, node)
                : new AdHocInstanceNodeBuilder<TState>(builder, node);
        }
    }
}
