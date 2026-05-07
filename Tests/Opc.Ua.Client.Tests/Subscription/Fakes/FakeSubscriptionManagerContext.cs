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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="ISubscriptionManagerContext"/>.
    /// Records every invocation and lets tests override return behaviour
    /// via callback fields. Replaces
    /// <c>Mock&lt;ISubscriptionManagerContext&gt;</c>.
    /// </summary>
    internal sealed class FakeSubscriptionManagerContext : ISubscriptionManagerContext
    {
        /// <summary>Recorded calls to <see cref="CreateSubscription"/>.</summary>
        public List<CreateSubscriptionCall> CreateSubscriptionCalls { get; } = [];

        /// <summary>Recorded calls to <see cref="PublishAsync"/>.</summary>
        public List<PublishCall> PublishCalls { get; } = [];

        /// <summary>Recorded calls to <see cref="TransferSubscriptionsAsync"/>.</summary>
        public List<TransferCall> TransferCalls { get; } = [];

        /// <summary>Recorded calls to <see cref="DeleteSubscriptionsAsync"/>.</summary>
        public List<DeleteCall> DeleteCalls { get; } = [];

        /// <summary>
        /// Required factory for <see cref="CreateSubscription"/>. Tests must
        /// assign this before invoking the manager.
        /// </summary>
        public Func<ISubscriptionNotificationHandler,
            IOptionsMonitor<SubscriptionOptions>, IMessageAckQueue,
            IManagedSubscription> CreateSubscriptionFactory { get; set; }
            = (_, _, _) => throw new InvalidOperationException(
                "CreateSubscriptionFactory not set on FakeSubscriptionManagerContext.");

        /// <summary>
        /// Optional override for <see cref="PublishAsync"/>. If null,
        /// returns a default <see cref="PublishResponse"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<SubscriptionAcknowledgement>,
            CancellationToken, ValueTask<PublishResponse>>? OnPublishAsync { get; set; }

        /// <summary>
        /// Optional override for <see cref="TransferSubscriptionsAsync"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<uint>, bool, CancellationToken,
            ValueTask<TransferSubscriptionsResponse>>? OnTransferSubscriptionsAsync { get; set; }

        /// <summary>
        /// Optional override for <see cref="DeleteSubscriptionsAsync"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<uint>, CancellationToken,
            ValueTask<DeleteSubscriptionsResponse>>? OnDeleteSubscriptionsAsync { get; set; }

        public IManagedSubscription CreateSubscription(
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options,
            IMessageAckQueue queue)
        {
            CreateSubscriptionCalls.Add(new CreateSubscriptionCall(handler,
                options, queue));
            return CreateSubscriptionFactory(handler, options, queue);
        }

        public ValueTask<PublishResponse> PublishAsync(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            PublishCalls.Add(new PublishCall(requestHeader,
                subscriptionAcknowledgements));
            return OnPublishAsync?.Invoke(requestHeader,
                subscriptionAcknowledgements, ct)
                ?? new ValueTask<PublishResponse>(new PublishResponse());
        }

        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader, ArrayOf<uint> subscriptionIds,
            bool sendInitialValues, CancellationToken ct = default)
        {
            TransferCalls.Add(new TransferCall(requestHeader, subscriptionIds,
                sendInitialValues));
            return OnTransferSubscriptionsAsync?.Invoke(requestHeader,
                subscriptionIds, sendInitialValues, ct)
                ?? new ValueTask<TransferSubscriptionsResponse>(
                    new TransferSubscriptionsResponse());
        }

        public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader? requestHeader, ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            DeleteCalls.Add(new DeleteCall(requestHeader, subscriptionIds));
            return OnDeleteSubscriptionsAsync?.Invoke(requestHeader,
                subscriptionIds, ct)
                ?? new ValueTask<DeleteSubscriptionsResponse>(
                    new DeleteSubscriptionsResponse());
        }

        internal readonly record struct CreateSubscriptionCall(
            ISubscriptionNotificationHandler Handler,
            IOptionsMonitor<SubscriptionOptions> Options,
            IMessageAckQueue Queue);

        internal readonly record struct PublishCall(
            RequestHeader? RequestHeader,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements);

        internal readonly record struct TransferCall(
            RequestHeader? RequestHeader, ArrayOf<uint> SubscriptionIds,
            bool SendInitialValues);

        internal readonly record struct DeleteCall(
            RequestHeader? RequestHeader, ArrayOf<uint> SubscriptionIds);
    }
}
