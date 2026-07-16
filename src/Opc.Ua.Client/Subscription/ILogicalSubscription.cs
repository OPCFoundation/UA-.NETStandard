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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// <para>
    /// Non-SDK interface implemented by the V2 logical-subscription
    /// wrapper (<see cref="LogicalSubscription"/>) so the
    /// <see cref="SubscriptionManager"/> can fan operations out to
    /// the wrapper's underlying server-side partition subscriptions
    /// without taking a dependency on the concrete class.
    /// </para>
    /// <para>
    /// Distinct from <see cref="IManagedSubscription"/>: the
    /// per-partition state machine still implements
    /// <see cref="IManagedSubscription"/> / <see cref="IMessageProcessor"/>
    /// since those abstractions assume a single server-side
    /// <c>Id</c>, a single publish-sequence stream, and a single
    /// acknowledgement queue — properties the wrapper does not have.
    /// </para>
    /// </summary>
    internal interface ILogicalSubscription : IPartitionedSubscription
    {
        /// <summary>
        /// All underlying partition subscriptions that back this
        /// logical subscription. The first entry is the primary
        /// partition (whose id is exposed as <c>ServerId</c> on the
        /// concrete wrapper). The order matches
        /// <see cref="IPartitionedSubscription.PartitionIds"/>.
        /// </summary>
        IReadOnlyList<IManagedSubscription> Partitions { get; }

        /// <summary>
        /// Recreate every partition on the new session after
        /// reconnect/recreate. Mirrors
        /// <see cref="IManagedSubscription.RecreateAsync"/>; called
        /// by <see cref="SubscriptionManager.RecreateSubscriptionsAsync"/>.
        /// </summary>
        ValueTask RecreateAsync(CancellationToken ct = default);

        /// <summary>
        /// Notify every partition that the publish pipeline has
        /// paused or resumed. Mirrors
        /// <see cref="IManagedSubscription.NotifySubscriptionManagerPaused"/>.
        /// </summary>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
