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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    public sealed class SessionTransferAcknowledgementTests
    {
        public enum TransferSequenceSet
        {
            Empty,
            Consecutive,
            Gapped
        }

        [Test]
        [Combinatorial]
        public async Task TransferAcknowledgementsWaitForRequestedRepublishAsync(
            [Values] bool republishAfterTransfer,
            [Values] bool sequentialPublishing,
            [Values] TransferSequenceSet sequenceSet,
            [Values] bool changePreferenceDuringTransfer)
        {
            ArrayOf<uint> availableSequenceNumbers = sequenceSet switch
            {
                TransferSequenceSet.Empty => [],
                TransferSequenceSet.Consecutive => [1, 2],
                TransferSequenceSet.Gapped => [4, 1, 3],
                _ => throw new ArgumentOutOfRangeException(nameof(sequenceSet))
            };
            ArrayOf<uint> replayedSequenceNumbers = !republishAfterTransfer ? [] : sequenceSet switch
            {
                TransferSequenceSet.Empty => [],
                TransferSequenceSet.Consecutive => [1, 2],
                TransferSequenceSet.Gapped => [3, 4],
                _ => throw new ArgumentOutOfRangeException(nameof(sequenceSet))
            };
            ArrayOf<uint> immediateAcknowledgements = !republishAfterTransfer
                ? availableSequenceNumbers
                : sequenceSet == TransferSequenceSet.Gapped ? [1] : [];
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel originChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            ScriptedChannel targetChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            var originPublish = new AsyncOperationGate();
            var targetPublish = new AsyncOperationGate();
            var republish = new AsyncOperationGate();
            var finalPublish = new AsyncOperationGate();
            var firstRequest = new TaskCompletionSource<PublishRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRequest = new TaskCompletionSource<PublishRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var notificationsReceived = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var notifications = new ConcurrentQueue<DataChangeNotification>();
            int publishCount = 0;
            int republishCount = 0;
            int preferenceChanged = 0;

            originChannel.RequestHandler = async (request, ct) =>
            {
                switch (request)
                {
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = originChannel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case PublishRequest:
                        await originPublish.WaitAsync(ct).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = originChannel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    default:
                        return originChannel.CreateResponse(request);
                }
            };
            targetChannel.RequestHandler = async (request, ct) =>
            {
                switch (request)
                {
                    case CreateSessionRequest:
                        var created = (CreateSessionResponse)targetChannel.CreateResponse(request);
                        created.SessionId = new NodeId(2u, 1);
                        created.AuthenticationToken = new NodeId(102u, 1);
                        return created;
                    case TransferSubscriptionsRequest transfer:
                        return new TransferSubscriptionsResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            Results = transfer.SubscriptionIds.ConvertAll(_ => new TransferResult
                            {
                                StatusCode = StatusCodes.Good,
                                AvailableSequenceNumbers = availableSequenceNumbers
                            })
                        };
                    case RepublishRequest replay:
                        Interlocked.Increment(ref republishCount);
                        await republish.WaitAsync(ct).ConfigureAwait(false);
                        return new RepublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            NotificationMessage = new NotificationMessage
                            {
                                SequenceNumber = replay.RetransmitSequenceNumber,
                                NotificationData =
                                [
                                    new ExtensionObject(new DataChangeNotification
                                    {
                                        MonitoredItems =
                                        [
                                            new MonitoredItemNotification
                                            {
                                                ClientHandle = 1,
                                                Value = new DataValue(new Variant(replay.RetransmitSequenceNumber))
                                            }
                                        ]
                                    })
                                ]
                            }
                        };
                    case PublishRequest publish when Interlocked.Increment(ref publishCount) == 1:
                        firstRequest.TrySetResult(publish);
                        await targetPublish.WaitAsync(ct).ConfigureAwait(false);
                        return new PublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                            AvailableSequenceNumbers = replayedSequenceNumbers,
                            NotificationMessage = new NotificationMessage
                            {
                                SequenceNumber = sequenceSet switch
                                {
                                    TransferSequenceSet.Empty => 1,
                                    TransferSequenceSet.Consecutive => 3,
                                    TransferSequenceSet.Gapped => 5,
                                    _ => throw new ArgumentOutOfRangeException(nameof(sequenceSet))
                                }
                            }
                        };
                    case PublishRequest publish:
                        secondRequest.TrySetResult(publish);
                        await finalPublish.WaitAsync(ct).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    default:
                        return targetChannel.CreateResponse(request);
                }
            };

            await using var origin = new Session(
                originChannel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance);
            await using var target = new Session(
                targetChannel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance);
            target.MinPublishRequestCount = 1;
            target.MaxPublishRequestCount = 1;
            using var subscription = new Subscription(origin.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true,
                RepublishAfterTransfer = republishAfterTransfer,
                SequentialPublishing = sequentialPublishing,
                DisableMonitoredItemCache = true,
                FastDataChangeCallback = (_, notification, _) =>
                {
                    notifications.Enqueue(notification);
                    if (notifications.Count == replayedSequenceNumbers.Count)
                    {
                        notificationsReceived.TrySetResult(true);
                    }
                }
            };
            subscription.StateChanged += (_, args) =>
            {
                if (changePreferenceDuringTransfer &&
                    (args.Status & SubscriptionChangeMask.Transferred) != 0 &&
                    Interlocked.CompareExchange(ref preferenceChanged, 1, 0) == 0)
                {
                    subscription.RepublishAfterTransfer = !republishAfterTransfer;
                }
            };
            try
            {
                await origin.OpenAsync("origin", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
                await target.OpenAsync("target", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(origin.AddSubscription(subscription), Is.True);
                await subscription.CreateAsync().ConfigureAwait(false);
                Assert.That(
                    await target.TransferSubscriptionsAsync([subscription], false, CancellationToken.None)
                        .ConfigureAwait(false),
                    Is.True);
                Assert.That(subscription.AvailableSequenceNumbers.ToArray(),
                    Is.EqualTo(availableSequenceNumbers.ToArray()));
                Assert.That(preferenceChanged, Is.EqualTo(changePreferenceDuringTransfer ? 1 : 0));

                PublishRequest first = await firstRequest.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(
                    first.SubscriptionAcknowledgements.ToArray().Select(ack => ack.SequenceNumber),
                    Is.EquivalentTo(immediateAcknowledgements.ToArray()),
                    "Transfer must not acknowledge messages whose Republish responses are still blocked.");

                if (!replayedSequenceNumbers.IsEmpty)
                {
                    await republish.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(notifications, Is.Empty);
                    republish.Release();
                    await notificationsReceived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(notifications.Select(notification => notification.SequenceNumber),
                        Is.EquivalentTo(replayedSequenceNumbers.ToArray()));
                    foreach (DataChangeNotification notification in notifications)
                    {
                        Assert.That(notification.MonitoredItems.Count, Is.EqualTo(1));
                        Assert.That(notification.MonitoredItems[0].Value.WrappedValue,
                            Is.EqualTo(new Variant(notification.SequenceNumber)));
                    }
                    Assert.That(republishCount, Is.EqualTo(replayedSequenceNumbers.Count));
                }
                else
                {
                    Assert.That(republishCount, Is.Zero);
                    Assert.That(notifications, Is.Empty);
                }

                targetPublish.Release();
                PublishRequest next = await secondRequest.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(
                    next.SubscriptionAcknowledgements.ToArray().Select(ack => ack.SequenceNumber),
                    Is.EquivalentTo(replayedSequenceNumbers.ToArray()));
                Assert.That(
                    first.SubscriptionAcknowledgements.ToArray().Concat(next.SubscriptionAcknowledgements.ToArray())
                        .Select(ack => ack.SubscriptionId),
                    Is.EqualTo(Enumerable.Repeat(1u, availableSequenceNumbers.Count)));
            }
            finally
            {
                originPublish.Release();
                republish.Release();
                targetPublish.Release();
                finalPublish.Release();
            }
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);
    }
}
