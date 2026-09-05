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
    /// Raised when a factory returns a NodeManager that is already live.
    /// </summary>
    internal sealed class NodeManagerAlreadyRegisteredException : InvalidOperationException
    {
        public NodeManagerAlreadyRegisteredException()
            : base("The NodeManager is already registered.")
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public NodeManagerAlreadyRegisteredException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a custom message and inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The exception that caused this error.</param>
        public NodeManagerAlreadyRegisteredException(
            string message,
            Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Hosts NodeManagers that are added, replaced, or retired while the server is running.
    /// <para>
    /// A lifecycle operation runs in stages so that a failure never leaves a partially visible
    /// address space. A NodeManager is first prepared, which builds its address space without
    /// making it reachable. It is then published or swapped in for the NodeManager it replaces,
    /// and finally committed, which is the point at which Clients observe the change. Every stage
    /// that fails is undone by the matching rollback, and only a committed NodeManager is
    /// destroyed.
    /// </para>
    /// </summary>
    internal interface IDynamicNodeManagerHost
    {
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
        /// Adds a prepared NodeManager to the routing table without making it visible, so its
        /// Nodes can be resolved by the lifecycle operation but not yet by Clients.
        /// </summary>
        /// <param name="prepared">The prepared NodeManager.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask PublishAsync(
            PreparedNodeManager prepared,
            CancellationToken ct = default);

        /// <summary>
        /// Stages <paramref name="replacement"/> in place of <paramref name="current"/> so that
        /// the replaced NodeManager can be restored if a later stage fails.
        /// </summary>
        /// <param name="current">The NodeManager that is being replaced.</param>
        /// <param name="replacement">The prepared replacement.</param>
        /// <param name="allowActiveMonitoredItems">
        /// <c>true</c> to allow the replacement even when the current generation still owns
        /// active monitored items, as a shadow or immediate reload does; <c>false</c> to fail
        /// closed before any routing change, as an ordinary reload does.
        /// </param>
        /// <param name="retainReplacedNotifications">
        /// <c>true</c> to keep the replaced generation in session and existing all-events
        /// notification fan-out while its active MonitoredItems drain.
        /// </param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask ReplaceAsync(
            IAsyncNodeManager current,
            PreparedNodeManager replacement,
            bool allowActiveMonitoredItems = false,
            bool retainReplacedNotifications = false,
            CancellationToken ct = default);

        /// <summary>
        /// Makes a published NodeManager visible to Clients. The callbacks run inside the commit
        /// so that work which must not be observed in a half applied state, such as detaching and
        /// reattaching MonitoredItems, is ordered around the visibility boundary.
        /// </summary>
        /// <param name="prepared">The prepared NodeManager to commit.</param>
        /// <param name="beforeCommit">Runs while the change is still invisible to Clients.</param>
        /// <param name="afterCommit">Runs once the change is visible to Clients.</param>
        /// <param name="rollbackCommit">Undoes <paramref name="beforeCommit"/> when the commit fails.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask CommitAsync(
            PreparedNodeManager prepared,
            Func<ValueTask>? beforeCommit = null,
            Func<ValueTask>? afterCommit = null,
            Func<ValueTask>? rollbackCommit = null,
            CancellationToken ct = default);

        /// <summary>
        /// Hides a committed NodeManager from Clients while keeping it registered, so that it can
        /// be made visible again if the removal cannot be completed.
        /// </summary>
        /// <param name="nodeManager">The NodeManager to hide.</param>
        /// <param name="beforeUnpublish">Runs while the NodeManager is still visible.</param>
        /// <param name="rollbackUnpublish">Undoes <paramref name="beforeUnpublish"/> on failure.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask UnpublishAsync(
            IAsyncNodeManager nodeManager,
            Func<ValueTask>? beforeUnpublish = null,
            Func<ValueTask>? rollbackUnpublish = null,
            CancellationToken ct = default);

        /// <summary>
        /// Destroys the address space of a NodeManager that is no longer reachable. This method
        /// does not remove external references discovered during deletion and does not dispose the
        /// NodeManager. The lifecycle checkpoints this stage before performing either later action.
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
        /// Undoes a prepared, published, or staged NodeManager after a failed lifecycle operation
        /// and returns the address space to the state it had before the operation started.
        /// </summary>
        /// <param name="prepared">The prepared NodeManager to undo.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask RollbackAsync(
            PreparedNodeManager prepared,
            CancellationToken ct = default);

        /// <summary>
        /// Transfers the application NodeManagers created during server startup into the
        /// live lifecycle bookkeeping. Built-in configuration, diagnostics, and core
        /// NodeManagers are not included.
        /// </summary>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <returns>The already-published startup NodeManagers and their external references.</returns>
        ValueTask<ArrayOf<PreparedNodeManager>> TakeStartupNodeManagersAsync(
            CancellationToken ct = default);

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
        /// Reattaches MonitoredItems that were detached because their Node disappeared, once a
        /// compatible Node with the same NodeId is visible again.
        /// </summary>
        /// <param name="nodeManager">The NodeManager that gained the Nodes.</param>
        /// <param name="nodeIds">The Nodes that became available, or <c>null</c> for all.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask RecoverDetachedMonitoredItemsAsync(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId>? nodeIds = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables or suspends session and existing all-events notifications for a
        /// shadow-retired generation. Suspending notifications establishes the cutoff
        /// used before its final request drain and destruction.
        /// </summary>
        /// <param name="nodeManager">The shadow-retired generation.</param>
        /// <param name="enabled"><c>true</c> to enable notifications; otherwise <c>false</c>.</param>
        void SetRetiredGenerationNotifications(
            IAsyncNodeManager nodeManager,
            bool enabled);

        /// <summary>
        /// Disables new lifecycle-protected notifications and waits for dispatches that already
        /// captured <paramref name="nodeManager"/> to finish, without changing its retained
        /// all-events subscription snapshot.
        /// </summary>
        /// <param name="nodeManager">The generation whose dispatches must drain.</param>
        /// <param name="ct">The token used to cancel the wait.</param>
        ValueTask WaitForNotificationDispatchesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);

        /// <summary>
        /// Unsubscribes a shadow-retired generation from the exact all-events monitored
        /// items captured when it was retired. Items already handled by an in-flight
        /// deletion are absent, while deletions excluded after notification suspension
        /// remain in the snapshot and are finalized here.
        /// </summary>
        /// <param name="nodeManager">The shadow-retired generation.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        ValueTask FinalizeRetiredGenerationNotificationsAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Carries a NodeManager and the bookkeeping a lifecycle operation needs to move it through
    /// the prepare, publish, commit, and rollback stages of <see cref="IDynamicNodeManagerHost"/>.
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
        /// Gets the references the NodeManager adds to Nodes owned by other NodeManagers.
        /// </summary>
        public Dictionary<NodeId, IList<IReference>> ExternalReferences { get; }

        /// <summary>
        /// Gets or sets whether lifecycle operations initiated from an OPC UA request callback
        /// are allowed for this NodeManager.
        /// </summary>
        public bool AllowLifecycleFromRequestCallback { get; set; }

        /// <summary>
        /// Gets or sets whether the NodeManager was added to the routing table and therefore has
        /// to be removed again by a rollback.
        /// </summary>
        public bool Published { get; set; }

        /// <summary>
        /// Gets or sets whether the NodeManager was staged in place of another one and therefore
        /// has to be swapped back by a rollback.
        /// </summary>
        public bool Staged { get; set; }

        /// <summary>
        /// Gets or sets the NodeManager this one replaces, which a rollback restores.
        /// </summary>
        public IAsyncNodeManager? ReplacedNodeManager { get; set; }

        /// <summary>
        /// Gets or sets the external references of the replaced NodeManager, which a rollback
        /// restores together with it.
        /// </summary>
        public Dictionary<NodeId, IList<IReference>>? ReplacedExternalReferences { get; set; }

        /// <summary>
        /// Gets or sets whether <see cref="ReplacedNodeManager"/> may still own active
        /// monitored items when this replacement is committed. Set by
        /// <see cref="IDynamicNodeManagerHost.ReplaceAsync"/> for a shadow reload; the
        /// replaced generation is preserved for its existing monitored items and is torn
        /// down only after they drain.
        /// </summary>
        public bool AllowActiveMonitoredItems { get; set; }

        /// <summary>
        /// Gets or sets whether the replaced generation remains in session and existing
        /// all-events notification fan-out after the routing swap.
        /// </summary>
        public bool RetainReplacedNotifications { get; set; }
    }
}
