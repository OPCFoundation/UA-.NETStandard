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
        public IReadOnlyList<CreateSubscriptionCall> CreateSubscriptionCalls
            => Snapshot(m_createSubscriptionCalls);

        /// <summary>Recorded calls to <see cref="PublishAsync"/>.</summary>
        public IReadOnlyList<PublishCall> PublishCalls => Snapshot(m_publishCalls);

        /// <summary>Recorded calls to <see cref="TransferSubscriptionsAsync"/>.</summary>
        public IReadOnlyList<TransferCall> TransferCalls => Snapshot(m_transferCalls);

        /// <summary>Recorded calls to <see cref="DeleteSubscriptionsAsync"/>.</summary>
        public IReadOnlyList<DeleteCall> DeleteCalls => Snapshot(m_deleteCalls);

        /// <summary>
        /// Number of recorded calls to <see cref="DeleteSubscriptionsAsync"/>.
        /// Polling loops use this instead of <see cref="DeleteCalls"/> so a
        /// wait condition does not allocate a snapshot on every iteration.
        /// </summary>
        public int DeleteCallsCount => Count(m_deleteCalls);

        /// <summary>
        /// Required factory for <see cref="CreateSubscription"/>. Tests must
        /// assign this before invoking the manager.
        /// </summary>
        public Func<ISubscriptionNotificationHandler,
            IOptionsMonitor<SubscriptionOptions>, IMessageAckQueue,
            IManagedSubscription> CreateSubscriptionFactory
        { get; set; }
            = (_, _, _) => throw new InvalidOperationException(
                "CreateSubscriptionFactory not set on FakeSubscriptionManagerContext.");

        /// <summary>
        /// Optional override for <see cref="PublishAsync"/>. If null,
        /// returns a default <see cref="PublishResponse"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<SubscriptionAcknowledgement>,
            CancellationToken, ValueTask<PublishResponse>>? OnPublishAsync
        { get; set; }

        /// <summary>
        /// Optional override for <see cref="TransferSubscriptionsAsync"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<uint>, bool, CancellationToken,
            ValueTask<TransferSubscriptionsResponse>>? OnTransferSubscriptionsAsync
        { get; set; }

        /// <summary>
        /// Optional override for <see cref="DeleteSubscriptionsAsync"/>.
        /// </summary>
        public Func<RequestHeader?, ArrayOf<uint>, CancellationToken,
            ValueTask<DeleteSubscriptionsResponse>>? OnDeleteSubscriptionsAsync
        { get; set; }

        public IManagedSubscription CreateSubscription(
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options,
            IMessageAckQueue queue,
            SubscriptionLoadState? loadState = null)
        {
            Record(m_createSubscriptionCalls,
                new CreateSubscriptionCall(handler, options, queue, loadState));
            return CreateSubscriptionFactory(handler, options, queue);
        }

        public ValueTask<PublishResponse> PublishAsync(
            RequestHeader? requestHeader,
            ArrayOf<SubscriptionAcknowledgement> subscriptionAcknowledgements,
            CancellationToken ct = default)
        {
            Record(m_publishCalls, new PublishCall(requestHeader,
                subscriptionAcknowledgements));
            return OnPublishAsync?.Invoke(requestHeader,
                subscriptionAcknowledgements, ct)
                ?? new ValueTask<PublishResponse>(new PublishResponse());
        }

        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader? requestHeader, ArrayOf<uint> subscriptionIds,
            bool sendInitialValues, CancellationToken ct = default)
        {
            Record(m_transferCalls, new TransferCall(requestHeader, subscriptionIds,
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
            Record(m_deleteCalls, new DeleteCall(requestHeader, subscriptionIds));
            return OnDeleteSubscriptionsAsync?.Invoke(requestHeader,
                subscriptionIds, ct)
                ?? new ValueTask<DeleteSubscriptionsResponse>(
                    new DeleteSubscriptionsResponse());
        }

        /// <summary>
        /// Identifiers the fake session claims outside the manager's registry,
        /// standing in for subscriptions created through the classic API.
        /// </summary>
        public HashSet<uint> SessionOwnedSubscriptionIds { get; } = [];

        /// <inheritdoc/>
        public int SessionSubscriptionCount => SessionOwnedSubscriptionIds.Count;

        /// <summary>Recorded dispatches to session-owned subscriptions.</summary>
        public int SessionDispatchCount => Volatile.Read(ref m_sessionDispatchCount);

        public bool TryDispatchToSessionSubscription(
            uint subscriptionId,
            NotificationMessage message,
            ArrayOf<uint> availableSequenceNumbers,
            ArrayOf<string> stringTable,
            bool moreNotifications)
        {
            if (!SessionOwnedSubscriptionIds.Contains(subscriptionId))
            {
                return false;
            }
            Interlocked.Increment(ref m_sessionDispatchCount);
            return true;
        }

        private int m_sessionDispatchCount;

        /// <summary>
        /// Appends a recorded call. Publish workers run on background
        /// threads while the test thread inspects the recordings, so the
        /// backing lists must never be mutated without synchronization.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="recordings"></param>
        /// <param name="call"></param>
        private void Record<T>(List<T> recordings, T call)
        {
            lock (m_recordLock)
            {
                recordings.Add(call);
            }
        }

        /// <summary>
        /// Returns a stable copy of a recording so assertions cannot
        /// observe a list that is being appended to concurrently.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="recordings"></param>
        private IReadOnlyList<T> Snapshot<T>(List<T> recordings)
        {
            lock (m_recordLock)
            {
                return [.. recordings];
            }
        }

        /// <summary>
        /// Reads the number of recorded calls without allocating a snapshot.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="recordings"></param>
        private int Count<T>(List<T> recordings)
        {
            lock (m_recordLock)
            {
                return recordings.Count;
            }
        }

        internal readonly record struct CreateSubscriptionCall(
            ISubscriptionNotificationHandler Handler,
            IOptionsMonitor<SubscriptionOptions> Options,
            IMessageAckQueue Queue,
            SubscriptionLoadState? LoadState);

        internal readonly record struct PublishCall(
            RequestHeader? RequestHeader,
            ArrayOf<SubscriptionAcknowledgement> Acknowledgements);

        internal readonly record struct TransferCall(
            RequestHeader? RequestHeader, ArrayOf<uint> SubscriptionIds,
            bool SendInitialValues);

        internal readonly record struct DeleteCall(
            RequestHeader? RequestHeader, ArrayOf<uint> SubscriptionIds);

        private readonly List<CreateSubscriptionCall> m_createSubscriptionCalls = [];
        private readonly List<PublishCall> m_publishCalls = [];
        private readonly List<TransferCall> m_transferCalls = [];
        private readonly List<DeleteCall> m_deleteCalls = [];
        private readonly System.Threading.Lock m_recordLock = new();
    }
}
