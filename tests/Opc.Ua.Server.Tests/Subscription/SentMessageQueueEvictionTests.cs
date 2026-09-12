/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class SentMessageQueueEvictionTests
    {
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
                Assert.That(
                    queue.AvailableSequenceNumbersForRetransmission(),
                    Is.EqualTo(Enumerable.Range(2, (int)capacity).Select(i => (uint)i)));
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

        private static List<NotificationMessage> CreateMessages(int first, int count)
        {
            return Enumerable.Range(first, count)
                .Select(i => new NotificationMessage { SequenceNumber = (uint)i, NotificationData = [] })
                .ToList();
        }
    }
}
