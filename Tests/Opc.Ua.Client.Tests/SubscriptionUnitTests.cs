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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    public class SubscriptionUnitTests
    {
        private class SubscriptionContainer : IDisposable
        {
            private readonly CancellationTokenRegistration m_tokedCancellation;
            public Subscription Subscription { get; }
            public Task[] ProcessedMessages { get; }

            public SubscriptionContainer(Subscription subscription, TaskCompletionSource<bool>[] awaiters, CancellationToken token)
            {
                Subscription = subscription;
                ProcessedMessages = [.. awaiters.Select(x => x.Task)];
                m_tokedCancellation = token.Register(() =>
                {
                    foreach (TaskCompletionSource<bool> awaiter in awaiters)
                    {
                        awaiter.TrySetCanceled();
                    }
                });
            }

            public void Dispose()
            {
                m_tokedCancellation.Dispose();
                Subscription.Dispose();
            }
        }

        private static Task AwaitForRepublishTimeout(CancellationToken ct)
        {
            return Task.Delay(Subscription.RepublishMessageTimeout + 100, ct);
        }

        private static ISession BuildSessionMock(Func<uint, uint, bool> republishHandler)
        {
            uint subscriptionIdSeed = 0u;

            var session = new Mock<ISession>();
            session
                .Setup(x => x.RepublishAsync(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync<uint, uint, CancellationToken, ISession, (bool, ServiceResult)>((
                    subscriptionId,
                    sequenceNumber,
                    ct) =>
                {
                    if (subscriptionId > subscriptionIdSeed)
                    {
                        return (true, StatusCodes.BadSubscriptionIdInvalid);
                    }
                    if (republishHandler(subscriptionId, sequenceNumber))
                    {
                        return (true, ServiceResult.Good);
                    }
                    return (true, StatusCodes.BadMessageNotAvailable);
                });
            session
                .Setup(x => x
                    .CreateSubscriptionAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<double>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<uint>(),
                        It.IsAny<bool>(),
                        It.IsAny<byte>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync<
                    RequestHeader,
                    double,
                    uint,
                    uint,
                    uint,
                    bool,
                    byte,
                    CancellationToken,
                    ISession,
                    CreateSubscriptionResponse
                    >((
                        requestHeader,
                        requestedPublishingInterval,
                        requestedLifetimeCount,
                        requestedMaxKeepAliveCount,
                        maxNotificationsPerPublish,
                        publishingEnabled,
                        priority,
                        ct
                      ) => new() { SubscriptionId = ++subscriptionIdSeed });
            session
                .Setup(x => x.SetPublishingModeAsync(It.IsAny<RequestHeader>(), It.IsAny<bool>(), It.IsAny<UInt32Collection>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync<
                    RequestHeader,
                    bool,
                    UInt32Collection,
                    CancellationToken,
                    ISession,
                    SetPublishingModeResponse
                    >((requestHeader, publishingEnabled, subscriptionIds, ct) => new()
                    {
                        Results = [.. subscriptionIds.Select(id => id > subscriptionIdSeed ? StatusCodes.BadSubscriptionIdInvalid : StatusCodes.Good)],
                        DiagnosticInfos = [.. subscriptionIds.Select(_ => new DiagnosticInfo())]
                    });
            return session.Object;
        }

        private static NotificationMessage[] BuildMessages(int count)
        {
            return Enumerable
                .Range(1, count)
                .Select(sequenceNumber => new NotificationMessage
                {
                    SequenceNumber = (uint)sequenceNumber,
                    NotificationData = [new(new DataChangeNotification { SequenceNumber = (uint)sequenceNumber })]
                })
                .Prepend(new())//stub to compensate sequenceNumbers start from 1. Should be ignored
                .ToArray();
        }

        private static async Task<SubscriptionContainer> BuildSubscriptionAsync(
            NotificationMessage[] messagesToProcess,
            bool sequentialPublishing,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool>[] messageAwaiters = messagesToProcess
                .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToArray();
            messageAwaiters[0].SetResult(true);
            List<uint> availableSequenceNumbers = [.. messagesToProcess.Skip(1).Select(x => x.SequenceNumber)];

            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new()
                {
                    PublishingEnabled = true,
                    SequentialPublishing = sequentialPublishing,
                    MaxMessageCount = messagesToProcess.Length,
                })
            {
                FastDataChangeCallback = (_, message, _) =>
                {
                    messageAwaiters[message.SequenceNumber].SetResult(true);
                },
            };
            subscription.Session = BuildSessionMock((subscriptionId, sequenceNumber) =>
            {
                //simplified republish emulation
                if (subscription.Id == subscriptionId && availableSequenceNumbers.Remove(sequenceNumber))
                {
                    subscription.SaveMessageInCache(null, messagesToProcess[sequenceNumber]);
                    return true;
                }
                return false;
            });
            await subscription.CreateAsync(cancellationToken).ConfigureAwait(false);
            return new(subscription, messageAwaiters, cancellationToken);
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            TestContext.AddFormatter<NotificationMessage>((obj) =>
            {
                if (obj is NotificationMessage other)
                {
                    return $"{nameof(NotificationMessage)}: {other.SequenceNumber}";
                }
                return null;
            });
        }

        [Test]
        [Explicit("Test shows possibility for broken order of notifications during sequential publishing")]
        [CancelAfter(Subscription.RepublishMessageTimeout * 6)]
        public async Task UnorderedMessagesWouldBeLostForSequentialPublishingAsync(CancellationToken ct)
        {
            NotificationMessage[] messages = BuildMessages(5);
            using SubscriptionContainer container = await BuildSubscriptionAsync(messages, sequentialPublishing: true, ct).ConfigureAwait(false);
            Subscription subscription = container.Subscription;

            subscription.SaveMessageInCache([], messages[3]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[2]);//this one will be processed, because one message out of order is always awaited
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[4]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[1]);//this one should be lost due to expiration timout
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[5]);

            await Task.WhenAll(
                container.ProcessedMessages[2],
                container.ProcessedMessages[3],
                container.ProcessedMessages[4],
                container.ProcessedMessages[5]
                ).ConfigureAwait(false);

            Assert.That(subscription.Notifications, Is.EqualTo([messages[2], messages[3], messages[4], messages[5]]));
        }

        [Test]
        [CancelAfter(Subscription.RepublishMessageTimeout * 3)]
        public async Task WillRestoreOrderOfTwoMessagesForSequentialPublishingAsync(CancellationToken ct)
        {
            NotificationMessage[] messages = BuildMessages(3);
            using SubscriptionContainer container = await BuildSubscriptionAsync(messages, sequentialPublishing: true, ct).ConfigureAwait(false);
            Subscription subscription = container.Subscription;

            subscription.SaveMessageInCache([], messages[2]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[1]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache([], messages[3]);

            await Task.WhenAll(container.ProcessedMessages).ConfigureAwait(false);
            Assert.That(subscription.Notifications, Is.EqualTo(messages.Skip(1)));
        }

        [Theory]
        [CancelAfter(Subscription.RepublishMessageTimeout * 7)]
        public async Task WillAbandonMissedMessagesIfThereAreNoAvailableSequenceNumbersAsync(bool sequentialPublishing, CancellationToken ct)
        {
            int[] sequenceNumbersToPublish = [/*1..2 gap*/ 3, 4, /*5..7 gap*/ 8, 9, 10];
            NotificationMessage[] messages = BuildMessages(sequenceNumbersToPublish[^1]);
            NotificationMessage[] messagesToPublish = [.. sequenceNumbersToPublish.Select(x => messages[x])];
            using SubscriptionContainer container = await BuildSubscriptionAsync(messages, sequentialPublishing, ct).ConfigureAwait(false);
            Subscription subscription = container.Subscription;

            foreach (NotificationMessage message in messagesToPublish)
            {
                subscription.SaveMessageInCache([], message);
                await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            }
            await Task.WhenAll(sequenceNumbersToPublish.Select(x => container.ProcessedMessages[x])).ConfigureAwait(false);

            if (sequentialPublishing)
            {
                Assert.That(subscription.Notifications, Is.EqualTo(messagesToPublish));
            }
            else
            {
                Assert.That(subscription.Notifications, Is.EquivalentTo(messagesToPublish));
            }
        }

        [Theory]
        [CancelAfter(Subscription.RepublishMessageTimeout * 3)]
        public async Task WillRepublishIfMissedMessagesOnFirstPublishAsync(
            bool sequentialPublishing,
            [Values(2, 5, 8, 11)] int gapEnd,
            CancellationToken ct)
        {
            int finalMessage = gapEnd + 1;
            NotificationMessage[] messages = BuildMessages(finalMessage);
            uint[] sequenceNumbersOfGap = [.. messages.Skip(1).Take(gapEnd).Select(x => x.SequenceNumber)];
            using SubscriptionContainer container = await BuildSubscriptionAsync(messages, sequentialPublishing, ct).ConfigureAwait(false);
            Subscription subscription = container.Subscription;

            subscription.SaveMessageInCache(sequenceNumbersOfGap, messages[gapEnd]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache(sequenceNumbersOfGap, messages[finalMessage]);
            await Task.WhenAll(container.ProcessedMessages).ConfigureAwait(false);

            if (sequentialPublishing)
            {
                Assert.That(subscription.Notifications, Is.EqualTo(messages.Skip(1)));
            }
            else
            {
                Assert.That(subscription.Notifications, Is.EquivalentTo(messages.Skip(1)));
            }
        }

        [Theory]
        [CancelAfter(Subscription.RepublishMessageTimeout * 3)]
        public async Task WillRepublishIfMissedMessagesInBetweenOfPublishesAsync(
            bool sequentialPublishing,
            [Values(1, 6, 9)] int gapStart,
            [Values(1, 6, 9)] int gapSize,
            CancellationToken ct)
        {
            int gapEnd = gapStart + gapSize + 1;
            int finalMessage = gapEnd + 1;
            NotificationMessage[] messages = BuildMessages(finalMessage);
            uint[] sequenceNumbersOfGap = [.. messages.Skip(1).Skip(gapStart).Take(gapSize).Select(x => x.SequenceNumber)];
            using SubscriptionContainer container = await BuildSubscriptionAsync(messages, sequentialPublishing, ct).ConfigureAwait(false);
            Subscription subscription = container.Subscription;

            for (int i = 1; i <= gapStart; i++)
            {
                subscription.SaveMessageInCache([], messages[i]);
            }
            subscription.SaveMessageInCache(sequenceNumbersOfGap, messages[gapEnd]);
            await AwaitForRepublishTimeout(ct).ConfigureAwait(false);
            subscription.SaveMessageInCache(sequenceNumbersOfGap, messages[finalMessage]);
            await Task.WhenAll(container.ProcessedMessages).ConfigureAwait(false);

            if (sequentialPublishing)
            {
                Assert.That(subscription.Notifications, Is.EqualTo(messages.Skip(1)));
            }
            else
            {
                Assert.That(subscription.Notifications, Is.EquivalentTo(messages.Skip(1)));
            }
        }
    }
}
