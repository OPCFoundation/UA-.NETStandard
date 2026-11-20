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
        private const PublishStateChangedMask kSessionNotConnected = PublishStateChangedMask.Stopped | PublishStateChangedMask.SessionNotConnected;

        public record KeepAliveTestDataProvider(PublishStateChangedMask ExpectedPublishState) : IFormattable
        {
            public bool SessionConnected { get; init; }
            public bool SessionReconnecting { get; init; }
            public bool SessionKeepAliveStopped { get; init; }
            public string ToString(string format, IFormatProvider formatProvider)
            {
                return $"Connected={SessionConnected}, " +
                    $"reconnecting={SessionReconnecting}, " +
                    $"keepAlive={SessionKeepAliveStopped}. " +
                    $"Expected status:{ExpectedPublishState}";
            }
        }

        private sealed class SubscriptionContainer : IDisposable
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

        private static ISession BuildSessionMock(Func<uint, uint, bool> republishHandler = null, Action<Mock<ISession>> setup = null)
        {
            uint subscriptionIdSeed = 0u;

            var session = new Mock<ISession>();
            if (republishHandler is not null)
            {
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
            }
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
            setup?.Invoke(session);
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
        /// The most entries the incoming message queue may hold.
        /// </summary>
        private const int kMaxIncomingMessages = 2048;

        private static NotificationMessage BuildDataMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                NotificationData = [new(new DataChangeNotification
                {
                    SequenceNumber = sequenceNumber,
                    MonitoredItems = [new MonitoredItemNotification { ClientHandle = 1 }]
                })]
            };
        }

        /// <summary>
        /// Counts the entries of the incoming message queue, and those that still
        /// reference a message.
        /// </summary>
        private static int CountIncomingMessages(Subscription subscription, out int withMessage)
        {
            System.Reflection.FieldInfo field = typeof(Subscription).GetField(
                "m_incomingMessages",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            withMessage = 0;
            if (field.GetValue(subscription) is not System.Collections.ICollection entries)
            {
                return 0;
            }
            object cache = typeof(Subscription).GetField(
                "m_cache",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(subscription);
            lock (cache)
            {
                foreach (object entry in entries)
                {
                    if (entry.GetType().GetField("Message").GetValue(entry) != null)
                    {
                        withMessage++;
                    }
                }
                return entries.Count;
            }
        }

        /// <summary>
        /// A Publish response whose sequence number is far ahead of the last
        /// one processed must not add one placeholder per missing sequence
        /// number. The client resynchronises on the new sequence number and
        /// still delivers the message.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60_000)]
        public async Task SequenceNumberFarAheadDoesNotFillTheQueueAsync(bool firstMessage, CancellationToken ct)
        {
            const uint kFarAhead = 200_000;
            int delivered = 0;
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new() { PublishingEnabled = true })
            {
                DisableMonitoredItemCache = true,
                FastDataChangeCallback = (_, _, _) => Interlocked.Increment(ref delivered)
            };
            using (subscription)
            {
                subscription.Session = BuildSessionMock((_, _) => false);
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                int expected = 1;
                if (!firstMessage)
                {
                    subscription.SaveMessageInCache([], BuildDataMessage(1));
                    expected++;
                }

                subscription.SaveMessageInCache([], BuildDataMessage(kFarAhead));

                Assert.That(CountIncomingMessages(subscription, out _),
                    Is.LessThanOrEqualTo(kMaxIncomingMessages));

                // the next message in sequence is delivered without waiting
                // for the messages that were skipped.
                subscription.SaveMessageInCache([], BuildDataMessage(kFarAhead + 1));
                expected++;

                while (Volatile.Read(ref delivered) < expected)
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
                Assert.That(CountIncomingMessages(subscription, out _),
                    Is.LessThanOrEqualTo(kMaxIncomingMessages));
            }
        }

        /// <summary>
        /// While a message is missing at the head of the incoming queue, later
        /// messages are delivered but their entries stay queued behind it. The
        /// queue stays within its limit, and processed entries do not keep their
        /// messages.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task IncomingMessageQueueIsBoundedWhileAMessageIsMissingAsync(CancellationToken ct)
        {
            const int kMessages = kMaxIncomingMessages + 1000;
            int delivered = 0;
            var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new() { PublishingEnabled = true })
            {
                DisableMonitoredItemCache = true,
                FastDataChangeCallback = (_, _, _) => Interlocked.Increment(ref delivered)
            };
            using (subscription)
            {
                // Republish never completes; message 2 stays listed as available
                // and its entry is neither delivered nor given up.
                subscription.Session = BuildSessionMock(
                    setup: mock => mock
                        .Setup(x => x.RepublishAsync(It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((false, new ServiceResult(StatusCodes.BadTimeout))));
                await subscription.CreateAsync(ct).ConfigureAwait(false);

                subscription.SaveMessageInCache([1], BuildDataMessage(1));
                int saved = 1;
                for (uint sequenceNumber = 3; sequenceNumber < kMessages + 3; sequenceNumber++)
                {
                    subscription.SaveMessageInCache([2, sequenceNumber], BuildDataMessage(sequenceNumber));
                    saved++;
                    Assert.That(CountIncomingMessages(subscription, out _),
                        Is.LessThanOrEqualTo(kMaxIncomingMessages));
                    if (saved % 256 == 0)
                    {
                        // Let the worker deliver each batch, so the limit gives up
                        // processed entries rather than messages still waiting for it.
                        await WaitForDeliveredAsync(saved).ConfigureAwait(false);
                    }
                }

                // Every message after the gap is delivered without the missing one.
                await WaitForDeliveredAsync(kMessages + 1).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref delivered), Is.EqualTo(kMessages + 1));
                CountIncomingMessages(subscription, out int retained);
                Assert.That(retained, Is.Zero, "Processed entries must not keep their messages.");
            }

            async Task WaitForDeliveredAsync(int count)
            {
                while (Volatile.Read(ref delivered) < count)
                {
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Transfer recovery queues at most as many messages for Republish as
        /// the incoming queue holds.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task TransferRecoveryQueuesNoMoreMessagesThanTheQueueBoundAsync(CancellationToken ct)
        {
            const int kAvailable = kMaxIncomingMessages + 500;
            using var subscription = new Subscription(NUnitTelemetryContext.Create())
            {
                RepublishAfterTransfer = true
            };
            UInt32Collection available = [.. Enumerable.Range(1, kAvailable).Select(value => (uint)value)];
            ISession session = BuildSessionMock(
                (_, _) => false,
                mock =>
                {
                    mock.Setup(value => value.Subscriptions).Returns([]);
                    mock.Setup(value => value.RemoveTransferredSubscription(subscription)).Returns(true);
                    mock.Setup(value => value.AddSubscription(subscription)).Returns(true);
                });
            subscription.Session = session;
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            bool transferred = await subscription
                .TransferAsync(session, subscription.Id, available, ct)
                .ConfigureAwait(false);

            Assert.That(transferred, Is.True);
            Assert.That(CountIncomingMessages(subscription, out _),
                Is.LessThanOrEqualTo(kMaxIncomingMessages));
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

        [DatapointSource]
        public IEnumerable<KeepAliveTestDataProvider> SubscriptionKeepAliveValues()
        {
            yield return new(PublishStateChangedMask.Stopped) { SessionConnected = true, SessionReconnecting = false, SessionKeepAliveStopped = false };

            yield return new(kSessionNotConnected) { SessionConnected = false, SessionReconnecting = false, SessionKeepAliveStopped = false };
            yield return new(kSessionNotConnected) { SessionConnected = false, SessionReconnecting = true, SessionKeepAliveStopped = false };
            yield return new(kSessionNotConnected) { SessionConnected = false, SessionReconnecting = false, SessionKeepAliveStopped = true };
            yield return new(kSessionNotConnected) { SessionConnected = false, SessionReconnecting = true, SessionKeepAliveStopped = true };

            yield return new(kSessionNotConnected) { SessionConnected = true, SessionReconnecting = true, SessionKeepAliveStopped = false };
            yield return new(kSessionNotConnected) { SessionConnected = true, SessionReconnecting = true, SessionKeepAliveStopped = true };

            yield return new(kSessionNotConnected) { SessionConnected = true, SessionReconnecting = false, SessionKeepAliveStopped = true };
        }

        [Theory]
        [CancelAfter(Subscription.MinKeepAliveTimerInterval * 10)]
        public async Task RespectsStateOfSessionDuringKeepAliveCalls(KeepAliveTestDataProvider testData, CancellationToken ct)
        {
            var keepAliveCompleted = new TaskCompletionSource<PublishStateChangedMask>(TaskCreationOptions.RunContinuationsAsynchronously);
            void KeepAliveHasTriggered(Subscription x, PublishStateChangedEventArgs y) => keepAliveCompleted.TrySetResult(y.Status);
            ISession session = BuildSessionMock(
                setup: mock =>
                {
                    mock.Setup(x => x.Connected).Returns(testData.SessionConnected);
                    mock.Setup(x => x.Reconnecting).Returns(testData.SessionReconnecting);
                    mock.Setup(x => x.KeepAliveStopped).Returns(testData.SessionKeepAliveStopped);
                });

            using var subscription = new Subscription(
                NUnitTelemetryContext.Create(),
                new() { PublishingEnabled = true })
            {
                Session = session
            };
            subscription.PublishStatusChanged += KeepAliveHasTriggered;
            await subscription.CreateAsync(ct).ConfigureAwait(false);
            await Task.WhenAny(keepAliveCompleted.Task, Task.Delay(-1, ct)).ConfigureAwait(false);

            Assert.That(keepAliveCompleted.Task.Result, Is.EqualTo(testData.ExpectedPublishState));
        }
    }
}
