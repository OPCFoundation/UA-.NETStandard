// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Message acknoledgement queue
    /// </summary>
    internal interface IMessageAckQueue
    {
        /// <summary>
        /// Subscriptions queue acknoledgements for completed
        /// notifications as soon as they are dispatched / handled.
        /// </summary>
        /// <param name="ack"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask QueueAsync(SubscriptionAcknowledgement ack,
            CancellationToken ct = default);

        /// <summary>
        /// Complete subscription
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask CompleteAsync(uint subscriptionId,
            CancellationToken ct = default);
    }
}
