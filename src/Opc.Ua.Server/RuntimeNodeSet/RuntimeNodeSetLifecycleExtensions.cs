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

namespace Opc.Ua.Server.RuntimeNodeSet
{
    /// <summary>
    /// Live lifecycle extensions for runtime NodeSet-backed NodeManagers.
    /// </summary>
    public static class RuntimeNodeSetLifecycleExtensions
    {
        /// <summary>
        /// Loads and publishes a runtime NodeSet on a running server.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<NodeManagerRegistration> AddRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            RuntimeNodeSetOptions options,
            CancellationToken ct = default)
        {
            return lifecycle.AddRuntimeNodeSetAsync(options, callerContext: null, ct);
        }

        /// <summary>
        /// Loads and publishes a runtime NodeSet on a running server on behalf of the supplied
        /// operation.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="options">The options describing the NodeSet to load.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/> or <paramref name="options"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing.
        /// </exception>
        public static ValueTask<NodeManagerRegistration> AddRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            RuntimeNodeSetOptions options,
            IOperationContext? callerContext,
            CancellationToken ct)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return lifecycle.AddAsync(
                new RuntimeNodeSetNodeManagerFactory(options),
                callerContext,
                ct);
        }

        /// <summary>
        /// Reloads a live runtime NodeSet registration from replacement options.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/>, <paramref name="registration"/>, or
        /// <paramref name="replacement"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<NodeManagerRegistration> ReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            CancellationToken ct = default)
        {
            return lifecycle.ReloadRuntimeNodeSetAsync(
                registration,
                replacement,
                callerContext: null,
                ct);
        }

        /// <summary>
        /// Reloads a live runtime NodeSet registration from replacement options on behalf of the
        /// supplied operation.
        /// </summary>
        /// <param name="lifecycle">The lifecycle provider of the running server.</param>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The options describing the replacement NodeSet.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="lifecycle"/>, <paramref name="registration"/>, or
        /// <paramref name="replacement"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing.
        /// </exception>
        public static ValueTask<NodeManagerRegistration> ReloadRuntimeNodeSetAsync(
            this INodeManagerLifecycle lifecycle,
            NodeManagerRegistration registration,
            RuntimeNodeSetOptions replacement,
            IOperationContext? callerContext,
            CancellationToken ct)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return lifecycle.ReloadAsync(
                registration,
                new RuntimeNodeSetNodeManagerFactory(replacement),
                callerContext,
                ct);
        }
    }
}
