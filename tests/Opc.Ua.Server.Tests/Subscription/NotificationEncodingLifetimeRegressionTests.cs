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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [NonParallelizable]
    public sealed class NotificationEncodingLifetimeRegressionTests
    {
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
            NotificationMessage published = queue.Enqueue(messages, [], out _, out _);
            NotificationMessage selected = surface switch
            {
                "queued" => queue.TryDequeueQueued([], false, out _)!,
                "republish" => queue.FindForRepublish(source.SequenceNumber)!,
                "fullMirror" or "deltaMirror" => mirrored!,
                _ => published
            };
            uint expectedSequence = source.SequenceNumber;
            var releaseEncoder = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new PublishResponse { NotificationMessage = selected };
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
                Assert.That(source.IsEmpty, Is.True);
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
                Assert.That(item.ClientHandle, Is.Zero);
                eventItem.Reuse();
            }
            queue.Clear();
        }

        private static async Task<PublishResponse> EncodeAfterReleaseAsync(PublishResponse response, Task release)
        {
            await release.ConfigureAwait(false);
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(context);
            response.Encode(encoder);
            using var decoder = new BinaryDecoder(encoder.CloseAndReturnBuffer()!, context);
            var decoded = new PublishResponse();
            decoded.Decode(decoder);
            return decoded;
        }

    }
}
