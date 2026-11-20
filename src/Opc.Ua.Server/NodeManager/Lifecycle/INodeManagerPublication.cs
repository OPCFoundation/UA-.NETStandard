/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Compatibility marker for lifecycles that support captured-view publication transactions.
    /// New callers should use the transaction members on <see cref="INodeManagerLifecycle"/>.
    /// </summary>
    public interface INodeManagerPublicationLifecycle : INodeManagerBatchLifecycle
    {
    }

    /// <summary>
    /// Owner-bound revision evidence captured before application preparation.
    /// </summary>
    public interface INodeManagerPublicationCapture
    {
        /// <summary>
        /// Begins one independently owned invocation without requiring an explicit request context.
        /// Other invocations and ordinary lifecycle operations may proceed concurrently.
        /// A stale capture returns an invocation whose <see cref="INodeManagerPublication.IsCurrent"/>
        /// is false and cannot prepare units.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains operation ownership and its prepared units until disposed.</returns>
        ValueTask<INodeManagerPublication> BeginAsync(
            CancellationToken cancellationToken = default);

    }

    /// <summary>
    /// Retains optimistic serving-revision evidence and operation ownership across sequential publication units.
    /// Conflicts with other publications are rejected before a durable decision.
    /// Factories are never retried implicitly.
    /// </summary>
    public interface INodeManagerPublication : IAsyncDisposable
    {
        /// <summary>
        /// Gets whether the invocation's expected routing, type and factory revisions are still current.
        /// Successful units advance this evidence only to their own published revision.
        /// </summary>
        bool IsCurrent { get; }

        /// <summary>
        /// Prepares a unit on the existing lifecycle owner without holding the mutation writer during factories.
        /// Dispose each unit before the invocation. An unrelated publication makes this invocation stale;
        /// the caller must explicitly capture a new invocation rather than retrying a factory implicitly.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares immutable read state without replacing any NodeManager.
        /// At least one image is required; publication uses the same decision and routing switch as other units.
        /// </summary>
        /// <param name="images">The read images to publish.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit carrying the images.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
            ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default);

        /// <summary>
        /// Prepares and inspects a private candidate, then disposes it without publishing.
        /// The candidate is valid only during the inspection callback and cannot be committed.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements to validate.</param>
        /// <param name="inspectAsync">The callback inspecting the private candidate.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>A task that completes when the candidate has been inspected and disposed.</returns>
        ValueTask ValidateAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional validation of a publication candidate without allocating serving namespace mappings.
    /// </summary>
    public interface INodeManagerValidationPublication : INodeManagerPublication
    {
    }
}
