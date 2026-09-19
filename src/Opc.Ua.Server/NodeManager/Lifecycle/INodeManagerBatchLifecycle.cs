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
    /// Optional lifecycle capability for privately preparing a complete publication unit.
    /// </summary>
    public interface INodeManagerBatchLifecycle : INodeManagerLifecycle
    {
        /// <summary>
        /// Prepares the requested additions, replacements and retirements without publishing them.
        /// The change sequence is copied before the operation can yield.
        /// The returned owner must be committed or asynchronously disposed.
        /// </summary>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns an unpublished unit and all resources acquired while preparing it.
    /// </summary>
    public interface IPreparedNodeManagerBatch : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact candidate registrations in addition/replacement order.
        /// They are not live until the commit publishes the unit.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether this unit has crossed its irreversible publication decision.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// Rechecks ownership and the captured routing revision, invokes the durable decision once,
        /// then publishes one routing image.
        /// Returning from the decision means committed; its confirmed noncommit or indeterminate
        /// exceptions must escape it. A caller with a committed durability warning must retain that
        /// warning and return normally so publication completes. After the decision, cancellation
        /// cannot undo the unit; cleanup failures are returned with the committed registrations.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Routing changed after preparation; the durable decision is not invoked.
        /// </exception>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the same unit and publishes its already-prepared in-memory state immediately
        /// after the routing switch, before readiness and retirement reconciliation.
        /// The publication callback must perform no I/O or additional durable decision.
        /// A callback failure occurs after commit and cannot make the batch uncommitted.
        /// </summary>
        ValueTask<NodeManagerBatchResult> CommitAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            Action publishCommittedState,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One addition, replacement or retirement in a prepared publication unit.
    /// </summary>
    public sealed class NodeManagerBatchChange
    {
        private NodeManagerBatchChange(
            NodeManagerRegistration? current,
            IAsyncNodeManagerFactory? factory,
            bool immediate)
        {
            Current = current;
            Factory = factory;
            Immediate = immediate;
        }

        /// <summary>
        /// Gets the exact registration being replaced or retired, or null for an addition.
        /// </summary>
        public NodeManagerRegistration? Current { get; }

        /// <summary>
        /// Gets the candidate factory, or null for a retirement without replacement.
        /// </summary>
        public IAsyncNodeManagerFactory? Factory { get; }

        /// <summary>
        /// Gets whether old monitored sources are invalidated instead of gracefully retained.
        /// </summary>
        public bool Immediate { get; }

        /// <summary>
        /// Adds a new privately prepared owner.
        /// </summary>
        public static NodeManagerBatchChange Add(IAsyncNodeManagerFactory factory)
        {
            return new NodeManagerBatchChange(
                null, factory ?? throw new ArgumentNullException(nameof(factory)), false);
        }

        /// <summary>
        /// Replaces an exact registration while preserving its logical identity.
        /// </summary>
        public static NodeManagerBatchChange Replace(
            NodeManagerRegistration current,
            IAsyncNodeManagerFactory factory,
            bool immediate = false)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)),
                factory ?? throw new ArgumentNullException(nameof(factory)),
                immediate);
        }

        /// <summary>
        /// Retires an exact registration in the same switch as the other changes.
        /// </summary>
        public static NodeManagerBatchChange Remove(
            NodeManagerRegistration current,
            bool immediate = false)
        {
            return new NodeManagerBatchChange(
                current ?? throw new ArgumentNullException(nameof(current)), null, immediate);
        }
    }

    /// <summary>
    /// The committed registrations and any post-decision reconciliation failure.
    /// </summary>
    public sealed class NodeManagerBatchResult
    {
        internal NodeManagerBatchResult(
            ArrayOf<NodeManagerRegistration> registrations,
            uint retired,
            Exception? cleanupFailure)
        {
            Registrations = registrations;
            Retired = retired;
            CleanupFailure = cleanupFailure;
        }

        /// <summary>
        /// Gets the exact committed additions and replacements.
        /// </summary>
        public ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets the number of old generations whose cleanup actually completed.
        /// </summary>
        public uint Retired { get; }

        /// <summary>
        /// Gets a post-commit failure, if any. It never denotes an aborted publication.
        /// </summary>
        public Exception? CleanupFailure { get; }
    }
}
