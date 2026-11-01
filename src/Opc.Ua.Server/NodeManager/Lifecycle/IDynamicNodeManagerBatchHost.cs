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
    /// Identifies lifecycle operations that a callback may re-enter without reacquiring its current admission.
    /// </summary>
    [Flags]
    internal enum NodeManagerAdmissionCapability
    {
        /// <summary>
        /// No re-entry capability is granted.
        /// </summary>
        None = 0,

        /// <summary>
        /// The callback may publish address-space changes.
        /// </summary>
        AddressSpaceMutation = 1,

        /// <summary>
        /// The callback may publish server lifecycle changes.
        /// </summary>
        ServerLifecycle = 2
    }

    /// <summary>
    /// Carries semantic host admission for one lifecycle batch commit.
    /// </summary>
    internal interface INodeManagerBatchAdmission : IDisposable
    {
        /// <summary>
        /// Determines whether the host granted the specified lifecycle capability.
        /// </summary>
        /// <param name="capability">The capability to check.</param>
        /// <returns>True when the capability is currently available.</returns>
        bool Includes(NodeManagerAdmissionCapability capability);

        /// <summary>
        /// Releases the specified lifecycle capability before an external durable decision.
        /// </summary>
        /// <param name="capability">The capability to release.</param>
        void Release(NodeManagerAdmissionCapability capability);
    }

    /// <summary>
    /// Carries semantic capabilities for lifecycle re-entry from a manager callback.
    /// </summary>
    internal sealed class NodeManagerCallbackPermit : IAsyncDisposable
    {
        /// <summary>
        /// Establishes a scoped permit in the current asynchronous execution context.
        /// </summary>
        /// <param name="current">The execution-context slot for callback permits.</param>
        /// <param name="capabilities">The lifecycle capabilities granted to the callback.</param>
        internal NodeManagerCallbackPermit(
            AsyncLocal<NodeManagerCallbackPermit?> current,
            NodeManagerAdmissionCapability capabilities)
        {
            m_current = current;
            Previous = current.Value;
            Capabilities = capabilities;
            current.Value = this;
        }

        /// <summary>
        /// Gets the preceding callback permit.
        /// </summary>
        internal NodeManagerCallbackPermit? Previous { get; }

        /// <summary>
        /// Gets the lifecycle capabilities granted to this callback.
        /// </summary>
        internal NodeManagerAdmissionCapability Capabilities { get; }

        /// <summary>
        /// Gets whether this permit remains active.
        /// </summary>
        internal bool IsActive => Volatile.Read(ref m_active) != 0;

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref m_active, 0);
            RestoreContext();
            return default;
        }

        private void RestoreContext()
        {
            if (ReferenceEquals(m_current.Value, this))
            {
                m_current.Value = Previous;
            }
        }

        private readonly AsyncLocal<NodeManagerCallbackPermit?> m_current;
        private int m_active = 1;
    }

    /// <summary>
    /// Carries the candidate routing image and callbacks for a batch publication.
    /// </summary>
    /// <param name="Candidates">The prepared node managers to add or replace.</param>
    /// <param name="Removed">The node managers removed by the batch.</param>
    /// <param name="TypeTree">The candidate type tree.</param>
    /// <param name="OriginalTypes">The type tree the batch was prepared against.</param>
    /// <param name="TypeRevision">The revision of the original type tree.</param>
    /// <param name="Factory">The candidate encodeable factory.</param>
    /// <param name="OriginalFactory">The encodeable factory the batch was prepared against.</param>
    /// <param name="FactoryRevision">The revision of the original factory.</param>
    /// <param name="ReadImages">The read images to bind when the batch is published.</param>
    /// <param name="DecideAsync">The callback deciding whether to publish, or null to validate only.</param>
    /// <param name="ViewPublished">The callback marking the routing-view commit point.</param>
    /// <param name="Published">The callback invoked after the candidate has been published.</param>
    /// <param name="ReconcileBindingsAsync">The callback reconciling monitored item bindings.</param>
    /// <param name="DeferReferenceRemoval">
    /// The callback recording external reference removals that cannot be applied to a staged NodeState.
    /// </param>
    /// <param name="ReportCleanupFailure">The callback receiving cleanup failures.</param>
    /// <param name="CancellationToken">The token to cancel the operation.</param>
    /// <param name="Preparation">The explicit private metadata staging context.</param>
    /// <param name="Reconciliation">The server-owned post-commit checkpoints.</param>
    /// <param name="References">Reference edits discovered before publication serialization.</param>
    /// <param name="CallbackPermit">The callback permit captured when preparation began, if any.</param>
    /// <param name="Admission">The semantic host admission acquired before mutation ownership.</param>
    internal sealed record NodeManagerBatchContext(
        ArrayOf<PreparedNodeManager> Candidates,
        ArrayOf<IAsyncNodeManager> Removed,
        TypeTable TypeTree,
        TypeTable OriginalTypes,
        long TypeRevision,
        EncodeableFactory Factory,
        EncodeableFactory OriginalFactory,
        long FactoryRevision,
        ArrayOf<INodeManagerReadImage> ReadImages,
        Func<CancellationToken, ValueTask>? DecideAsync,
        Action ViewPublished,
        Func<ValueTask> Published,
        Func<ValueTask> ReconcileBindingsAsync,
        Action<IAsyncNodeManager, List<LocalReference>> DeferReferenceRemoval,
        Action<Exception> ReportCleanupFailure,
        ChangePreparer Preparation,
        GenerationReconciler.ReconciliationWork Reconciliation,
        CancellationToken CancellationToken,
        PreparedAddressSpaceReferences? References = null,
        NodeManagerCallbackPermit? CallbackPermit = null,
        INodeManagerBatchAdmission? Admission = null);

    /// <summary>
    /// Publication responsibilities of the unified dynamic NodeManager host.
    /// </summary>
    internal interface IDynamicNodeManagerBatchHost
    {
        /// <summary>
        /// Gets the current live routing image whose identity is the preparation revision.
        /// </summary>
        NodeManagerRoutingTable.RoutingSnapshot RoutingRevision { get; }

        /// <summary>
        /// Captures the callback permit associated with the current asynchronous execution context.
        /// </summary>
        /// <returns>The current callback permit, if present.</returns>
        NodeManagerCallbackPermit? CaptureLifecycleCallbackPermit();

        /// <summary>
        /// Stops routing item deletions to a retired generation after its all-events bindings were released.
        /// </summary>
        /// <param name="generation">The retired generation whose bindings were released.</param>
        void ReleaseRetiringEventGeneration(NodeManagerGeneration generation);

        /// <summary>
        /// Resolves reference owners under the captured view without holding lifecycle mutation ownership.
        /// </summary>
        /// <param name="candidates">Private additions and replacements.</param>
        /// <param name="removed">Explicit removals.</param>
        /// <param name="view">The serving view captured by the transaction.</param>
        /// <param name="callbackPermit">The callback permit captured when preparation began, if any.</param>
        /// <param name="cancellationToken">Cancels reference discovery.</param>
        /// <returns>The versioned reference edits to reserve before deciding.</returns>
        ValueTask<PreparedAddressSpaceReferences> PrepareReferencesAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            NodeManagerRoutingTable.RoutingSnapshot view,
            CancellationToken cancellationToken,
            NodeManagerCallbackPermit? callbackPermit = null);

        /// <summary>
        /// Acquires all required host admission capabilities without retaining a partial set while waiting.
        /// </summary>
        /// <param name="callbackPermit">The callback permit captured when preparation began, if any.</param>
        /// <param name="cancellationToken">The token used to cancel admission.</param>
        /// <returns>The semantic admission granted by the host.</returns>
        ValueTask<INodeManagerBatchAdmission> AcquireCommitAdmissionAsync(
            NodeManagerCallbackPermit? callbackPermit,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reserves and checks the complete candidate, then publishes it when a decision is supplied.
        /// A null decision validates and releases the reservations without changing the serving image.
        /// </summary>
        /// <param name="context">The batch publication inputs and callbacks.</param>
        /// <returns>A task that completes when the batch has been validated or published.</returns>
        ValueTask CommitBatchAsync(NodeManagerBatchContext context);
    }
}
