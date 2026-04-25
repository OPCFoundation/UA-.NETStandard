#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Non sdk interface that allows subscription manager to manage
    /// subcriptions. Must be implemted by subscriptions to be
    /// manageable by the <see cref="SubscriptionManager"/>.
    /// </summary>
    internal interface IManagedSubscription : ISubscription, IMessageProcessor
    {
        /// <summary>
        /// Called after the subscription was transferred.
        /// </summary>
        /// <param name="availableSequenceNumbers">A list of sequence number
        /// ranges that identify NotificationMessages that are in the
        /// Subscription’s retransmission queue.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct = default);

        /// <summary>
        /// Recreate the subscription on a new session
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask RecreateAsync(CancellationToken ct = default);

        /// <summary>
        /// Notify subscription that the subscription manager has paused or
        /// resumed operations.
        /// </summary>
        /// <param name="paused"></param>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
#endif
