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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Hosts NodeManagers that are added, replaced, or retired while the server is running.
    /// <para>
    /// Candidates are prepared privately and published by the unified batch host.
    /// Failed preparation can only abort private candidates; committed generations are
    /// reconciled or removed through another transaction, never structurally rolled back.
    /// </para>
    /// </summary>
    internal interface IDynamicNodeManagerHost : IDynamicNodeManagerBatchHost
    {
        /// <summary>
        /// Gets the server-owned reconciler for exact generation binding records.
        /// </summary>
        GenerationReconciler GenerationReconciler { get; }

        /// <summary>
        /// Builds the address space of <paramref name="nodeManager"/> without making it reachable,
        /// and collects the references it wants to add to Nodes owned by other NodeManagers.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to prepare.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <returns>The prepared NodeManager, which is the input to every later stage.</returns>
        ValueTask<PreparedNodeManager> PrepareAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        /// <summary>
        /// Destroys the address space of a NodeManager that is no longer reachable. This method
        /// does not remove external references discovered during deletion and does not dispose the
        /// NodeManager. The lifecycle checkpoints this stage before performing either later action.
        /// The caller must retain exclusive ownership of the detached generation throughout
        /// deletion; the callback may perform lifecycle operations on its dependencies.
        /// </summary>
        /// <param name="nodeManager">The NodeManager whose address space is torn down.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask DestroyAddressSpaceAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        /// <summary>
        /// Removes external references discovered while destroying
        /// <paramref name="nodeManager"/>'s address space. This separate, retryable stage ensures
        /// a failure in another NodeManager never repeats third-party address-space deletion.
        /// A reload does not invoke this stage because its replacement re-adds the references.
        /// </summary>
        /// <param name="nodeManager">The destroyed NodeManager whose references are removed.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask RemoveDestroyedExternalReferencesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        /// <summary>
        /// Destroys a private candidate after preparation fails or its transaction is abandoned.
        /// A candidate that has already been published cannot be aborted.
        /// </summary>
        /// <param name="prepared">The prepared NodeManager to undo.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask AbortAsync(
            PreparedNodeManager prepared,
            CancellationToken ct = default);

        /// <summary>
        /// Gets application factories to publish through initial Add changes.
        /// </summary>
        ArrayOf<IAsyncNodeManagerFactory> InitialFactories { get; }

        /// <summary>
        /// Gets the stable slots supplying the server's protected system roles.
        /// </summary>
        ArrayOf<NodeManagerSlot> SystemSlots { get; }

        /// <summary>
        /// Routes hosted shutdown and disposal admission through the canonical generation owner.
        /// </summary>
        void SetLifetimeOwner(Func<CancellationToken, ValueTask> shutdown, Func<bool> canDispose);

        /// <summary>
        /// Rejects preconstructed applications that cannot participate in private initial preparation.
        /// </summary>
        void ValidateStartupFactories();

        /// <summary>
        /// Drops the host bookkeeping for a NodeManager that the caller has taken over, without
        /// tearing its address space down.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to release.</param>
        void Release(IAsyncNodeManager nodeManager);

        /// <summary>
        /// Registers a callback the host invokes (from an ownership-sensitive monitored
        /// item request such as Delete) once monitored items owned by a shadow-retired
        /// generation may have drained. The callback must not tear down anything inline;
        /// it schedules cleanup off the request path.
        /// </summary>
        void SetRetiredGenerationDrainObserver(Action? observer);

        /// <summary>
        /// Resolves nonterminal monitored-item source bindings when compatible Nodes become available
        /// on their stable slot again.
        /// </summary>
        /// <param name="nodeManager">The NodeManager that gained the Nodes.</param>
        /// <param name="nodeIds">The Nodes that became available, or <c>null</c> for all.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask ResolveUnresolvedSourcesAsync(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId>? nodeIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes generation admission, waits for admitted callbacks to drain, and releases
        /// the generation's remaining exact all-events binding records.
        /// </summary>
        /// <param name="nodeManager">The NodeManager whose generation is being finalized.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask FinalizeRetiredGenerationNotificationsAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Carries a NodeManager and the bookkeeping a lifecycle operation needs to move it through
    /// private preparation, batch publication, and abort cleanup.
    /// </summary>
    internal sealed class PreparedNodeManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedNodeManager"/> class.
        /// </summary>
        /// <param name="nodeManager">The NodeManager that was prepared.</param>
        /// <param name="externalReferences">
        /// The references the NodeManager adds to Nodes owned by other NodeManagers.
        /// </param>
        public PreparedNodeManager(
            IAsyncNodeManager nodeManager,
            Dictionary<NodeId, IList<IReference>> externalReferences)
        {
            NodeManager = nodeManager;
            ExternalReferences = externalReferences;
        }

        /// <summary>
        /// Gets the NodeManager that was prepared.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// Gets or sets the explicit metadata preparation, if this is a runtime candidate.
        /// </summary>
        public ChangePreparer? Preparation { get; set; }

        /// <summary>
        /// Gets or sets the exact generation identity assigned to this prepared NodeManager.
        /// </summary>
        public NodeManagerGeneration? Generation { get; set; }

        /// <summary>
        /// Gets the references the NodeManager adds to Nodes owned by other NodeManagers.
        /// </summary>
        public Dictionary<NodeId, IList<IReference>> ExternalReferences { get; }

        /// <summary>
        /// Gets or sets whether the candidate crossed the routing-view commit point.
        /// </summary>
        public bool Published { get; set; }

        /// <summary>
        /// Gets or sets the exact predecessor withdrawn by this candidate's publication.
        /// </summary>
        public IAsyncNodeManager? ReplacedNodeManager { get; set; }

    }
}
