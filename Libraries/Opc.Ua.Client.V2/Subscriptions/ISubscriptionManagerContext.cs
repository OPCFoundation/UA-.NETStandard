// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Microsoft.Extensions.Options;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscription manager context
    /// </summary>
    internal interface ISubscriptionManagerContext
    {
        /// <summary>
        /// Create a managed subscription
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="options">The subscription options to pass</param>
        /// <param name="queue">The completion queue</param>
        /// <returns></returns>
        IManagedSubscription CreateSubscription(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options, IMessageAckQueue queue);

        /// <summary>
        /// Publish service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionAcknowledgements"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<PublishResponse> PublishAsync(RequestHeader? requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct = default);

        /// <summary>
        /// Transfer subscription
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionIds"></param>
        /// <param name="sendInitialValues"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader, UInt32Collection subscriptionIds,
            bool sendInitialValues, CancellationToken ct = default);

        /// <summary>
        /// Delete subscriptions on server when we get publish
        /// responses for unknown subscriptions.
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader, UInt32Collection subscriptionIds,
            CancellationToken ct = default);
    }
}
