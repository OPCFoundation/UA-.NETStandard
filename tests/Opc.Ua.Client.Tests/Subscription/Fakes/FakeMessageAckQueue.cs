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

namespace Opc.Ua.Client.Subscriptions.Fakes
{
    /// <summary>
    /// Hand-rolled fake for <see cref="IMessageAckQueue"/>. Records every
    /// invocation and lets tests override return behaviour via callback
    /// fields. Replaces <c>Mock&lt;IMessageAckQueue&gt;</c>.
    /// </summary>
    internal sealed class FakeMessageAckQueue : IMessageAckQueue
    {
        public List<SubscriptionAcknowledgement> QueuedAcks { get; } = [];
        public List<uint> CompletedSubscriptions { get; } = [];

        /// <summary>
        /// Instances passed to <see cref="CompleteAsync"/>, so tests can
        /// assert the subscription is retired by identity and not only by
        /// the server assigned id.
        /// </summary>
        public List<IMessageProcessor> CompletedProcessors { get; } = [];
        public int UpdateCalls { get; private set; }
        public int PublishingQuiescenceCalls { get; private set; }

        /// <summary>
        /// Optional override for <see cref="QueueAsync"/>. If null, returns
        /// completed.
        /// </summary>
        public Func<SubscriptionAcknowledgement, CancellationToken, ValueTask>? OnQueueAsync { get; set; }

        /// <summary>
        /// Optional override for <see cref="CompleteAsync"/>. If null,
        /// returns completed.
        /// </summary>
        public Func<uint, CancellationToken, ValueTask>? OnCompleteAsync { get; set; }

        public ValueTask QueueAsync(SubscriptionAcknowledgement ack,
            CancellationToken ct = default)
        {
            lock (m_lock)
            {
                QueuedAcks.Add(ack);
            }
            return OnQueueAsync?.Invoke(ack, ct) ?? default;
        }

        /// <summary>
        /// Waits until at least <paramref name="count"/> acknowledgements have
        /// been queued. The processor under test dispatches a notification
        /// before it queues the acknowledgement for it, so a test that only
        /// waits for the dispatch races the enqueue that follows it.
        /// </summary>
        /// <param name="count">
        /// The number of queued acknowledgements to wait for.
        /// </param>
        /// <param name="timeoutMs">How long to wait before giving up.</param>
        /// <exception cref="TimeoutException">
        /// The acknowledgements did not arrive in time. Failing here rather
        /// than returning quietly keeps the diagnosis at the right level: a
        /// caller that went on to assert the count would only report the
        /// mismatch, which cannot distinguish an acknowledgement that was
        /// never queued from one that merely arrived late - and if that
        /// assertion is ever loosened, a silent return would hide the race
        /// this helper exists to close.
        /// </exception>
        public async Task WaitForQueuedAckAsync(int count, int timeoutMs = 5000)
        {
            const int kPollIntervalMs = 10;
            TimeSpan timeout = TimeSpan.FromMilliseconds(timeoutMs);
            long start = TimeProvider.System.GetTimestamp();
            while (true)
            {
                int queued;
                lock (m_lock)
                {
                    queued = QueuedAcks.Count;
                }
                if (queued >= count)
                {
                    return;
                }
                // Measure against a monotonic clock rather than accumulating
                // the poll interval: Task.Delay routinely overshoots, so
                // counting the requested interval silently stretches the
                // effective timeout well past what the caller asked for.
                if (TimeProvider.System.GetElapsedTime(start) >= timeout)
                {
                    throw new TimeoutException(
                        $"Expected at least {count} queued acknowledgement(s) within " +
                        $"{timeout.TotalSeconds:0.##}s but only {queued} arrived.");
                }
                await Task.Delay(kPollIntervalMs).ConfigureAwait(false);
            }
        }

        public ValueTask CompleteAsync(IMessageProcessor subscription,
            uint subscriptionId,
            CancellationToken ct = default)
        {
            CompletedProcessors.Add(subscription);
            CompletedSubscriptions.Add(subscriptionId);
            return OnCompleteAsync?.Invoke(subscriptionId, ct) ?? default;
        }

        public ValueTask RunWithPublishingQuiescedAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct = default)
        {
            PublishingQuiescenceCalls++;
            return operation(ct);
        }

        /// <summary>
        /// Records the subscription ids dropped via
        /// <see cref="DropPendingForSubscription"/> so tests can assert
        /// stale-ack pruning happened during recovery.
        /// </summary>
        public List<uint> DroppedSubscriptions { get; } = [];

        public int DropPendingForSubscription(uint subscriptionId)
        {
            DroppedSubscriptions.Add(subscriptionId);
            lock (m_lock)
            {
                return QueuedAcks.RemoveAll(
                    ack => ack.SubscriptionId == subscriptionId);
            }
        }

        /// <summary>
        /// Test-controlled answer for
        /// <see cref="IMessageAckQueue.OwnsSubscriptionId"/>. Defaults to
        /// <c>true</c>, i.e. the caller still owns the id.
        /// </summary>
        public Func<IMessageProcessor, uint, bool>? OnOwnsSubscriptionId { get; set; }

        /// <summary>
        /// Ids <see cref="OwnsSubscriptionId"/> was asked about.
        /// </summary>
        public List<uint> OwnershipChecks { get; } = [];

        public bool OwnsSubscriptionId(IMessageProcessor subscription, uint subscriptionId)
        {
            OwnershipChecks.Add(subscriptionId);
            return OnOwnsSubscriptionId?.Invoke(subscription, subscriptionId) ?? true;
        }

        public void Update()
        {
            UpdateCalls++;
        }

        /// <summary>
        /// Test-controlled value for the
        /// <see cref="IMessageAckQueue.PoolNotifications"/> setter the
        /// subscription dispatcher reads to decide whether to perform the
        /// pooled-notification reuse walk.
        /// </summary>
        public bool PoolNotifications { get; set; }

        private readonly Lock m_lock = new();
    }
}
