// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

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
        /// Notify every partition that the publish pipeline has
        /// paused or resumed. Mirrors
        /// <see cref="IManagedSubscription.NotifySubscriptionManagerPaused"/>.
        /// </summary>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
