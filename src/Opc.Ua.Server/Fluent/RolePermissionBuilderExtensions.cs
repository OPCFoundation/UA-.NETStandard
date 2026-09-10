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
    /// Fluent wiring for the <c>RolePermissions</c> and
    /// <c>UserRolePermissions</c> attribute read hooks of a node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A server that grants access on something the static model cannot
    /// express — the caller's own application record, the tenant a
    /// session belongs to, a lease that has not expired yet — has to
    /// compute the permission set per request. The stack calls
    /// <see cref="NodeState.OnReadRolePermissions"/> and
    /// <see cref="NodeState.OnReadUserRolePermissions"/> while it reads
    /// those two attributes, which is where such a decision belongs;
    /// these extensions attach the handlers through the same fluent
    /// surface as the value and method hooks instead of forcing the
    /// caller to reach for the raw <see cref="NodeState"/> fields.
    /// </para>
    /// <para>
    /// Handlers receive the currently configured permissions by
    /// reference and either replace them or add to them. Returning
    /// <see cref="ServiceResult.Good"/> keeps whatever the handler left
    /// in <c>value</c>; returning a bad code fails the attribute read.
    /// </para>
    /// <example>
    /// <code>
    /// builder.Node("Directory/UpdateApplication")
    ///        .OnReadRolePermissions(AddSelfAdminRole)
    ///        .OnReadUserRolePermissions(AddSelfAdminUserRole);
    /// </code>
    /// </example>
    /// </remarks>
    public static class RolePermissionBuilderExtensions
    {
        /// <summary>
        /// Wires <see cref="NodeState.OnReadRolePermissions"/> on the
        /// resolved node.
        /// </summary>
        /// <param name="builder">The node builder.</param>
        /// <param name="handler">
        /// Invoked while the <c>RolePermissions</c> attribute is read.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="handler"/> is
        /// <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// A different handler is already assigned to the node.
        /// </exception>
        public static INodeBuilder OnReadRolePermissions(
            this INodeBuilder builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
        {
            AssignRolePermissions(builder, handler);
            return builder;
        }

        /// <summary>
        /// Strongly-typed sibling of
        /// <see cref="OnReadRolePermissions(INodeBuilder, NodeAttributeEventHandler{ArrayOf{RolePermissionType}})"/>
        /// that preserves the typed builder for further chaining.
        /// </summary>
        /// <typeparam name="TState">The resolved node's state type.</typeparam>
        public static INodeBuilder<TState> OnReadRolePermissions<TState>(
            this INodeBuilder<TState> builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
            where TState : NodeState
        {
            AssignRolePermissions(builder, handler);
            return builder;
        }

        /// <summary>
        /// Wires <see cref="NodeState.OnReadUserRolePermissions"/> on the
        /// resolved node.
        /// </summary>
        /// <param name="builder">The node builder.</param>
        /// <param name="handler">
        /// Invoked while the <c>UserRolePermissions</c> attribute is read.
        /// </param>
        /// <returns>The same builder for chaining.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> or <paramref name="handler"/> is
        /// <c>null</c>.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// A different handler is already assigned to the node.
        /// </exception>
        public static INodeBuilder OnReadUserRolePermissions(
            this INodeBuilder builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
        {
            AssignUserRolePermissions(builder, handler);
            return builder;
        }

        /// <summary>
        /// Strongly-typed sibling of
        /// <see cref="OnReadUserRolePermissions(INodeBuilder, NodeAttributeEventHandler{ArrayOf{RolePermissionType}})"/>
        /// that preserves the typed builder for further chaining.
        /// </summary>
        /// <typeparam name="TState">The resolved node's state type.</typeparam>
        public static INodeBuilder<TState> OnReadUserRolePermissions<TState>(
            this INodeBuilder<TState> builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
            where TState : NodeState
        {
            AssignUserRolePermissions(builder, handler);
            return builder;
        }

        private static void AssignRolePermissions(
            INodeBuilder builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
        {
            NodeState node = Validate(builder, handler);
            ThrowIfOtherHandlerAssigned(
                node,
                node.OnReadRolePermissions,
                handler,
                "OnReadRolePermissions");
            node.OnReadRolePermissions = handler;
        }

        private static void AssignUserRolePermissions(
            INodeBuilder builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
        {
            NodeState node = Validate(builder, handler);
            ThrowIfOtherHandlerAssigned(
                node,
                node.OnReadUserRolePermissions,
                handler,
                "OnReadUserRolePermissions");
            node.OnReadUserRolePermissions = handler;
        }

        private static NodeState Validate(
            INodeBuilder builder,
            NodeAttributeEventHandler<ArrayOf<RolePermissionType>> handler)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            return builder.Node;
        }

        /// <summary>
        /// Mirrors the slot guard the per-node builders apply to their
        /// own <c>On*</c> hooks so a second, different handler cannot
        /// silently displace the first. Re-assigning the very same
        /// delegate is allowed: managers that wire a node both at
        /// startup and again when it is re-registered at runtime would
        /// otherwise fail on the second pass.
        /// </summary>
        private static void ThrowIfOtherHandlerAssigned(
            NodeState node,
            Delegate? existing,
            Delegate handler,
            string what)
        {
            if (existing != null && !existing.Equals(handler))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node '{0}' (id '{1}') already has a {2} handler assigned.",
                    node.BrowseName,
                    node.NodeId,
                    what);
            }
        }
    }
}
