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
    /// Performs native source operations for generation-owned MonitoredItems.
    /// <para>
    /// The generation coordinator owns the one-pass handoff. These operations release or adopt
    /// one exact source incarnation; they do not own subscription membership, queues, unresolved
    /// state or recovery policy. Native durable restoration uses
    /// <see cref="IMonitoredItemManager.RestoreMonitoredItem"/> instead.
    /// </para>
    /// </summary>
    internal interface INodeManagerMonitoredItemSource
    {
        /// <summary>
        /// Captures a fence for notifications already queued on one node's consumer.
        /// Consumer lifetime belongs to the generation, not this fence.
        /// </summary>
        /// <param name="nodeId">The NodeId whose notifications must drain.</param>
        /// <returns>The pending-notification fence, or null when no consumer exists.</returns>
        Func<CancellationToken, ValueTask>? GetNotificationDrain(NodeId nodeId);

        /// <summary>
        /// Validates a candidate source before publication without changing the serving binding.
        /// </summary>
        /// <param name="monitoredItem">The MonitoredItem to test.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>
        /// A good result when the candidate source is compatible, otherwise the incompatibility.
        /// </returns>
        ValueTask<ServiceResult> ValidateSourceAsync(
            IMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases native ownership of the captured source without disposing the subscription item.
        /// A newer incarnation cannot be released by this operation.
        /// </summary>
        /// <param name="monitoredItem">The subscription item whose source is released.</param>
        /// <param name="sourceRevision">The exact source incarnation captured by the caller.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        ValueTask<ServiceResult> ReleaseSourceAsync(
            IMonitoredItem monitoredItem,
            long sourceRevision,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits a compatible native source while preserving the item's subscription identity and queue.
        /// The attempt carries the expected source incarnation and records only its own committed incarnation.
        /// </summary>
        /// <param name="monitoredItem">The subscription item whose source is adopted.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <param name="adoption">The attempt whose exact native source incarnation is recorded.</param>
        ValueTask<ServiceResult> AdoptSourceAsync(
            IMonitoredItem monitoredItem,
            MonitoredItemAdoption adoption,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Carries the exact source incarnation owned by one adoption, including a rolled-back native binding.
    /// </summary>
    internal sealed class MonitoredItemAdoption
    {
        /// <summary>
        /// Captures the source incarnation released before the target attempt.
        /// </summary>
        internal MonitoredItemAdoption(long sourceRevision)
        {
            SourceRevision = sourceRevision;
        }

        /// <summary>
        /// Gets or sets the incarnation committed by this attempt, never an unrelated adoption.
        /// </summary>
        internal long SourceRevision { get; set; }
    }

    /// <summary>
    /// Keeps local-deletion finalization and restoration scoped to the source actually released.
    /// </summary>
    /// <param name="Item">The subscription item whose native source was released.</param>
    /// <param name="Revision">The exact released incarnation, unchanged by unbinding.</param>
    internal readonly record struct MonitoredItemSourceRelease(IMonitoredItem Item, long Revision);
}
