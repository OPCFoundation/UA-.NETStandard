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

#pragma warning disable CA2007

using System.Collections.Generic;
using System.Linq;
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
        /// <summary>
        /// Verifies that restoration preserves sequence state and dequeues an independent copy of the queued message.
        /// </summary>
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
                Assert.That(result, Is.Not.SameAs(messages[1]));
                Assert.That(result!.IsEqual(messages[1]), Is.True);
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
                // the trimmed unsent message counts as discarded before acknowledgement.
                Assert.That(newlyUnacknowledgedCount, Is.EqualTo(1u));
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
        public void EnqueueWithZeroCapacityStillReturnsNotification()
        {
            var queue = new SentMessageQueue(
                () => 14,
                maxMessageCount: 0,
                retransmissionStore: null,
                Mock.Of<ILogger>());
            var availableSequenceNumbers = new List<uint>();

            NotificationMessage published = queue.Enqueue(
                [CreateMessage(1)],
                availableSequenceNumbers,
                out bool moreNotifications,
                out uint newlyUnacknowledgedCount);

            Assert.Multiple(() =>
            {
                Assert.That(published.SequenceNumber, Is.EqualTo(1u));
                Assert.That(queue.SentCount, Is.EqualTo(1));
                Assert.That(moreNotifications, Is.False);
                Assert.That(newlyUnacknowledgedCount, Is.Zero);
                Assert.That(availableSequenceNumbers, Has.Count.EqualTo(1));
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

        [Test]
        public void CreateRestoredWithNullEntryKeepsUnsentMessageQueued()
        {
            var queue = SentMessageQueue.CreateRestored(
                () => 15,
                maxMessageCount: 5,
                null,
                Mock.Of<ILogger>(),
                [CreateMessage(7), null!, CreateMessage(8)],
                nextSequenceNumber: 9,
                lastSentMessage: 2);

            // 7 and the null entry were in front of the cursor; 8 was not sent yet.
            NotificationMessage? next = queue.TryDequeueQueued([], hasItemsToPublish: false, out _);

            Assert.Multiple(() =>
            {
                Assert.That(next!.SequenceNumber, Is.EqualTo(8u));
                Assert.That(queue.FindForRepublish(7), Is.Not.Null);
            });
        }

        [TestCase(13u, 11u, new uint[] { 10, 12 }, 12u)]
        [TestCase(3u, uint.MaxValue, new uint[] { uint.MaxValue - 1, 1, 2 }, 1u)]
        public async Task LoadRetransmissionStateAsyncFindsBoundaryWhenFirstUnsentMessageIsMissingAsync(
            uint nextSequenceNumber,
            uint firstUnsentSequenceNumber,
            uint[] retained,
            uint expectedFirstUnsent)
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = nextSequenceNumber,
                SentMessages = [.. retained.Select(CreateMessage)],
                FirstUnsentSequenceNumber = firstUnsentSequenceNumber
            };
            var store = new Mock<ISubscriptionRetransmissionSendStateStore>();
            store.Setup(s => s.LoadRetransmissionStateAsync(21, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));
            var queue = new SentMessageQueue(
                () => 21,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            NotificationMessage? first = queue.TryDequeueQueued([], hasItemsToPublish: false, out _);
            Assert.That(first!.SequenceNumber, Is.EqualTo(expectedFirstUnsent));
        }

        [Test]
        public void TryAcknowledgeRejectsMessageNotSentYet()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>();
            var queue = SentMessageQueue.CreateRestored(
                () => 14,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>(),
                [CreateMessage(31), CreateMessage(32)],
                nextSequenceNumber: 33,
                lastSentMessage: 1);

            // 32 is still queued for Publish: the client cannot know it, so acknowledging it
            // fails (Bad_SequenceNumberUnknown) and it is still delivered.
            bool acknowledged = queue.TryAcknowledge(32);
            NotificationMessage? next = queue.TryDequeueQueued([], hasItemsToPublish: false, out _);

            Assert.Multiple(() =>
            {
                Assert.That(acknowledged, Is.False);
                Assert.That(next!.SequenceNumber, Is.EqualTo(32u));
            });
            store.Verify(s => s.AcknowledgeNotification(14, 32), Times.Never);
        }

        [Test]
        public void CreateRestoredWithMissingStateStartsEmptyAtSequenceNumberOne()
        {
            var queue = SentMessageQueue.CreateRestored(
                () => 15,
                maxMessageCount: 5,
                retransmissionStore: null,
                Mock.Of<ILogger>(),
                sentMessages: null,
                nextSequenceNumber: 0,
                lastSentMessage: -3);

            Assert.Multiple(() =>
            {
                Assert.That(queue.SentCount, Is.Zero);
                Assert.That(queue.LastSentMessage, Is.Zero);
                // OPC 10000-4, 5.14.1.1: sequence number 0 is never used.
                Assert.That(queue.NextSequenceNumber, Is.EqualTo(1u));
                Assert.That(queue.AssignSequenceNumber(), Is.EqualTo(1u));
            });
        }

        [Test]
        public void CreateRestoredClampsSentIndexAndContinuesAfterNewestMessage()
        {
            var tooLarge = SentMessageQueue.CreateRestored(
                () => 16,
                maxMessageCount: 5,
                retransmissionStore: null,
                Mock.Of<ILogger>(),
                [CreateMessage(5), CreateMessage(6)],
                nextSequenceNumber: 0,
                lastSentMessage: 10);
            var negative = SentMessageQueue.CreateRestored(
                () => 17,
                maxMessageCount: 5,
                retransmissionStore: null,
                Mock.Of<ILogger>(),
                [CreateMessage(5), CreateMessage(6)],
                nextSequenceNumber: 7,
                lastSentMessage: -1);

            NotificationMessage? queued = negative.TryDequeueQueued(
                [],
                hasItemsToPublish: false,
                out bool moreNotifications);

            Assert.Multiple(() =>
            {
                Assert.That(tooLarge.LastSentMessage, Is.EqualTo(2));
                Assert.That(tooLarge.NextSequenceNumber, Is.EqualTo(7u));
                Assert.That(queued!.SequenceNumber, Is.EqualTo(5u));
                Assert.That(moreNotifications, Is.True);
            });
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncMapsSequenceNumberZeroAsync()
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = 0,
                SentMessages = [CreateMessage(20), CreateMessage(21)]
            };
            var store = new Mock<ISubscriptionRetransmissionStore>();
            store.Setup(s => s.LoadRetransmissionStateAsync(18, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));
            var queue = new SentMessageQueue(
                () => 18,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(queue.NextSequenceNumber, Is.EqualTo(22u));
        }

        [Test]
        public void SendStateStoreMirrorsFirstUnsentSequenceNumber()
        {
            var store = new Mock<ISubscriptionRetransmissionSendStateStore>();
            var queue = new SentMessageQueue(
                () => 19,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            queue.Enqueue(
                [CreateMessage(10), CreateMessage(11), CreateMessage(12)],
                [],
                out bool moreNotifications,
                out _);
            Assert.That(moreNotifications, Is.True);
            store.Verify(s => s.StoreFirstUnsentSequenceNumber(19, It.IsAny<uint>(), 11), Times.Once);

            queue.TryDequeueQueued([], hasItemsToPublish: false, out _);
            store.Verify(s => s.StoreFirstUnsentSequenceNumber(19, It.IsAny<uint>(), 12), Times.Once);

            queue.TryDequeueQueued([], hasItemsToPublish: false, out _);
            store.Verify(s => s.StoreFirstUnsentSequenceNumber(19, It.IsAny<uint>(), 0), Times.Once);

            // Nothing changes: no further mirror writes.
            queue.TryAcknowledge(10);
            queue.Enqueue([CreateMessage(13)], [], out _, out _);
            store.Verify(
                s => s.StoreFirstUnsentSequenceNumber(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<uint>()),
                Times.Exactly(3));
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncKeepsUnsentMessagesQueuedForPublishAsync()
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = 13,
                SentMessages = [CreateMessage(10), CreateMessage(11), CreateMessage(12)],
                FirstUnsentSequenceNumber = 11
            };
            var store = new Mock<ISubscriptionRetransmissionSendStateStore>();
            store.Setup(s => s.LoadRetransmissionStateAsync(20, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));
            var queue = new SentMessageQueue(
                () => 20,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            // OPC 10000-4, 5.14.1.1: the backlog is delivered by Publish, not only by Republish.
            NotificationMessage? first = queue.TryDequeueQueued([], hasItemsToPublish: false, out bool more);
            NotificationMessage? second = queue.TryDequeueQueued([], hasItemsToPublish: false, out _);
            NotificationMessage? none = queue.TryDequeueQueued([], hasItemsToPublish: false, out _);

            Assert.Multiple(() =>
            {
                Assert.That(first!.SequenceNumber, Is.EqualTo(11u));
                Assert.That(more, Is.True);
                Assert.That(second!.SequenceNumber, Is.EqualTo(12u));
                Assert.That(none, Is.Null);
            });
        }

        [Test]
        public async Task SendStateAfterRestoreCarriesNextSequenceNumberAsync()
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = 13,
                SentMessages = [CreateMessage(10), CreateMessage(11), CreateMessage(12)],
                FirstUnsentSequenceNumber = 11
            };
            var store = new Mock<ISubscriptionRetransmissionSendStateStore>();
            store.Setup(s => s.LoadRetransmissionStateAsync(21, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));
            var queue = new SentMessageQueue(
                () => 21,
                maxMessageCount: 5,
                store.Object,
                Mock.Of<ILogger>());

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            // The first send-state write after a restore is not preceded by a retransmission
            // state write, so it must carry the restored next sequence number itself.
            queue.TryDequeueQueued([], hasItemsToPublish: false, out _);
            store.Verify(s => s.StoreFirstUnsentSequenceNumber(21, 13, 12), Times.Once);
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
