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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that a notification message keeps its pooled payload until the retransmission
    /// queue and every Publish response that carries it have released it, and that copies
    /// handed out for republish and mirroring are independent of queue recycling.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [NonParallelizable]
    public sealed class NotificationEncodingLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies that delayed encoding preserves notifications after acknowledgement, eviction,
        /// clearing, and pool reuse, and that a message shared with its response is recycled
        /// only once the response was sent.
        /// </summary>
        [Test]
        public async Task ReturnedNotificationSurvivesPoolReuseUntilEncodingCompletesAsync(
            [Values("publish", "queued", "republish", "fullMirror", "deltaMirror")] string surface,
            [Values("acknowledge", "evict", "clear")] string mutation,
            [Values(false, true)] bool events)
        {
            NotificationMessage? mirrored = null;
            var fullStore = new Mock<ISubscriptionRetransmissionStore>();
            fullStore.Setup(store => store.StoreRetransmissionState(
                    1, It.IsAny<uint>(), It.IsAny<ArrayOf<NotificationMessage>>()))
                .Callback<uint, uint, ArrayOf<NotificationMessage>>((_, _, values) => mirrored ??= values[0]);
            var deltaStore = new Mock<ISubscriptionRetransmissionDeltaStore>();
            deltaStore.Setup(store => store.StoreRetransmissionStateDelta(
                    1, It.IsAny<uint>(), It.IsAny<ArrayOf<NotificationMessage>>(), It.IsAny<ArrayOf<uint>>()))
                .Callback<uint, uint, ArrayOf<NotificationMessage>, ArrayOf<uint>>(
                    (_, _, values, _) => mirrored ??= values[0]);
            ISubscriptionRetransmissionStore? store = surface switch
            {
                "fullMirror" => fullStore.Object,
                "deltaMirror" => deltaStore.Object,
                _ => null
            };
            var queue = new SentMessageQueue(() => 1, 2, store, NullLogger.Instance);
            var source = (NotificationMessage)NotificationMessageActivator.Instance.CreateInstance();
            source.SequenceNumber = surface == "queued" ? 2u : 1u;
            var item = (MonitoredItemNotification)MonitoredItemNotificationActivator.Instance.CreateInstance();
            item.ClientHandle = 77;
            item.Value = new DataValue(42);
            var eventItem = (EventFieldList)EventFieldListActivator.Instance.CreateInstance();
            eventItem.ClientHandle = 77;
            eventItem.EventFields = [42];
            var data = new DataChangeNotification { MonitoredItems = [item] };
            var eventList = new EventNotificationList { Events = [eventItem] };
            source.NotificationData = [new ExtensionObject(events ? eventList : data)];
            List<NotificationMessage> messages = surface == "queued"
                ? [new NotificationMessage { SequenceNumber = 1 }, source]
                : [source];
            NotificationMessage published = queue.Enqueue(messages, [], out _, out _, out NotificationMessageLease publishedLease);
            NotificationMessageLease? sharedLease = null;
            NotificationMessage selected;
            switch (surface)
            {
                case "queued":
                    publishedLease.Release();
                    selected = queue.TryDequeueQueued([], false, out _, out sharedLease)!;
                    break;
                case "publish":
                    sharedLease = publishedLease;
                    selected = published;
                    break;
                default:
                    // the response that carried the message was sent long ago.
                    publishedLease.Release();
                    selected = surface == "republish"
                        ? queue.FindForRepublish(source.SequenceNumber)!
                        : mirrored!;
                    break;
            }
            bool shared = sharedLease != null;
            Assert.That(ReferenceEquals(selected, source), Is.EqualTo(shared),
                "Publish shares the retained message; republish and mirrors copy it.");

            uint expectedSequence = source.SequenceNumber;
            var releaseEncoder = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PublishResponse response = shared
                ? new LeasedPublishResponse(sharedLease) { NotificationMessage = selected }
                : new PublishResponse { NotificationMessage = selected };
            Task<PublishResponse> encoding = EncodeAfterReleaseAsync(response, releaseEncoder.Task);
            try
            {
                if (mutation == "acknowledge")
                {
                    Assert.That(queue.TryAcknowledge(expectedSequence), Is.True);
                }
                else if (mutation == "evict")
                {
                    queue.Enqueue(
                        [new NotificationMessage { SequenceNumber = 3 }, new NotificationMessage { SequenceNumber = 4 }],
                        [], out _, out _);
                }
                else
                {
                    queue.Clear();
                }

                // a payload still owned by a response being sent is not recycled.
                Assert.That(source.IsEmpty, Is.EqualTo(!shared));
            }
            finally
            {
                releaseEncoder.TrySetResult(true);
            }
            var reused = (NotificationMessage)NotificationMessageActivator.Instance.CreateInstance();
            reused.SequenceNumber = 999;
            PublishResponse decoded;
            try
            {
                decoded = await encoding.ConfigureAwait(false);
            }
            finally
            {
                reused.Reuse();
            }

            // the transport has sent the response: the last owner recycles the payload.
            ((IPooledEncodeable)response).Reuse();
            Assert.That(source.IsEmpty, Is.True);

            Assert.That(decoded.NotificationMessage.SequenceNumber, Is.EqualTo(expectedSequence));
            Assert.That(decoded.NotificationMessage.NotificationData, Has.Count.EqualTo(1));
            if (events)
            {
                Assert.That(decoded.NotificationMessage.NotificationData[0].TryGetValue(out EventNotificationList? value),
                    Is.True);
                Assert.That(value!.Events[0].ClientHandle, Is.EqualTo(77u));
                Assert.That(value.Events[0].EventFields[0].GetInt32(), Is.EqualTo(42));
                Assert.That(eventItem.ClientHandle, Is.Zero);
                item.Reuse();
            }
            else
            {
                Assert.That(decoded.NotificationMessage.NotificationData[0].TryGetValue(out DataChangeNotification? value),
                    Is.True);
                Assert.That(value!.MonitoredItems[0].ClientHandle, Is.EqualTo(77u));
                Assert.That(value.MonitoredItems[0].Value.WrappedValue.GetInt32(), Is.EqualTo(42));

                // data change notifications are not pooled by the server; they are left to the GC.
                Assert.That(item.ClientHandle, Is.EqualTo(77u));
                item.Reuse();
                eventItem.Reuse();
            }
            queue.Clear();
        }

        /// <summary>
        /// A client may acknowledge a sequence number before the server finished sending the
        /// response that carries it. The acknowledgement must not recycle the payload that is
        /// still being encoded; the send completion does.
        /// </summary>
        [Test]
        public void AcknowledgeBeforeSendCompletionDoesNotRecyclePayloadStillBeingSent()
        {
            var queue = new SentMessageQueue(() => 1, 4, null, NullLogger.Instance);
            var message = new CountingNotificationMessage { SequenceNumber = 1 };
            var item = new CountingMonitoredItemNotification { ClientHandle = 5, Value = new DataValue(7) };
            message.NotificationData = [new ExtensionObject(new DataChangeNotification { MonitoredItems = [item] })];

            NotificationMessage sent = queue.Enqueue([message], [], out _, out _, out NotificationMessageLease lease);
            var response = new LeasedPublishResponse(lease) { NotificationMessage = sent };

            Assert.That(queue.TryAcknowledge(1), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(message.ReuseCount, Is.Zero, "acknowledged before it was sent");
                Assert.That(item.ReuseCount, Is.Zero);
                Assert.That(Encode(response).NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            });

            ((IPooledEncodeable)response).Reuse();
            Assert.Multiple(() =>
            {
                Assert.That(message.ReuseCount, Is.EqualTo(1));

                // data change notifications are not pooled by the server and never reused.
                Assert.That(item.ReuseCount, Is.Zero);
            });
        }

        /// <summary>
        /// The send completion before the acknowledgement keeps the retained message for
        /// Republish, which returns the original content.
        /// </summary>
        [Test]
        public void RepublishAfterSendCompletedReturnsOriginalContent()
        {
            var queue = new SentMessageQueue(() => 1, 4, null, NullLogger.Instance);
            var message = new CountingNotificationMessage { SequenceNumber = 1 };
            var item = new CountingMonitoredItemNotification { ClientHandle = 5, Value = new DataValue(7) };
            message.NotificationData = [new ExtensionObject(new DataChangeNotification { MonitoredItems = [item] })];

            NotificationMessage sent = queue.Enqueue([message], [], out _, out _, out NotificationMessageLease lease);
            var response = new LeasedPublishResponse(lease) { NotificationMessage = sent };
            Encode(response);
            ((IPooledEncodeable)response).Reuse();
            Assert.That(message.ReuseCount, Is.Zero, "still retained for republish");

            NotificationMessage republished = queue.FindForRepublish(1)!;
            Assert.That(republished, Is.Not.SameAs(message));
            Assert.That(republished.IsEqual(message), Is.True);

            Assert.That(queue.TryAcknowledge(1), Is.True);
            Assert.That(message.ReuseCount, Is.EqualTo(1));

            // the republished copy is independent of the recycled original.
            PublishResponse decoded = Encode(new PublishResponse { NotificationMessage = republished });
            Assert.That(decoded.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            Assert.That(
                decoded.NotificationMessage.NotificationData[0].TryGetValue(out DataChangeNotification? data),
                Is.True);
            Assert.That(data!.MonitoredItems[0].ClientHandle, Is.EqualTo(5u));
            Assert.That(data.MonitoredItems[0].Value.WrappedValue.GetInt32(), Is.EqualTo(7));
        }

        /// <summary>
        /// When sending fails the transport drops the response, and a failure path may release
        /// it more than once (fault after a failed send, retried send on another channel). The
        /// payload is released exactly once and only after the queue released it too.
        /// </summary>
        [Test]
        public void ChannelFailureMidSendReleasesPayloadExactlyOnce(
            [Values(false, true)] bool acknowledgeFirst)
        {
            var queue = new SentMessageQueue(() => 1, 4, null, NullLogger.Instance);
            var message = new CountingNotificationMessage { SequenceNumber = 1 };
            message.NotificationData = [new ExtensionObject(new DataChangeNotification
            {
                MonitoredItems = [new CountingMonitoredItemNotification { ClientHandle = 5 }]
            })];
            NotificationMessage sent = queue.Enqueue([message], [], out _, out _, out NotificationMessageLease lease);
            var response = new LeasedPublishResponse(lease) { NotificationMessage = sent };

            // a copy made while the response was in flight owns no reference.
            var copy = (PublishResponse)response.Clone();
            ((IPooledEncodeable)copy).Reuse();

            if (acknowledgeFirst)
            {
                Assert.That(queue.TryAcknowledge(1), Is.True);
            }

            // the send failed: the listener releases the response, then its error path again.
            ((IPooledEncodeable)response).Reuse();
            ((IPooledEncodeable)response).Reuse();
            Assert.That(message.ReuseCount, Is.EqualTo(acknowledgeFirst ? 1 : 0));

            if (!acknowledgeFirst)
            {
                // the client never got it and republishes; it acknowledges afterwards.
                Assert.That(queue.FindForRepublish(1), Is.Not.Null);
                Assert.That(queue.TryAcknowledge(1), Is.True);
            }

            queue.Clear();
            Assert.That(message.ReuseCount, Is.EqualTo(1));
            Assert.That(lease.References, Is.Zero);
        }

        /// <summary>
        /// The rented array that holds the data change notifications of a message is returned
        /// to its pool with the payload, after the acknowledgement and the send completion,
        /// and the message encodes only its slice of it.
        /// </summary>
        [Test]
        public void RentedNotificationArrayIsReturnedWithThePayload([Values(false, true)] bool acknowledgeFirst)
        {
            var queue = new SentMessageQueue(() => 1, 4, null, NullLogger.Instance);
            MonitoredItemNotification[] rented = System.Buffers.ArrayPool<MonitoredItemNotification>.Shared.Rent(3);
            for (int ii = 0; ii < 3; ii++)
            {
                rented[ii] = new MonitoredItemNotification { ClientHandle = (uint)ii + 1, Value = new DataValue(ii) };
            }
            var message = new NotificationMessage
            {
                SequenceNumber = 1,
                NotificationData = [new ExtensionObject(new DataChangeNotification
                {
                    MonitoredItems = new ArrayOf<MonitoredItemNotification>(rented.AsMemory(0, 3))
                })]
            };

            MonitoredItemNotification[] originals = rented.Take(3).ToArray();
            NotificationMessage sent = queue.Enqueue(
                [message], [], out _, out _, out NotificationMessageLease lease, [rented]);
            var response = new LeasedPublishResponse(lease) { NotificationMessage = sent };
            PublishResponse decoded = Encode(response);
            Assert.That(
                decoded.NotificationMessage.NotificationData[0].TryGetValue(out DataChangeNotification? data),
                Is.True);
            Assert.That(data!.MonitoredItems.Count, Is.EqualTo(3));
            Assert.That(data.MonitoredItems[2].ClientHandle, Is.EqualTo(3u));

            if (acknowledgeFirst)
            {
                Assert.That(queue.TryAcknowledge(1), Is.True);
                Assert.That(rented[0], Is.SameAs(originals[0]), "still being sent");
                ((IPooledEncodeable)response).Reuse();
            }
            else
            {
                ((IPooledEncodeable)response).Reuse();
                Assert.That(rented[0], Is.SameAs(originals[0]), "still retained for republish");
                Assert.That(queue.TryAcknowledge(1), Is.True);
            }

            // returned with clearArray, so it no longer references the recycled notifications
            // (another test may have rented it again since).
            for (int ii = 0; ii < originals.Length; ii++)
            {
                Assert.That(rented[ii], Is.Not.SameAs(originals[ii]));
            }
        }

        /// <summary>
        /// Messages dropped unsent on queue overflow return their rented arrays.
        /// </summary>
        [Test]
        public void OverflowReturnsTheRentedArraysOfDroppedMessages()
        {
            var queue = new SentMessageQueue(() => 1, 1, null, NullLogger.Instance);
            var droppedItem = new MonitoredItemNotification { ClientHandle = 1 };
            var keptItem = new MonitoredItemNotification { ClientHandle = 2 };
            MonitoredItemNotification[] dropped = System.Buffers.ArrayPool<MonitoredItemNotification>.Shared.Rent(1);
            dropped[0] = droppedItem;
            MonitoredItemNotification[] kept = System.Buffers.ArrayPool<MonitoredItemNotification>.Shared.Rent(1);
            kept[0] = keptItem;
            List<NotificationMessage> messages =
            [
                new NotificationMessage { SequenceNumber = 1 },
                new NotificationMessage { SequenceNumber = 2 }
            ];
            List<MonitoredItemNotification[]?> rented = [dropped, kept];

            NotificationMessage sent = queue.Enqueue(messages, [], out _, out uint discarded, out NotificationMessageLease lease, rented);

            Assert.Multiple(() =>
            {
                Assert.That(sent.SequenceNumber, Is.EqualTo(2u));
                Assert.That(discarded, Is.EqualTo(1u));
                Assert.That(dropped[0], Is.Not.SameAs(droppedItem), "returned with the dropped message");
                Assert.That(kept[0], Is.SameAs(keptItem));
                Assert.That(rented, Has.Count.EqualTo(1), "trimmed with the messages");
            });
            lease.Release();
            queue.Clear();
            Assert.That(kept[0], Is.Not.SameAs(keptItem));
        }

        /// <summary>
        /// A release beyond the owners never recycles the payload a second time, and a
        /// later regular release still finds the count intact.
        /// </summary>
        [Test]
        public void ExtraReleaseNeverRecyclesTwice()
        {
            var message = new CountingNotificationMessage { SequenceNumber = 1 };
            var lease = new NotificationMessageLease(message);
            lease.Release();
            lease.Release();
            Assert.That(message.ReuseCount, Is.EqualTo(1));
            Assert.That(lease.References, Is.Zero);

            var shared = new CountingNotificationMessage { SequenceNumber = 2 };
            var sharedLease = new NotificationMessageLease(shared);
            sharedLease.AddReference();
            sharedLease.Release();
            Assert.That(shared.ReuseCount, Is.Zero, "one owner left");
            sharedLease.Release();
            sharedLease.Release();
            Assert.That(shared.ReuseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// The retained messages are exposed read-only, in queue order, as the shared
        /// instances (not copies).
        /// </summary>
        [Test]
        public void SentMessagesExposesTheRetainedMessagesInOrder()
        {
            var queue = new SentMessageQueue(() => 1, 4, null, NullLogger.Instance);
            var first = new NotificationMessage { SequenceNumber = 1 };
            var second = new NotificationMessage { SequenceNumber = 2 };
            queue.Enqueue([first, second], [], out _, out _, out NotificationMessageLease lease);
            lease.Release();

            IReadOnlyList<NotificationMessage> sent = queue.SentMessages;
            Assert.Multiple(() =>
            {
                Assert.That(sent, Has.Count.EqualTo(2));
                Assert.That(sent[1], Is.SameAs(second));
                Assert.That(sent.ToList(), Is.EqualTo(new[] { first, second }));
                Assert.That(((System.Collections.IEnumerable)sent).Cast<NotificationMessage>().First(), Is.SameAs(first));
            });
            queue.Clear();
            Assert.That(sent, Is.Empty);
        }

        /// <summary>
        /// A keep-alive is not retained for republish, so its response is the only owner and
        /// recycles it once it was sent.
        /// </summary>
        [Test]
        public void KeepAliveMessageIsOwnedByItsResponseOnly()
        {
            var keepAlive = new CountingNotificationMessage { SequenceNumber = 3 };
            var lease = new NotificationMessageLease(keepAlive);
            var response = new LeasedPublishResponse(lease) { NotificationMessage = keepAlive };
            Assert.That(keepAlive.ReuseCount, Is.Zero);
            ((IPooledEncodeable)response).Reuse();
            Assert.That(keepAlive.ReuseCount, Is.EqualTo(1));
        }

        private static PublishResponse Encode(PublishResponse response)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            response.Encode(encoder);
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);
            var decoded = new PublishResponse();
            decoded.Decode(decoder);
            return decoded;
        }

        /// <summary>
        /// Waits for queue mutation before round-tripping the retained publish response through binary encoding.
        /// </summary>
        private static async Task<PublishResponse> EncodeAfterReleaseAsync(PublishResponse response, Task release)
        {
            await release.ConfigureAwait(false);
            return Encode(response);
        }

        /// <summary>
        /// Counts recycling instead of returning the instance to the shared pool.
        /// </summary>
        private sealed class CountingNotificationMessage : NotificationMessage
        {
            public int ReuseCount { get; private set; }

            protected override void ReuseCore()
            {
                ReuseCount++;
            }
        }

        /// <summary>
        /// Counts recycling instead of returning the instance to the shared pool.
        /// </summary>
        private sealed class CountingMonitoredItemNotification : MonitoredItemNotification
        {
            public int ReuseCount { get; private set; }

            protected override void ReuseCore()
            {
                ReuseCount++;
            }
        }
    }
}
