/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Wires asynchronous Method results with ordered input diagnostics.
    /// </summary>
    public static class MethodInvocationBuilderExtensions
    {
        /// <summary>
        /// Registers a Method handler that returns resolved operation and input results.
        /// </summary>
        /// <param name="builder">The node builder.</param>
        /// <param name="handler">The method called with result event handler async.</param>
        /// <exception cref="ArgumentNullException">The builder or handler is null.</exception>
        /// <exception cref="ServiceResultException">The node is not a method or registration is invalid.</exception>
        public static INodeBuilder OnCallWithResult(
            this INodeBuilder builder, MethodCalledWithResultEventHandlerAsync handler)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (builder.Node is not MethodState method)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "A Method handler requires a Method Node.");
            }
            NodeManagerBuilder? manager = builder.Builder is NodeManagerBuilder concrete
                ? concrete
                : FluentNodeManagerBase.TryResolveAttachedBuilder(builder.Builder);
            manager?.ThrowIfSealed();
            method.RegisterHandler(handler);
            return builder;
        }

        /// <summary>
        /// Registers a result handler while preserving typed-Method chaining.
        /// </summary>
        /// <typeparam name="TState">The concrete Method state type.</typeparam>
        /// <param name="builder">The typed method node builder.</param>
        /// <param name="handler">The method called with result event handler async.</param>
        /// <exception cref="ArgumentNullException">The builder or handler is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The method already has a handler or the builder is sealed.
        /// </exception>
        public static INodeBuilder<TState> OnCallWithResult<TState>(
            this INodeBuilder<TState> builder, MethodCalledWithResultEventHandlerAsync handler)
            where TState : MethodState
        {
            OnCallWithResult((INodeBuilder)builder, handler);
            return builder;
        }

        /// <summary>
        /// Registers a complete-result handler for Methods materialized on demand.
        /// </summary>
        /// <param name="builder">The virtual node builder.</param>
        /// <param name="handler">The method called with result event handler async.</param>
        /// <exception cref="ArgumentNullException">The builder or handler is null.</exception>
        /// <exception cref="ArgumentException">The builder was not created by ResolveNodes.</exception>
        /// <exception cref="ServiceResultException">
        /// The family already has a handler or the builder is sealed.
        /// </exception>
        public static IVirtualNodeBuilder OnCallWithResult(
            this IVirtualNodeBuilder builder, MethodCalledWithResultEventHandlerAsync handler)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (builder is not VirtualNodeRegistration registration)
            {
                throw new ArgumentException(
                    "The virtual node builder was not created by ResolveNodes.", nameof(builder));
            }
            return registration.OnCallWithResult(handler);
        }

        /// <summary>
        /// Registers a complete-result handler without replacing an existing invocation handler.
        /// </summary>
        /// <param name="method">The method state.</param>
        /// <param name="handler">The method called with result event handler async.</param>
        /// <exception cref="ServiceResultException">The method already has an invocation handler.</exception>
        internal static void RegisterHandler(this MethodState method, MethodCalledWithResultEventHandlerAsync handler)
        {
            if (method.OnCallMethodWithResultAsync is not null ||
                method.OnCallMethod2Async is not null ||
                method.OnCallMethod2 is not null ||
                method.OnCallMethod is not null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The Method already has an invocation handler.");
            }
            method.OnCallMethodWithResultAsync = handler;
        }

        /// <summary>
        /// Rejects ordinary method registration when a complete-result handler is already installed.
        /// </summary>
        /// <param name="method">The method whose invocation slot is being configured.</param>
        /// <exception cref="ServiceResultException">A complete-result handler is already registered.</exception>
        internal static void ThrowIfCompleteHandlerRegistered(this MethodState method)
        {
            if (method.OnCallMethodWithResultAsync is not null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Node '{0}' (id '{1}') already has a complete invocation handler.",
                    method.BrowseName, method.NodeId);
            }
        }
    }
}
