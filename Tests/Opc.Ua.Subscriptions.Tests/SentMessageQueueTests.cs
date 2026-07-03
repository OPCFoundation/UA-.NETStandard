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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;

#nullable enable

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="SentMessageQueue"/> — the subscription-owned
    /// sent-notification ring that backs Publish, Acknowledge and Republish. The queue is
    /// driven directly (no live server) to exercise sequence assignment, overflow trimming,
    /// eviction, acknowledgement, republish lookup and the optional retransmission mirror.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    public sealed class SentMessageQueueTests
    {
        [Test]
        public void ConstructorThrowsWhenSubscriptionIdProviderNull()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SentMessageQueue(null!, 10, null, Mock.Of<ILogger>()))!;
            Assert.That(ex.ParamName, Is.EqualTo("subscriptionIdProvider"));
        }

        [Test]
        public void ConstructorThrowsWhenLoggerNull()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new SentMessageQueue(() => 1, 10, null, null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("logger"));
        }

        [Test]
        public void NewQueueStartsEmptyWithSequenceNumberOne()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 7);

            Assert.That(queue.NextSequenceNumber, Is.EqualTo(1u));
            Assert.That(queue.MaxMessageCount, Is.EqualTo(7u));
            Assert.That(queue.SentCount, Is.Zero);
            Assert.That(queue.LastSentMessage, Is.Zero);
            Assert.That(queue.SentMessages, Is.Empty);
        }

        [Test]
        public void AssignSequenceNumberReturnsCurrentAndAdvances()
        {
            SentMessageQueue queue = NewQueue();

            Assert.That(queue.AssignSequenceNumber(), Is.EqualTo(1u));
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(2u));
            Assert.That(queue.AssignSequenceNumber(), Is.EqualTo(2u));
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(3u));
        }

        [Test]
        public void AssignSequenceNumberWrapsPastMaxValueSkippingZero()
        {
            SentMessageQueue queue = NewRestoredQueue(
                [], nextSequenceNumber: uint.MaxValue, lastSentMessage: 0);

            Assert.That(queue.AssignSequenceNumber(), Is.EqualTo(uint.MaxValue));
            // Zero is reserved, so the counter wraps straight to 1.
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(1u));
        }

        [Test]
        public void CreateRestoredPreservesProvidedState()
        {
            List<NotificationMessage> messages = Messages(10, 11, 12);

            SentMessageQueue queue = NewRestoredQueue(
                messages, nextSequenceNumber: 13, lastSentMessage: 2);

            Assert.That(queue.NextSequenceNumber, Is.EqualTo(13u));
            Assert.That(queue.LastSentMessage, Is.EqualTo(2));
            Assert.That(queue.SentCount, Is.EqualTo(3));
            Assert.That(queue.SentMessages, Is.SameAs(messages));
        }

        [Test]
        public void TryDequeueQueuedReturnsQueuedMessageAndMore()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2, 3), nextSequenceNumber: 4, lastSentMessage: 0);
            var available = new List<uint>();

            NotificationMessage? message = queue.TryDequeueQueued(
                available, hasItemsToPublish: false, out bool moreNotifications);

            Assert.That(message, Is.Not.Null);
            Assert.That(message!.SequenceNumber, Is.EqualTo(1u));
            Assert.That(moreNotifications, Is.True);
            Assert.That(available, Is.EqualTo(new List<uint> { 1 }));
            Assert.That(queue.LastSentMessage, Is.EqualTo(1));
        }

        [Test]
        public void TryDequeueQueuedSignalsMoreWhenItemsPending()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(5), nextSequenceNumber: 6, lastSentMessage: 0);
            var available = new List<uint>();

            NotificationMessage? message = queue.TryDequeueQueued(
                available, hasItemsToPublish: true, out bool moreNotifications);

            Assert.That(message, Is.Not.Null);
            Assert.That(moreNotifications, Is.True);
        }

        [Test]
        public void TryDequeueQueuedLastMessageWithoutPendingReportsNoMore()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2), nextSequenceNumber: 3, lastSentMessage: 1);
            var available = new List<uint>();

            NotificationMessage? message = queue.TryDequeueQueued(
                available, hasItemsToPublish: false, out bool moreNotifications);

            Assert.That(message, Is.Not.Null);
            Assert.That(message!.SequenceNumber, Is.EqualTo(2u));
            Assert.That(moreNotifications, Is.False);
            Assert.That(available, Is.EqualTo(new List<uint> { 1, 2 }));
        }

        [Test]
        public void TryDequeueQueuedReturnsNullWhenNothingQueued()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2), nextSequenceNumber: 3, lastSentMessage: 2);
            var available = new List<uint>();

            NotificationMessage? message = queue.TryDequeueQueued(
                available, hasItemsToPublish: true, out bool moreNotifications);

            Assert.That(message, Is.Null);
            Assert.That(moreNotifications, Is.False);
            Assert.That(available, Is.Empty);
        }

        [Test]
        public void FillAvailableSequenceNumbersReturnsUpToCursor()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(7, 8, 9), nextSequenceNumber: 10, lastSentMessage: 1);
            var available = new List<uint>();

            queue.FillAvailableSequenceNumbers(available);

            Assert.That(available, Is.EqualTo(new List<uint> { 7, 8 }));
        }

        [Test]
        public void EnqueueAddsMessagesToEmptyQueue()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 10);
            var available = new List<uint>();

            NotificationMessage returned = queue.Enqueue(
                Messages(1), available, out bool moreNotifications, out uint newlyUnacknowledged);

            Assert.That(returned.SequenceNumber, Is.EqualTo(1u));
            Assert.That(moreNotifications, Is.False);
            Assert.That(newlyUnacknowledged, Is.Zero);
            Assert.That(queue.SentCount, Is.EqualTo(1));
            Assert.That(queue.LastSentMessage, Is.EqualTo(1));
            Assert.That(available, Is.EqualTo(new List<uint> { 1 }));
        }

        [Test]
        public void EnqueueMultipleReportsMoreNotifications()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 10);
            var available = new List<uint>();

            NotificationMessage returned = queue.Enqueue(
                Messages(1, 2, 3), available, out bool moreNotifications, out uint newlyUnacknowledged);

            Assert.That(returned.SequenceNumber, Is.EqualTo(1u));
            Assert.That(moreNotifications, Is.True);
            Assert.That(newlyUnacknowledged, Is.Zero);
            Assert.That(queue.SentCount, Is.EqualTo(3));
        }

        [Test]
        public void EnqueueDropsOverflowMessagesBeyondCapacity()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 3);
            var available = new List<uint>();

            NotificationMessage returned = queue.Enqueue(
                Messages(1, 2, 3, 4, 5), available, out bool moreNotifications, out uint newlyUnacknowledged);

            // The two oldest messages are dropped to respect the capacity of three.
            Assert.That(queue.SentCount, Is.EqualTo(3));
            Assert.That(returned.SequenceNumber, Is.EqualTo(3u));
            Assert.That(moreNotifications, Is.True);
            Assert.That(newlyUnacknowledged, Is.Zero);
            Assert.That(queue.FindForRepublish(1), Is.Null);
            Assert.That(queue.FindForRepublish(2), Is.Null);
            Assert.That(queue.FindForRepublish(3), Is.Not.Null);
        }

        [Test]
        public void EnqueueEvictsEntireQueueWhenNewBatchFillsCapacity()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 2);
            queue.Enqueue(Messages(1, 2), [], out _, out _);

            var available = new List<uint>();
            NotificationMessage returned = queue.Enqueue(
                Messages(3, 4), available, out bool moreNotifications, out uint newlyUnacknowledged);

            Assert.That(newlyUnacknowledged, Is.EqualTo(2u));
            Assert.That(moreNotifications, Is.True);
            Assert.That(queue.SentCount, Is.EqualTo(2));
            Assert.That(returned.SequenceNumber, Is.EqualTo(3u));
            Assert.That(queue.FindForRepublish(1), Is.Null);
            Assert.That(queue.FindForRepublish(2), Is.Null);
            Assert.That(queue.FindForRepublish(3), Is.Not.Null);
            Assert.That(queue.FindForRepublish(4), Is.Not.Null);
        }

        [Test]
        public void EnqueuePartiallyEvictsOldestWhenQueueFull()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 5);
            queue.Enqueue(Messages(1, 2, 3, 4), [], out _, out _);

            var available = new List<uint>();
            NotificationMessage returned = queue.Enqueue(
                Messages(5, 6), available, out bool moreNotifications, out uint newlyUnacknowledged);

            Assert.That(newlyUnacknowledged, Is.EqualTo(2u));
            Assert.That(moreNotifications, Is.True);
            Assert.That(queue.SentCount, Is.EqualTo(4));
            Assert.That(returned.SequenceNumber, Is.EqualTo(5u));
            Assert.That(queue.FindForRepublish(1), Is.Null);
            Assert.That(queue.FindForRepublish(2), Is.Null);
            Assert.That(queue.FindForRepublish(3), Is.Not.Null);
            Assert.That(queue.FindForRepublish(6), Is.Not.Null);
        }

        [Test]
        public void TryAcknowledgeRemovesMessageAndReturnsTrue()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 10);
            queue.Enqueue(Messages(1, 2, 3), [], out _, out _);

            bool acknowledged = queue.TryAcknowledge(2);

            Assert.That(acknowledged, Is.True);
            Assert.That(queue.SentCount, Is.EqualTo(2));
            Assert.That(queue.FindForRepublish(2), Is.Null);
        }

        [Test]
        public void TryAcknowledgeDecrementsCursorWhenBeforeIt()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2, 3), nextSequenceNumber: 4, lastSentMessage: 2);

            bool acknowledged = queue.TryAcknowledge(1);

            Assert.That(acknowledged, Is.True);
            Assert.That(queue.LastSentMessage, Is.EqualTo(1));
            Assert.That(queue.SentCount, Is.EqualTo(2));
        }

        [Test]
        public void TryAcknowledgeKeepsCursorWhenAtOrAfterIt()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2, 3), nextSequenceNumber: 4, lastSentMessage: 1);

            bool acknowledged = queue.TryAcknowledge(3);

            Assert.That(acknowledged, Is.True);
            Assert.That(queue.LastSentMessage, Is.EqualTo(1));
            Assert.That(queue.SentCount, Is.EqualTo(2));
        }

        [Test]
        public void TryAcknowledgeReturnsFalseWhenNotFound()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 10);
            queue.Enqueue(Messages(1, 2), [], out _, out _);

            bool acknowledged = queue.TryAcknowledge(99);

            Assert.That(acknowledged, Is.False);
            Assert.That(queue.SentCount, Is.EqualTo(2));
        }

        [Test]
        public void FindForRepublishReturnsMatchingMessage()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2, 3), nextSequenceNumber: 4, lastSentMessage: 3);

            NotificationMessage? found = queue.FindForRepublish(2);

            Assert.That(found, Is.Not.Null);
            Assert.That(found!.SequenceNumber, Is.EqualTo(2u));
        }

        [Test]
        public void FindForRepublishReturnsNullWhenMissing()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2), nextSequenceNumber: 3, lastSentMessage: 2);

            Assert.That(queue.FindForRepublish(42), Is.Null);
        }

        [Test]
        public void AvailableSequenceNumbersForRetransmissionReturnsAll()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(4, 5, 6), nextSequenceNumber: 7, lastSentMessage: 1);

            ArrayOf<uint> result = queue.AvailableSequenceNumbersForRetransmission();

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(4u));
            Assert.That(result[1], Is.EqualTo(5u));
            Assert.That(result[2], Is.EqualTo(6u));
        }

        [Test]
        public void ClearRemovesAllSentMessages()
        {
            SentMessageQueue queue = NewQueue(maxMessageCount: 10);
            var messages = new List<NotificationMessage>
            {
                DataChangeMessage(1),
                EventMessage(2)
            };
            queue.Enqueue(messages, [], out _, out _);

            queue.Clear();

            Assert.That(queue.SentCount, Is.Zero);
            Assert.That(queue.SentMessages, Is.Empty);
        }

        [Test]
        public void EnqueueStoresRetransmissionStateOnNonDeltaStore()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>(MockBehavior.Loose);
            SentMessageQueue queue = NewQueue(maxMessageCount: 10, store: store.Object, subscriptionId: 42);

            queue.Enqueue(Messages(1), [], out _, out _);

            store.Verify(
                s => s.StoreRetransmissionState(
                    42u, It.IsAny<uint>(), It.IsAny<ArrayOf<NotificationMessage>>()),
                Times.Once);
        }

        [Test]
        public void EnqueueStoresDeltaWhenStoreSupportsDelta()
        {
            var store = new Mock<ISubscriptionRetransmissionDeltaStore>(MockBehavior.Loose);
            int capturedAdded = -1;
            ArrayOf<uint> capturedRemoved = default;
            store
                .Setup(s => s.StoreRetransmissionStateDelta(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<NotificationMessage>>(),
                    It.IsAny<ArrayOf<uint>>()))
                .Callback<uint, uint, ArrayOf<NotificationMessage>, ArrayOf<uint>>(
                    (id, seq, added, removed) =>
                    {
                        capturedAdded = added.Count;
                        capturedRemoved = removed;
                    });

            SentMessageQueue queue = NewQueue(maxMessageCount: 10, store: store.Object, subscriptionId: 42);
            queue.Enqueue(Messages(1, 2), [], out _, out _);

            store.Verify(
                s => s.StoreRetransmissionStateDelta(
                    42u,
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<NotificationMessage>>(),
                    It.IsAny<ArrayOf<uint>>()),
                Times.Once);
            Assert.That(capturedAdded, Is.EqualTo(2));
            Assert.That(capturedRemoved.Count, Is.Zero);
        }

        [Test]
        public void EnqueueDeltaReportsRemovedSequenceNumbersOnFullEviction()
        {
            var store = new Mock<ISubscriptionRetransmissionDeltaStore>(MockBehavior.Loose);
            var capturedRemoved = new List<uint>();
            store
                .Setup(s => s.StoreRetransmissionStateDelta(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<NotificationMessage>>(),
                    It.IsAny<ArrayOf<uint>>()))
                .Callback<uint, uint, ArrayOf<NotificationMessage>, ArrayOf<uint>>(
                    (id, seq, added, removed) => capturedRemoved = ToList(removed));

            SentMessageQueue queue = NewQueue(maxMessageCount: 2, store: store.Object);
            queue.Enqueue(Messages(1, 2), [], out _, out _);
            capturedRemoved.Clear();

            queue.Enqueue(Messages(3, 4), [], out _, out uint newlyUnacknowledged);

            Assert.That(newlyUnacknowledged, Is.EqualTo(2u));
            Assert.That(capturedRemoved, Is.EqualTo(new List<uint> { 1, 2 }));
        }

        [Test]
        public void EnqueueDeltaReportsRemovedSequenceNumbersOnPartialEviction()
        {
            var store = new Mock<ISubscriptionRetransmissionDeltaStore>(MockBehavior.Loose);
            var capturedRemoved = new List<uint>();
            store
                .Setup(s => s.StoreRetransmissionStateDelta(
                    It.IsAny<uint>(),
                    It.IsAny<uint>(),
                    It.IsAny<ArrayOf<NotificationMessage>>(),
                    It.IsAny<ArrayOf<uint>>()))
                .Callback<uint, uint, ArrayOf<NotificationMessage>, ArrayOf<uint>>(
                    (id, seq, added, removed) => capturedRemoved = ToList(removed));

            SentMessageQueue queue = NewQueue(maxMessageCount: 5, store: store.Object);
            queue.Enqueue(Messages(1, 2, 3, 4), [], out _, out _);
            capturedRemoved.Clear();

            queue.Enqueue(Messages(5, 6), [], out _, out uint newlyUnacknowledged);

            Assert.That(newlyUnacknowledged, Is.EqualTo(2u));
            Assert.That(capturedRemoved, Is.EqualTo(new List<uint> { 1, 2 }));
        }

        [Test]
        public void TryAcknowledgeNotifiesStore()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>(MockBehavior.Loose);
            SentMessageQueue queue = NewQueue(maxMessageCount: 10, store: store.Object, subscriptionId: 7);
            queue.Enqueue(Messages(1, 2), [], out _, out _);

            bool acknowledged = queue.TryAcknowledge(1);

            Assert.That(acknowledged, Is.True);
            store.Verify(s => s.AcknowledgeNotification(7u, 1u), Times.Once);
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncReturnsEarlyWithoutStoreAsync()
        {
            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2), nextSequenceNumber: 3, lastSentMessage: 2);

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(queue.SentCount, Is.EqualTo(2));
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(3u));
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncIgnoresNullStateAsync()
        {
            var store = new Mock<ISubscriptionRetransmissionStore>(MockBehavior.Loose);
            store
                .Setup(s => s.LoadRetransmissionStateAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>((SubscriptionRetransmissionState?)null));

            SentMessageQueue queue = NewRestoredQueue(
                Messages(1, 2), nextSequenceNumber: 3, lastSentMessage: 2, store: store.Object);

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(queue.SentCount, Is.EqualTo(2));
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(3u));
        }

        [Test]
        public async Task LoadRetransmissionStateAsyncRestoresStateFromStoreAsync()
        {
            var state = new SubscriptionRetransmissionState
            {
                NextSequenceNumber = 7,
                SentMessages = [DataChangeMessage(4), DataChangeMessage(5)]
            };
            var store = new Mock<ISubscriptionRetransmissionStore>(MockBehavior.Loose);
            store
                .Setup(s => s.LoadRetransmissionStateAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<SubscriptionRetransmissionState?>(state));

            SentMessageQueue queue = NewQueue(maxMessageCount: 10, store: store.Object);

            await queue.LoadRetransmissionStateAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(queue.SentCount, Is.EqualTo(2));
            Assert.That(queue.NextSequenceNumber, Is.EqualTo(7u));
            Assert.That(queue.LastSentMessage, Is.EqualTo(2));
            Assert.That(queue.FindForRepublish(4), Is.Not.Null);
        }

        private static SentMessageQueue NewQueue(
            uint maxMessageCount = 10,
            ISubscriptionRetransmissionStore? store = null,
            uint subscriptionId = 42)
        {
            return new SentMessageQueue(() => subscriptionId, maxMessageCount, store, Mock.Of<ILogger>());
        }

        private static SentMessageQueue NewRestoredQueue(
            List<NotificationMessage> sentMessages,
            uint nextSequenceNumber,
            int lastSentMessage,
            uint maxMessageCount = 10,
            ISubscriptionRetransmissionStore? store = null,
            uint subscriptionId = 42)
        {
            return SentMessageQueue.CreateRestored(
                () => subscriptionId,
                maxMessageCount,
                store,
                Mock.Of<ILogger>(),
                sentMessages,
                nextSequenceNumber,
                lastSentMessage);
        }

        private static List<NotificationMessage> Messages(params uint[] sequenceNumbers)
        {
            var list = new List<NotificationMessage>(sequenceNumbers.Length);
            foreach (uint sequenceNumber in sequenceNumbers)
            {
                list.Add(DataChangeMessage(sequenceNumber));
            }
            return list;
        }

        private static NotificationMessage DataChangeMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData =
                [
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems = [new MonitoredItemNotification()]
                    })
                ]
            };
        }

        private static NotificationMessage EventMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData =
                [
                    new ExtensionObject(new EventNotificationList
                    {
                        Events = [new EventFieldList()]
                    })
                ]
            };
        }

        private static List<uint> ToList(ArrayOf<uint> values)
        {
            var list = new List<uint>(values.Count);
            for (int ii = 0; ii < values.Count; ii++)
            {
                list.Add(values[ii]);
            }
            return list;
        }
    }
}
