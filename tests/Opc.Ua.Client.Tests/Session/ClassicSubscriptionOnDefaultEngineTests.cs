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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

#pragma warning disable CS0618 // tests exercise the obsolete classic API

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Classic subscriptions held by a session that uses the default (V2)
    /// subscription engine.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    public sealed class ClassicSubscriptionOnDefaultEngineTests
    {
        /// <summary>
        /// A Publish response that never arrives leaves a gap. The classic
        /// subscription recovers the message with Republish through the default
        /// engine, and its messages are acknowledged. A message the client cannot
        /// receive is released on the server; a message the server no longer has
        /// is skipped.
        /// </summary>
        [TestCase("Good")]
        [TestCase("BadEncodingLimitsExceeded")]
        [TestCase("BadMessageNotAvailable")]
        [CancelAfter(60_000)]
        public async Task LostMessageIsRepublishedAndMessagesAreAcknowledgedAsync(
            string republishOutcome,
            CancellationToken ct)
        {
            StatusCode republishStatus = republishOutcome switch
            {
                "BadEncodingLimitsExceeded" => StatusCodes.BadEncodingLimitsExceeded,
                "BadMessageNotAvailable" => StatusCodes.BadMessageNotAvailable,
                _ => StatusCodes.Good
            };
            bool recovered = republishStatus == StatusCodes.Good;
            bool released = republishStatus == StatusCodes.BadEncodingLimitsExceeded;
            uint[] expectedDelivered = recovered ? [1, 2, 3, 4, 5] : [1, 3, 4, 5];
            uint[] expectedAcknowledged = recovered || released ? [1, 2, 3, 4, 5] : [1, 3, 4, 5];
            const uint kSubscriptionId = 7;
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);

            // The server sends 1, 3, 4, 5 (2 is lost on the way) and then
            // keep-alives. It keeps every message that is not acknowledged.
            uint[] script = [1, 3, 4, 5];
            int scriptIndex = -1;
            var available = new SortedSet<uint>();
            var acknowledged = new HashSet<uint>();
            object gate = new();
            int republishRequests = 0;

            channel.RequestHandler = async (request, requestCt) =>
            {
                switch (request)
                {
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            SubscriptionId = kSubscriptionId,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case DeleteSubscriptionsRequest delete:
                        return new DeleteSubscriptionsResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = delete.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case RepublishRequest replay:
                        Interlocked.Increment(ref republishRequests);
                        if (!recovered)
                        {
                            if (!released)
                            {
                                lock (gate)
                                {
                                    available.Remove(replay.RetransmitSequenceNumber);
                                }
                            }
                            throw new ServiceResultException(republishStatus);
                        }
                        return new RepublishResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            NotificationMessage = CreateDataMessage(replay.RetransmitSequenceNumber)
                        };
                    case PublishRequest publish:
                    {
                        uint[] availableNow;
                        NotificationMessage message;
                        int index;
                        lock (gate)
                        {
                            foreach (SubscriptionAcknowledgement ack in publish.SubscriptionAcknowledgements)
                            {
                                if (ack.SubscriptionId == kSubscriptionId)
                                {
                                    acknowledged.Add(ack.SequenceNumber);
                                    available.Remove(ack.SequenceNumber);
                                }
                            }
                            index = ++scriptIndex;
                            if (index < script.Length)
                            {
                                uint seq = script[index];
                                available.Add(seq);
                                if (seq == 3)
                                {
                                    available.Add(2);
                                }
                                message = CreateDataMessage(seq);
                            }
                            else
                            {
                                message = new NotificationMessage
                                {
                                    SequenceNumber = 6,
                                    PublishTime = DateTime.UtcNow
                                };
                            }
                            availableNow = [.. available];
                        }
                        await Task.Delay(index < script.Length ? 20 : 200, requestCt).ConfigureAwait(false);
                        return new PublishResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            SubscriptionId = kSubscriptionId,
                            AvailableSequenceNumbers = availableNow,
                            NotificationMessage = message,
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => (StatusCode)StatusCodes.Good)
                        };
                    }
                    default:
                        return channel.CreateResponse(request);
                }
            };

            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            session.MinPublishRequestCount = 1;
            session.MaxPublishRequestCount = 1;

            var delivered = new ConcurrentQueue<uint>();
            using var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true,
                DisableMonitoredItemCache = true,
                FastDataChangeCallback = (_, notification, _) => delivered.Enqueue(notification.SequenceNumber)
            };

            await session.OpenAsync("classic-on-default", new UserIdentity(), ct).ConfigureAwait(false);
            Assert.That(session.SubscriptionEngine, Is.InstanceOf<DefaultSubscriptionEngine>());
            Assert.That(session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            // The placeholder for 2 is republished after RepublishMessageTimeout.
            while (!(Volatile.Read(ref republishRequests) >= 1 &&
                expectedDelivered.All(delivered.Contains) &&
                AllAcknowledged()))
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            Assert.That(Volatile.Read(ref republishRequests), Is.GreaterThanOrEqualTo(1));
            Assert.That(delivered, Is.EquivalentTo(expectedDelivered));
            await session.CloseAsync(ct).ConfigureAwait(false);

            bool AllAcknowledged()
            {
                lock (gate)
                {
                    return acknowledged.IsSupersetOf(expectedAcknowledged);
                }
            }
        }

        /// <summary>
        /// Publish requests of a session that holds only classic subscriptions keep
        /// their timeout hint within half the operation timeout, like those of the
        /// engine's own subscriptions.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task PublishTimeoutHintOfClassicSubscriptionsStaysWithinTheOperationTimeoutAsync(
            CancellationToken ct)
        {
            const uint kSubscriptionId = 9;
            const int kOperationTimeout = 10_000;
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            var hints = new ConcurrentQueue<uint>();

            channel.RequestHandler = async (request, requestCt) =>
            {
                switch (request)
                {
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            SubscriptionId = kSubscriptionId,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case DeleteSubscriptionsRequest delete:
                        return new DeleteSubscriptionsResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            Results = delete.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case PublishRequest publish:
                        hints.Enqueue(publish.RequestHeader.TimeoutHint);
                        await Task.Delay(50, requestCt).ConfigureAwait(false);
                        return new PublishResponse
                        {
                            ResponseHeader = channel.CreateGoodHeader(),
                            SubscriptionId = kSubscriptionId,
                            NotificationMessage = new NotificationMessage
                            {
                                SequenceNumber = 1,
                                PublishTime = DateTime.UtcNow
                            },
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => (StatusCode)StatusCodes.Good)
                        };
                    default:
                        return channel.CreateResponse(request);
                }
            };

            await using var session = new Session(
                channel.Channel, harness.Configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            session.MinPublishRequestCount = 1;
            session.MaxPublishRequestCount = 1;
            session.OperationTimeout = kOperationTimeout;

            // The keep-alive period (20 s) is longer than the operation timeout.
            using var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = 2000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true,
                DisableMonitoredItemCache = true
            };

            await session.OpenAsync("classic-hint", new UserIdentity(), ct).ConfigureAwait(false);
            Assert.That(session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            while (hints.Count < 3)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
            Assert.That(hints, Is.All.InRange(1u, (uint)(kOperationTimeout / 2)));
            await session.CloseAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// A Publish response for a subscription that is being transferred to the
        /// session cannot be dispatched yet. The default engine must not delete
        /// that subscription on the server as abandoned.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task SubscriptionBeingTransferredIsNotDeletedAsync(CancellationToken ct)
        {
            const uint kTransferredId = 1;
            const uint kLocalId = 10;
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel originChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            ScriptedChannel targetChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            var originPublish = new SemaphoreSlim(0);
            var targetPublish = new SemaphoreSlim(0);
            var deleted = new ConcurrentQueue<uint>();
            int transferredResponses = 0;
            var handledAfterTransferredResponse = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            originChannel.RequestHandler = async (request, requestCt) =>
            {
                switch (request)
                {
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = originChannel.CreateGoodHeader(),
                            SubscriptionId = kTransferredId,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = originChannel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case PublishRequest:
                        await originPublish.WaitAsync(requestCt).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    default:
                        return originChannel.CreateResponse(request);
                }
            };
            targetChannel.RequestHandler = async (request, requestCt) =>
            {
                switch (request)
                {
                    case CreateSessionRequest:
                        var created = (CreateSessionResponse)targetChannel.CreateResponse(request);
                        created.SessionId = new NodeId(2u, 1);
                        created.AuthenticationToken = new NodeId(102u, 1);
                        return created;
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            SubscriptionId = kLocalId,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case DeleteSubscriptionsRequest delete:
                        foreach (uint id in delete.SubscriptionIds)
                        {
                            deleted.Enqueue(id);
                        }
                        handledAfterTransferredResponse.TrySetResult(true);
                        return new DeleteSubscriptionsResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            Results = delete.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case TransferSubscriptionsRequest transfer:
                        // The server owns the subscription now and answers a queued
                        // Publish request with its message before the client has
                        // bound the subscription to this session.
                        targetPublish.Release();
                        await handledAfterTransferredResponse.Task.WaitAsync(TimeSpan.FromSeconds(10), requestCt)
                            .ConfigureAwait(false);
                        return new TransferSubscriptionsResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            Results = transfer.SubscriptionIds.ConvertAll(_ => new TransferResult
                            {
                                StatusCode = StatusCodes.Good,
                                AvailableSequenceNumbers = []
                            })
                        };
                    case PublishRequest publish:
                        if (Volatile.Read(ref transferredResponses) != 0)
                        {
                            // The worker handled the response and asks for the next one.
                            handledAfterTransferredResponse.TrySetResult(true);
                        }
                        await targetPublish.WaitAsync(requestCt).ConfigureAwait(false);
                        Interlocked.Increment(ref transferredResponses);
                        return new PublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            SubscriptionId = kTransferredId,
                            AvailableSequenceNumbers = [1],
                            NotificationMessage = CreateDataMessage(1),
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => (StatusCode)StatusCodes.Good)
                        };
                    default:
                        return targetChannel.CreateResponse(request);
                }
            };

            await using var origin = new Session(
                originChannel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance);
            await using var target = new Session(
                targetChannel.Channel, harness.Configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            target.MinPublishRequestCount = 1;
            target.MaxPublishRequestCount = 1;
            using var local = new Subscription(target.DefaultSubscription)
            {
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };
            using var transferred = new Subscription(origin.DefaultSubscription)
            {
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true
            };
            try
            {
                await origin.OpenAsync("origin", new UserIdentity(), ct).ConfigureAwait(false);
                await target.OpenAsync("target", new UserIdentity(), ct).ConfigureAwait(false);
                Assert.That(target.AddSubscription(local), Is.True);
                await local.CreateAsync(ct).ConfigureAwait(false);
                Assert.That(origin.AddSubscription(transferred), Is.True);
                await transferred.CreateAsync(ct).ConfigureAwait(false);

                bool result = await target.TransferSubscriptionsAsync([transferred], false, ct)
                    .ConfigureAwait(false);

                Assert.That(Volatile.Read(ref transferredResponses), Is.GreaterThanOrEqualTo(1));
                Assert.That(deleted, Does.Not.Contain(kTransferredId),
                    "The subscription being transferred must not be deleted on the server.");
                Assert.That(result, Is.True);
                Assert.That(transferred.Id, Is.EqualTo(kTransferredId));
            }
            finally
            {
                originPublish.Release(100);
                targetPublish.Release(100);
            }
        }

        private static NotificationMessage CreateDataMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = DateTime.UtcNow,
                NotificationData =
                [
                    new ExtensionObject(new DataChangeNotification
                    {
                        MonitoredItems =
                        [
                            new MonitoredItemNotification
                            {
                                ClientHandle = 1,
                                Value = new DataValue(new Variant(sequenceNumber))
                            }
                        ]
                    })
                ]
            };
        }
    }
}
