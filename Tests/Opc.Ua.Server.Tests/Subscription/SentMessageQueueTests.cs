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
#pragma warning disable CA2007

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SentMessageQueueTests
    {
        [Test]
        public void CreateRestoredPreservesQueueStateAndDequeuesExistingMessages()
        {
            List<NotificationMessage> messages =
            [
                CreateMessage(10),
                CreateMessage(11)
            ];
            var queue = SentMessageQueue.CreateRestored(
                () => 7,
                maxMessageCount: 5,
                retransmissionStore: null,
                Mock.Of<ILogger>(),
                messages,
                nextSequenceNumber: 42,
                lastSentMessage: 1);
            var availableSequenceNumbers = new List<uint>();

            NotificationMessage? result = queue.TryDequeueQueued(
                availableSequenceNumbers,
                hasItemsToPublish: false,
                out bool moreNotifications);

            Assert.Multiple(() =>
            {
                Assert.That(queue.NextSequenceNumber, Is.EqualTo(42u));
                Assert.That(queue.LastSentMessage, Is.EqualTo(2));
                Assert.That(result, Is.SameAs(messages[1]));
                Assert.That(availableSequenceNumbers, Has.Count.EqualTo(2));
                Assert.That(moreNotifications, Is.False);
            });
        }

        [Test]
        public void EnqueueUsesDeltaStoreWhenAvailableAndReportsRemovedSequenceNumbers()
        {
            var deltaStore = new Mock<ISubscriptionRetransmissionDeltaStore>();
            var queue = SentMessageQueue.CreateRestored(
                () => 9,
                maxMessageCount: 2,
                deltaStore.Object,
                Mock.Of<ILogger>(),
                [CreateMessage(1), CreateMessage(2)],
                nextSequenceNumber: 3,
                lastSentMessage: 2);
            var availableSequenceNumbers = new List<uint>();

            NotificationMessage published = queue.Enqueue(
                [CreateMessage(3)],
                availableSequenceNumbers,
                out bool moreNotifications,
                out uint newlyUnacknowledgedCount);

            Assert.Multiple(() =>
            {
                Assert.That(published.SequenceNumber, Is.EqualTo(3u));
                Assert.That(queue.SentCount, Is.EqualTo(2));
                Assert.That(newlyUnacknowledgedCount, Is.EqualTo(1u));
                Assert.That(moreNotifications, Is.False);
                Assert.That(availableSequenceNumbers, Has.Count.EqualTo(2));
            });
            deltaStore.Verify(s => s.StoreRetransmissionStateDelta(
                9,
                3,
                It.Is<ArrayOf<NotificationMessage>>(m => m.Count == 1 && m[0].SequenceNumber == 3),
                It.Is<ArrayOf<uint>>(r => r.Count == 1 && r[0] == 1)),
                Times.Once);
        }

        [Test]
        public void EnqueueTrimsOversizedBatchBeforeStoringSnapshot()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>();
            var logger = new Mock<ILogger>();
            var queue = new SentMessageQueue(
                () => 11,
                maxMessageCount: 2,
                store.Object,
                logger.Object);
            var availableSequenceNumbers = new List<uint>();

            NotificationMessage published = queue.Enqueue(
                [CreateMessage(1), CreateMessage(2), CreateMessage(3)],
                availableSequenceNumbers,
                out bool moreNotifications,
                out uint newlyUnacknowledgedCount);

            Assert.Multiple(() =>
            {
                Assert.That(published.SequenceNumber, Is.EqualTo(2u));
                Assert.That(queue.SentCount, Is.EqualTo(2));
                Assert.That(newlyUnacknowledgedCount, Is.Zero);
                Assert.That(moreNotifications, Is.True);
                Assert.That(availableSequenceNumbers, Has.Count.EqualTo(1));
            });
            store.Verify(s => s.StoreRetransmissionState(
                11,
                1,
                It.Is<ArrayOf<NotificationMessage>>(m => m.Count == 2 && m[0].SequenceNumber == 2)),
                Times.Once);
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncRestoresStateFromStoreAsync()
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = 100,
                SentMessages = [CreateMessage(20), CreateMessage(21)]
            };
            var store = new Mock<ISubscriptionRetransmissionStore>();
            store.Setup(s => s.LoadRetransmissionStateAsync(12, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));
            var queue = new SentMessageQueue(
                () => 12,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(queue.NextSequenceNumber, Is.EqualTo(100u));
                Assert.That(queue.LastSentMessage, Is.EqualTo(2));
                Assert.That(queue.AvailableSequenceNumbersForRetransmission(), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void TryAcknowledgeRemovesMessageAndMirrorsAcknowledgement()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>();
            var queue = SentMessageQueue.CreateRestored(
                () => 13,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>(),
                [CreateMessage(31), CreateMessage(32)],
                nextSequenceNumber: 33,
                lastSentMessage: 2);

            bool removed = queue.TryAcknowledge(31);
            bool missing = queue.TryAcknowledge(99);

            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.True);
                Assert.That(missing, Is.False);
                Assert.That(queue.SentCount, Is.EqualTo(1));
            });
            store.Verify(s => s.AcknowledgeNotification(13, 31), Times.Once);
        }

        private static NotificationMessage CreateMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData = []
            };
        }
    }
}
