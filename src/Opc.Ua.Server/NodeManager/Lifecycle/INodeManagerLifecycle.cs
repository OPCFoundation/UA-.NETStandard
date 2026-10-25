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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Adds, reloads, and removes lifecycle-managed NodeManagers on a running server.
    /// </summary>
    /// <remarks>
    /// Add and reload await <see cref="INodeManagerReadinessNotification"/> after committing
    /// the generation. Its registration remains visible but cannot be removed or reloaded
    /// until that startup operation completes. A readiness failure or cancellation is a
    /// post-commit failure; the live registration remains available for recovery or removal.
    /// Post-commit failures are reported by <see cref="NodeManagerAddCommittedException"/>
    /// or <see cref="NodeManagerReloadCommittedException"/> for add and reload, and by
    /// <see cref="NodeManagerRemovalCommittedException"/> for removal. Their registrations
    /// and results describe the authoritative published state; callers must not retry the
    /// same operation as though it had been rolled back.
    /// </remarks>
    public interface INodeManagerLifecycle
    {
        /// <summary>
        /// Gets the revision of the currently published NodeManager routing view.
        /// </summary>
        long Revision { get; }

        /// <summary>
        /// Gets the stable slots for the live application NodeManagers owned by this provider.
        /// Built-in diagnostics, configuration, and core NodeManagers are excluded.
        /// </summary>
        ArrayOf<NodeManagerSlot> Slots { get; }

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
        /// Gets whether this lifecycle supports preparing an unpublished transaction.
        /// </summary>
        bool SupportsPreparedPublication { get; }

        /// <summary>
        /// Gets whether publication admission can be captured across multiple prepared units.
        /// </summary>
        bool SupportsPublicationIsolation { get; }

        /// <summary>
        /// Captures the current routing, type, and factory revisions without reserving publication.
        /// </summary>
        /// <returns>The capture holding the observed revisions.</returns>
        INodeManagerPublicationCapture CapturePublication();

        /// <summary>
        /// Prepares additions, replacements, and retirements without publishing them.
        /// The returned owner must be committed or asynchronously disposed.
        /// </summary>
        /// <param name="changes">The additions, replacements, and retirements to prepare.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares additions, replacements, and retirements without publishing them.
        /// </summary>
        /// <param name="changes">The additions, replacements, and retirements to prepare.</param>
        /// <param name="callerContext">The legacy explicit operation context.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates and publishes a NodeManager from an asynchronous factory.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            CancellationToken ct = default);

        /// <summary>
        /// Creates and publishes a NodeManager from a synchronous factory without requiring a request context.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            CancellationToken ct = default);

        /// <summary>
        /// Creates and publishes a NodeManager from an asynchronous factory.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="callerContext">The legacy explicit operation context. New callers should use the overload
        /// without this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Creates and publishes a NodeManager from a synchronous factory.
        /// </summary>
        /// <param name="factory">The factory that creates the NodeManager.</param>
        /// <param name="callerContext">The legacy explicit operation context. New callers should use the overload
        /// without this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new asynchronous factory generation without requiring a request
        /// context.
        /// </summary>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The factory that creates the next generation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Replaces a live registration with a new synchronous factory generation without requiring a request
        /// context.
        /// </summary>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="replacement">The factory that creates the next generation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
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
        /// <param name="callerContext">The legacy explicit operation context. New callers should use the overload
        /// without this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
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
        /// <param name="callerContext">The legacy explicit operation context. New callers should use the overload
        /// without this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default);

        /// <summary>
        /// Legacy alias for <see cref="ReloadAsync(NodeManagerRegistration, IAsyncNodeManagerFactory,
        /// CancellationToken)"/> using the <c>Migrate</c> transition.
        /// </summary>
        /// <remarks>
        /// Compatible monitored items move to the replacement; incompatible items are invalidated.
        /// This method does not retain the old generation for future service requests or continuation points.
        /// A request already pinned to the old routing view may finish on that view as part of ordinary
        /// request lifetime management.
        /// </remarks>
        /// <param name="registration">The live registration to replace.</param>
        /// <param name="replacement">The factory that creates the replacement generation.</param>
        /// <param name="ct">The token to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Legacy alias for <see cref="ReloadAsync(NodeManagerRegistration, INodeManagerFactory,
        /// CancellationToken)"/> using the <c>Migrate</c> transition.
        /// </summary>
        /// <remarks>
        /// Compatible monitored items move to the replacement; incompatible items are invalidated.
        /// This method does not retain the old generation for future service requests or continuation points.
        /// A request already pinned to the old routing view may finish on that view as part of ordinary
        /// request lifetime management.
        /// </remarks>
        /// <param name="registration">The live registration to replace.</param>
        /// <param name="replacement">The factory that creates the replacement generation.</param>
        /// <param name="ct">The token to cancel the operation.</param>
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
        /// <param name="registration">The live registration to replace.</param>
        /// <param name="replacement">The factory that creates the replacement generation.</param>
        /// <param name="ct">The token to cancel the operation.</param>
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
        /// <param name="registration">The live registration to replace.</param>
        /// <param name="replacement">The factory that creates the replacement generation.</param>
        /// <param name="ct">The token to cancel the operation.</param>
        ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a live registration from the server without requiring a request context.
        /// </summary>
        /// <remarks>
        /// Waits through publication and reconciliation, but not through an in-flight generation drain.
        /// Use the prepared batch result's retirement task to await background cleanup explicitly.
        /// </remarks>
        /// <param name="registration">The registration to remove.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="NodeManagerRemovalCommittedException">
        /// The registration was removed, but post-commit reconciliation or retirement cleanup failed.
        /// </exception>
        ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            CancellationToken ct = default);

        /// <summary>
        /// Removes a live registration from the server.
        /// </summary>
        /// <remarks>
        /// Removal is committed before retirement cleanup. This operation never waits for an in-flight
        /// generation drain, regardless of whether the caller is a request callback.
        /// </remarks>
        /// <param name="registration">The registration to remove.</param>
        /// <param name="callerContext">The legacy explicit operation context. New callers should use the overload
        /// without this parameter; request lifetime is discovered by the lifecycle implementation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <exception cref="NodeManagerRemovalCommittedException">
        /// The registration was removed, but post-commit reconciliation or retirement cleanup failed.
        /// </exception>
        ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            IOperationContext? callerContext,
            CancellationToken ct = default);
    }
}
