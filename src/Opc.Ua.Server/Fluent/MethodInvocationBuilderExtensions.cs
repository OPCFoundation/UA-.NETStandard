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
    /// Wires asynchronous Method results with ordered input diagnostics.
    /// </summary>
    public static class MethodInvocationBuilderExtensions
    {
        /// <summary>
        /// Registers a Method handler that returns resolved operation and input results.
        /// </summary>
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
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "A Method handler requires a Method Node.");
            }
            RegisterHandler(method, handler);
            return builder;
        }

        /// <summary>
        /// Registers a result handler while preserving typed-Method chaining.
        /// </summary>
        /// <typeparam name="TState">The concrete Method state type.</typeparam>
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

        internal static void RegisterHandler(MethodState method, MethodCalledWithResultEventHandlerAsync handler)
        {
            if (method.OnCallMethodWithResultAsync is not null || method.OnCallMethod2Async is not null ||
                method.OnCallMethod2 is not null || method.OnCallMethod is not null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The Method already has an invocation handler.");
            }
            method.OnCallMethodWithResultAsync = handler;
        }
    }
}
