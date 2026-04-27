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
        ValueTask<PublishResponse> PublishAsync(RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct = default);

        /// <summary>
        /// Transfer subscription
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionIds"></param>
        /// <param name="sendInitialValues"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader, ArrayOf<uint> subscriptionIds,
            bool sendInitialValues, CancellationToken ct = default);

        /// <summary>
        /// Delete subscriptions on server when we get publish
        /// responses for unknown subscriptions.
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="subscriptionIds"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader, ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default);
    }
}
