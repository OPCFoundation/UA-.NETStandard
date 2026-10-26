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
}
