// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies bounded retransmission eviction and ordered delivery when an enqueue partially overflows the queue.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class SentMessageQueueEvictionTests
    {
        /// <summary>
        /// Verifies that partial overflow evicts only the excess oldest message and persists the exact queue delta.
        /// </summary>
        [TestCase(10u, 5, 6)]
        [TestCase(5u, 4, 2)]
        public void EnqueuePartialOverflowEvictsOnlyExcess(uint capacity, int oldCount, int newCount)
        {
            var store = new Mock<ISubscriptionRetransmissionDeltaStore>();
            List<NotificationMessage> oldMessages = CreateMessages(1, oldCount);
            List<NotificationMessage> newMessages = CreateMessages(oldCount + 1, newCount);
            var queue = SentMessageQueue.CreateRestored(
                () => 9,
                capacity,
                store.Object,
                NullLogger.Instance,
                oldMessages,
                nextSequenceNumber: 42,
                lastSentMessage: oldCount);
            var available = new List<uint>();

            NotificationMessage published = queue.Enqueue(
                newMessages,
                available,
                out bool moreNotifications,
                out uint newlyUnacknowledgedCount);

            Assert.Multiple(() =>
            {
                Assert.That(published.SequenceNumber, Is.EqualTo(oldCount + 1));
                Assert.That(queue.SentCount, Is.EqualTo(capacity));
                // only the sent messages are retransmittable; the rest of the new batch is
                // still queued for Publish responses.
                Assert.That(
                    queue.AvailableSequenceNumbersForRetransmission(),
                    Is.EqualTo(Enumerable.Range(2, oldCount).Select(i => (uint)i)));
                Assert.That(available, Is.EqualTo(Enumerable.Range(2, oldCount).Select(i => (uint)i)));
                Assert.That(newlyUnacknowledgedCount, Is.EqualTo(1u));
                Assert.That(moreNotifications, Is.True);
                Assert.That(queue.FindForRepublish(1), Is.Null);
                Assert.That(queue.FindForRepublish(2)?.SequenceNumber, Is.EqualTo(2u));
            });
            store.Verify(s => s.StoreRetransmissionStateDelta(
                9,
                42,
                It.Is<ArrayOf<NotificationMessage>>(m =>
                    m.Count == newCount && m[0].SequenceNumber == oldCount + 1),
                It.Is<ArrayOf<uint>>(r => r.Count == 1 && r[0] == 1)),
                Times.Once);

            for (int sequenceNumber = oldCount + 2; sequenceNumber <= oldCount + newCount; sequenceNumber++)
            {
                available.Clear();
                NotificationMessage? next = queue.TryDequeueQueued(available, false, out moreNotifications);
                Assert.That(next?.SequenceNumber, Is.EqualTo(sequenceNumber));
                Assert.That(moreNotifications, Is.EqualTo(sequenceNumber < oldCount + newCount));
            }
            Assert.That(queue.TryDequeueQueued(available, false, out _), Is.Null);
        }

        /// <summary>
        /// Creates notifications with consecutive sequence numbers for eviction and dequeue-order assertions.
        /// </summary>
        private static List<NotificationMessage> CreateMessages(int first, int count)
        {
            return Enumerable.Range(first, count)
                .Select(i => new NotificationMessage { SequenceNumber = (uint)i, NotificationData = [] })
                .ToList();
        }
    }
}
