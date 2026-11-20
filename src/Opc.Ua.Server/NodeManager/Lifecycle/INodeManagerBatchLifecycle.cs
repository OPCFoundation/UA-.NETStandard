// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Compatibility marker for lifecycles that expose prepared transactions.
    /// New callers should use the transaction members on <see cref="INodeManagerLifecycle"/>.
    /// </summary>
    public interface INodeManagerBatchLifecycle : INodeManagerLifecycle
    {
    }

    /// <summary>
    /// Source-compatible entry points for the one-release context migration window.
    /// Legacy contexts are ignored; request callbacks use the canonical transaction path.
    /// </summary>
    public static class NodeManagerBatchLifecycleExtensions
    {
        /// <summary>
        /// Preserves static calls to the former batch preparation helper.
        /// </summary>
        /// <param name="lifecycle">The batch lifecycle.</param>
        /// <param name="changes">The changes to prepare.</param>
        /// <param name="cancellationToken">The token to cancel preparation.</param>
        /// <returns>The unpublished prepared owner.</returns>
        [Obsolete("Use the PrepareAsync member on INodeManagerLifecycle.")]
        public static ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            this INodeManagerBatchLifecycle lifecycle,
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }
            return lifecycle.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Prepares a transaction, ignoring the legacy request context.
        /// </summary>
        /// <param name="lifecycle">The lifecycle.</param>
        /// <param name="changes">The additions, replacements and retirements to prepare.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        [Obsolete("Use PrepareAsync(changes, cancellationToken). The request context is no longer required.")]
        public static ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            this INodeManagerLifecycle lifecycle,
            ArrayOf<NodeManagerBatchChange> changes,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default)
        {
            if (lifecycle == null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            return lifecycle.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Reserves an invocation, ignoring the legacy request context.
        /// </summary>
        /// <param name="capture">The publication capture.</param>
        /// <param name="callerContext">The unused legacy request context.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The invocation that retains publication admission until disposed.</returns>
        [Obsolete("Use BeginAsync(cancellationToken). The request context is no longer required.")]
        public static ValueTask<INodeManagerPublication> BeginAsync(
            this INodeManagerPublicationCapture capture,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default)
        {
            if (capture == null)
            {
                throw new ArgumentNullException(nameof(capture));
            }

            return capture.BeginAsync(cancellationToken);
        }

        /// <summary>
        /// Preserves static calls to the former context-free admission helper.
        /// </summary>
        /// <param name="capture">The publication capture.</param>
        /// <param name="cancellationToken">The token to cancel admission.</param>
        /// <returns>The invocation retaining publication admission.</returns>
        [Obsolete("Use the BeginAsync member on INodeManagerPublicationCapture.")]
        public static ValueTask<INodeManagerPublication> BeginAsync(
            this INodeManagerPublicationCapture capture,
            CancellationToken cancellationToken = default)
        {
            if (capture is null)
            {
                throw new ArgumentNullException(nameof(capture));
            }
            return capture.BeginAsync(cancellationToken);
        }
    }
}
