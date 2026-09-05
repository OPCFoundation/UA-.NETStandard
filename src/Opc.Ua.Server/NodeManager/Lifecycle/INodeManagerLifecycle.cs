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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Adds, reloads, and removes lifecycle-managed NodeManagers on a running server.
    /// </summary>
    public interface INodeManagerLifecycle
    {
        /// <summary>
        /// Gets a snapshot of the live application NodeManagers owned by this provider,
        /// including managers composed before server startup and managers added at runtime.
        /// Built-in diagnostics, configuration, and core NodeManagers are excluded.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether the owning server has started its ordered shutdown sequence.
        /// Once this is set, the lifecycle rejects add and reload requests and tears
        /// down every remaining registration itself, so callers that only want to
        /// release a registration during teardown can skip the round trip.
        /// </summary>
        bool IsShuttingDown { get; }

        /// <summary>
        /// Creates and publishes a NodeManager from an asynchronous factory.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation. A callback passes the context it was
        /// invoked with; see <see cref="SystemContextOperationExtensions.GetOperationContext"/>.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing. Such a
        /// call would wait for its own request to complete, so it is rejected instead.
        /// </exception>
        ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Creates and publishes a NodeManager from a synchronous factory.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation. A callback passes the context it was
        /// invoked with; see <see cref="SystemContextOperationExtensions.GetOperationContext"/>.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing. Such a
        /// call would wait for its own request to complete, so it is rejected instead.
        /// </exception>
        ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new asynchronous factory generation.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The factory that creates the next generation.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation. A callback passes the context it was
        /// invoked with; see <see cref="SystemContextOperationExtensions.GetOperationContext"/>.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing. Such a
        /// call would wait for its own request to complete, so it is rejected instead.
        /// </exception>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new synchronous factory generation.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The factory that creates the next generation.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation. A callback passes the context it was
        /// invoked with; see <see cref="SystemContextOperationExtensions.GetOperationContext"/>.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing. Such a
        /// call would wait for its own request to complete, so it is rejected instead.
        /// </exception>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new asynchronous factory generation while
        /// allowing the current generation to keep serving monitored items that were
        /// already created on it. New service requests are atomically routed to the
        /// replacement generation as soon as it is committed; the current generation is
        /// retained only for its existing monitored items and any request or continuation
        /// point that already captured it, and is disposed automatically once they drain.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new synchronous factory generation while
        /// allowing the current generation to keep serving monitored items that were
        /// already created on it. New service requests are atomically routed to the
        /// replacement generation as soon as it is committed; the current generation is
        /// retained only for its existing monitored items and any request or continuation
        /// point that already captured it, and is disposed automatically once they drain.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration and immediately invalidates monitored items
        /// owned by the previous generation with <see cref="StatusCodes.BadNodeIdUnknown"/>.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration and immediately invalidates monitored items
        /// owned by the previous generation with <see cref="StatusCodes.BadNodeIdUnknown"/>.
        /// </summary>
        /// <remarks>
        /// See docs/NodeManagers.md#reload-modes for the client-visible differences between
        /// reload modes.
        /// </remarks>
        ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a live registration from the server.
        /// </summary>
        /// <param name="registration">The registration to remove.</param>
        /// <param name="callerContext">The operation the caller is running under, or <c>null</c>
        /// when the caller is not serving an operation. A callback passes the context it was
        /// invoked with; see <see cref="SystemContextOperationExtensions.GetOperationContext"/>.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="callerContext"/> is a Client request that is still executing. Such a
        /// call would wait for its own request to complete, so it is rejected instead.
        /// </exception>
        ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            IOperationContext? callerContext,
            CancellationToken ct = default);
    }
}