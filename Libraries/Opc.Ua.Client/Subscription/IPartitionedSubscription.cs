/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// <para>
    /// Surface exposing partition metadata for subscriptions whose
    /// monitored items are transparently split across multiple
    /// server-side subscriptions by the V2 engine. A "partition" is
    /// one server-side subscription that holds a slice of the
    /// logical subscription's monitored items; the engine creates
    /// new partitions on demand when the per-subscription cap
    /// (<see cref="ServerCapabilities.MaxMonitoredItemsPerSubscription"/>
    /// or <see cref="SubscriptionOptions.MaxMonitoredItemsPerPartition"/>)
    /// would be exceeded, and groups items pinned together via
    /// <see cref="MonitoredItems.MonitoredItemOptions.Affinity"/>
    /// into the same partition.
    /// </para>
    /// <para>
    /// The members on this interface are additive over
    /// <see cref="ISubscription"/>; consumers that do not care about
    /// partitioning can ignore this interface entirely. The default
    /// V2 implementation returned by
    /// <see cref="ISubscriptionManager.Add"/> implements this
    /// interface and callers can pattern-match or downcast to
    /// observe the partition layout (typically for diagnostics or
    /// resource accounting).
    /// </para>
    /// </summary>
    public interface IPartitionedSubscription : ISubscription
    {
        /// <summary>
        /// Number of underlying server-side partition subscriptions
        /// this logical subscription is currently split across.
        /// Always greater than or equal to <c>1</c> once any
        /// monitored item has been added; equal to <c>1</c> for
        /// subscriptions that fit inside the per-partition cap (the
        /// common case) and for subscriptions whose
        /// <see cref="SubscriptionOptions.DisableUnboundedItemMode"/>
        /// is <c>true</c>.
        /// </summary>
        int PartitionCount { get; }

        /// <summary>
        /// Server-assigned subscription identifiers for every
        /// partition that backs this logical subscription. The first
        /// entry corresponds to the primary partition; entries beyond
        /// the first identify secondary partitions created on demand.
        /// Empty until the primary partition has been created on the
        /// server (mirrors <see cref="ISubscription.Created"/>).
        /// </summary>
        IReadOnlyList<uint> PartitionIds { get; }
    }
}
