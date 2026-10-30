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
                    first.SubscriptionAcknowledgements.ToArray()!.Select(ack => ack.SequenceNumber),
                    Is.EquivalentTo(immediateAcknowledgements.ToArray()!),
                    "Transfer must not acknowledge messages whose Republish responses are still blocked.");

                if (!replayedSequenceNumbers.IsEmpty)
                {
                    await republish.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(notifications, Is.Empty);
                    republish.Release();
                    await notificationsReceived.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                    Assert.That(notifications.Select(notification => notification.SequenceNumber),
                        Is.EquivalentTo(replayedSequenceNumbers.ToArray()!));
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
                    next.SubscriptionAcknowledgements.ToArray()!.Select(ack => ack.SequenceNumber),
                    Is.EquivalentTo(replayedSequenceNumbers.ToArray()!));
                Assert.That(
                    first.SubscriptionAcknowledgements.ToArray()!.Concat(next.SubscriptionAcknowledgements.ToArray()!)
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

        /// <summary>
        /// A source publish completing while transfer is suspended cannot retire an
        /// acknowledgement that has moved to the destination session.
        /// </summary>
        [Test]
        public async Task TransferHandsOffSourceAcknowledgementBeforeSourcePublishCompletesAsync()
        {
            await using var harness = new SessionChannelHarness();
            ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
            ScriptedChannel originChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            ScriptedChannel targetChannel = harness.CreateOpenedStandaloneChannel(endpoint);
            var sourcePublish = new AsyncOperationGate();
            var targetPublish = new AsyncOperationGate();
            var republish = new AsyncOperationGate();
            var sourceInFlight = new TaskCompletionSource<PublishRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var targetFirstPublish = new TaskCompletionSource<PublishRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var targetSecondPublish = new TaskCompletionSource<PublishRequest>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceDetached = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourcePublishCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var republishedNotification = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceObserver = new CollectingObserver();
            var targetObserver = new CollectingObserver();
            int originPublishCount = 0;
            int targetPublishCount = 0;

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
                    case PublishRequest publish when Interlocked.Increment(ref originPublishCount) == 1:
                        return new PublishResponse
                        {
                            ResponseHeader = originChannel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                            AvailableSequenceNumbers = [1],
                            NotificationMessage = new NotificationMessage()
                        };
                    case PublishRequest publish:
                        sourceInFlight.TrySetResult(publish);
                        await sourcePublish.WaitAsync(ct).ConfigureAwait(false);
                        sourcePublishCompleted.TrySetResult(true);
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
                                AvailableSequenceNumbers = [1]
                            })
                        };
                    case RepublishRequest replay:
                        await republish.WaitAsync(ct).ConfigureAwait(false);
                        return new RepublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            NotificationMessage = CreateNotificationMessage(replay.RetransmitSequenceNumber)
                        };
                    case PublishRequest publish when Interlocked.Increment(ref targetPublishCount) == 1:
                        targetFirstPublish.TrySetResult(publish);
                        await targetPublish.WaitAsync(ct).ConfigureAwait(false);
                        return new PublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                            AvailableSequenceNumbers = [1],
                            NotificationMessage = new NotificationMessage { SequenceNumber = 2 }
                        };
                    case PublishRequest publish:
                        targetSecondPublish.TrySetResult(publish);
                        return new PublishResponse
                        {
                            ResponseHeader = targetChannel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            Results = publish.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                            NotificationMessage = new NotificationMessage { SequenceNumber = 2 }
                        };
                    default:
                        return targetChannel.CreateResponse(request);
                }
            };

            await using var origin = new Session(
                originChannel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance)
            {
                Acknowledgements = new SessionAcknowledgements(
                    AcknowledgementMode.AfterConfirmation,
                    null,
                    sourceObserver),
                MinPublishRequestCount = 1,
                MaxPublishRequestCount = 1
            };
            await using var target = new Session(
                targetChannel.Channel, harness.Configuration, endpoint,
                engineFactory: ClassicSubscriptionEngineFactory.Instance)
            {
                Acknowledgements = new SessionAcknowledgements(
                    AcknowledgementMode.AfterConfirmation,
                    null,
                    targetObserver),
                MinPublishRequestCount = 1,
                MaxPublishRequestCount = 1
            };
            using var subscription = new Subscription(origin.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = true,
                RepublishAfterTransfer = true,
                DisableMonitoredItemCache = true,
                FastDataChangeCallback = (owner, notification, _) =>
                {
                    if (notification.SequenceNumber == 1 && ReferenceEquals(owner.Session, target))
                    {
                        republishedNotification.TrySetResult(true);
                    }
                }
            };
            try
            {
                await origin.OpenAsync("origin", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
                await target.OpenAsync("target", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(origin.AddSubscription(subscription), Is.True);
                origin.SubscriptionsChanged += (_, _) =>
                {
                    if (!origin.Subscriptions.Contains(subscription))
                    {
                        sourceDetached.TrySetResult(true);
                        sourcePublish.Release();
                    }
                };
                await subscription.CreateAsync().ConfigureAwait(false);

                PublishRequest inFlightSourceRequest =
                    await sourceInFlight.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(inFlightSourceRequest.SubscriptionAcknowledgements, Is.Empty,
                    "The in-flight source Publish predates this acknowledgement.");
                var handle = new AcknowledgementHandle(subscription.GetAcknowledgementScope(), 1);
                ClassicSubscriptionEngine sourceEngine =
                    (ClassicSubscriptionEngine)origin.SubscriptionEngine;
                sourceEngine.AddPendingAcknowledgement(subscription.Id, 1);
                origin.Acknowledgements!.Confirm(handle);

                Task<bool> transferTask =
                    target.TransferSubscriptionsAsync([subscription], false, CancellationToken.None);
                await sourceDetached.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                await sourcePublishCompleted.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(await transferTask.ConfigureAwait(false), Is.True);
                PublishRequest firstTargetRequest =
                    await targetFirstPublish.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(firstTargetRequest.SubscriptionAcknowledgements, Is.Empty,
                    "A source-tracked message reserved for republish cannot be acknowledged early.");
                await republish.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
                sourcePublish.Release();
                republish.Release();
                await republishedNotification.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                targetPublish.Release();

                PublishRequest secondTargetRequest =
                    await targetSecondPublish.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(
                    secondTargetRequest.SubscriptionAcknowledgements.ToArray()!
                        .Select(acknowledgement => acknowledgement.SequenceNumber),
                    Is.EqualTo([1u]),
                    "The carried confirmation makes the message eligible only after republish receipt.");
                await targetObserver.WaitForCompletionAsync().WaitAsync(s_timeout).ConfigureAwait(false);
                Assert.That(sourceObserver.Outcomes, Is.Empty,
                    "The source publish completion cannot end the transferred acknowledgement.");
                Assert.That(targetObserver.Outcomes.Single().Handle, Is.EqualTo(handle));
                Assert.That(
                    targetObserver.Outcomes.Single().Disposition,
                    Is.EqualTo(AcknowledgementDisposition.Acknowledged));
            }
            finally
            {
                sourcePublish.Release();
                republish.Release();
                targetPublish.Release();
            }
        }

        private static NotificationMessage CreateNotificationMessage(uint sequenceNumber)
        {
            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
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

        private sealed class CollectingObserver : ISubscriptionAcknowledgementObserver
        {
            public AcknowledgementOutcome[] Outcomes => [.. m_outcomes];

            public Task<bool> WaitForCompletionAsync() => m_completed.Task;

            public ValueTask OnCompletedAsync(
                ArrayOf<AcknowledgementOutcome> outcomes,
                CancellationToken ct = default)
            {
                for (int i = 0; i < outcomes.Count; i++)
                {
                    m_outcomes.Enqueue(outcomes[i]);
                }
                m_completed.TrySetResult(true);
                return ValueTask.CompletedTask;
            }

            private readonly ConcurrentQueue<AcknowledgementOutcome> m_outcomes = new();
            private readonly TaskCompletionSource<bool> m_completed = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

        [TestCase(OwnershipGuard.PreviousIsNull, TestName = "HandOverAcknowledgementsHonorsOwnershipGuards_PreviousIsNull")]
        [TestCase(OwnershipGuard.PreviousIsTarget, TestName = "HandOverAcknowledgementsHonorsOwnershipGuards_PreviousIsTarget")]
        [TestCase(OwnershipGuard.ReleasedListIsEmpty, TestName = "HandOverAcknowledgementsHonorsOwnershipGuards_ReleasedListIsEmpty")]
        [TestCase(OwnershipGuard.PreviousIsNonClassic, TestName = "HandOverAcknowledgementsHonorsOwnershipGuards_PreviousIsNonClassic")]
        public async Task HandOverAcknowledgementsHonorsOwnershipGuardsAsync(OwnershipGuard guard)
        {
            await using var fixture = await TransferFixture.CreateAsync().ConfigureAwait(false);
            using Subscription targetSubscription =
                await CreateSubscriptionAsync(fixture.Target).ConfigureAwait(false);
            var targetEngine = (ClassicSubscriptionEngine)fixture.Target.SubscriptionEngine;
            Session? previous = fixture.Origin;
            Subscription releasedSubscription = fixture.Subscription;
            Session? nonClassicPrevious = null;

            if (guard == OwnershipGuard.PreviousIsTarget)
            {
                previous = fixture.Target;
                releasedSubscription = targetSubscription;
            }

            List<AcknowledgementHandover> released;
            if (guard == OwnershipGuard.ReleasedListIsEmpty)
            {
                AddTrackedAcknowledgement(fixture.Origin, fixture.Subscription, 21, confirmed: true);
                released = [];
            }
            else if (guard is OwnershipGuard.PreviousIsNull or OwnershipGuard.PreviousIsNonClassic)
            {
                released =
                [
                    new AcknowledgementHandover(
                        new AcknowledgementHandle(fixture.Subscription.GetAcknowledgementScope(), 21),
                        Confirmed: true)
                ];
            }
            else
            {
                released = ReleaseAcknowledgements(
                    ReferenceEquals(previous, fixture.Target) ? fixture.Target : fixture.Origin,
                    releasedSubscription,
                    (21, true));
            }

            if (guard == OwnershipGuard.PreviousIsNull)
            {
                previous = null!;
            }
            else if (guard == OwnershipGuard.PreviousIsNonClassic)
            {
                nonClassicPrevious = CreateNonClassicSession(fixture.Harness, fixture.Endpoint);
                previous = nonClassicPrevious;
            }

            try
            {
                fixture.Target.HandOverAcknowledgements(
                    previous,
                    targetSubscription,
                    released,
                    [21u],
                    [21u]);

                var targetProvider =
                    (ConfirmedAcknowledgementProvider)fixture.TargetAcknowledgements!.Provider!;
                Assert.That(targetProvider.TrackedAcknowledgements, Is.Zero,
                    "Every ownership guard must leave the target acknowledgement tracker untouched.");
                Assert.That(targetProvider.PendingConfirmations, Is.Zero);
                Assert.That(targetEngine.RemoveAcknowledgementsForSubscription(targetSubscription.Id), Is.Zero);
                Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
                Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);

                var sourceProvider =
                    (ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!;
                Assert.That(
                    sourceProvider.TrackedAcknowledgements,
                    Is.EqualTo(guard == OwnershipGuard.ReleasedListIsEmpty ? 1 : 0),
                    "An empty handover is a no-op; the other cases have already released the source handle.");
                Assert.That(
                    sourceProvider.PendingConfirmations,
                    Is.EqualTo(guard == OwnershipGuard.ReleasedListIsEmpty ? 1 : 0));
            }
            finally
            {
                if (nonClassicPrevious != null)
                {
                    await nonClassicPrevious.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task HandOverAcknowledgementsAdoptsTrackedHandlesIntoClassicEngineAsync()
        {
            await using var fixture = await TransferFixture.CreateAsync().ConfigureAwait(false);
            using Subscription targetSubscription =
                await CreateSubscriptionAsync(fixture.Target).ConfigureAwait(false);
            var sourceEngine = (ClassicSubscriptionEngine)fixture.Origin.SubscriptionEngine;
            var targetEngine = (ClassicSubscriptionEngine)fixture.Target.SubscriptionEngine;
            AcknowledgementScope sourceScope = fixture.Subscription.GetAcknowledgementScope();
            var confirmedSourceHandle = new AcknowledgementHandle(sourceScope, 31);
            var unconfirmedSourceHandle = new AcknowledgementHandle(sourceScope, 32);
            var unavailableSourceHandle = new AcknowledgementHandle(sourceScope, 33);
            List<AcknowledgementHandover> released = ReleaseAcknowledgements(
                fixture.Origin,
                fixture.Subscription,
                (31, true),
                (32, false),
                (33, true));
            AcknowledgementScope targetScope = targetSubscription.GetAcknowledgementScope();
            var confirmedTargetHandle = new AcknowledgementHandle(targetScope, 31);
            var unconfirmedTargetHandle = new AcknowledgementHandle(targetScope, 32);

            fixture.Target.HandOverAcknowledgements(
                fixture.Origin,
                targetSubscription,
                released,
                [31u, 32u],
                [31u, 32u]);

            await fixture.TargetObserver.WaitForCompletionAsync()
                .WaitAsync(s_timeout).ConfigureAwait(false);
            var targetProvider =
                (ConfirmedAcknowledgementProvider)fixture.TargetAcknowledgements!.Provider!;
            AcknowledgementDecision decision = await targetProvider.DecideAsync(
                new[] { confirmedTargetHandle, unconfirmedTargetHandle }.ToArrayOf()).ConfigureAwait(false);

            Assert.That(released, Has.Count.EqualTo(3));
            List<AcknowledgementHandle> expectedSourceHandles =
            [
                confirmedSourceHandle,
                unconfirmedSourceHandle,
                unavailableSourceHandle
            ];
            Assert.That(released.Select(item => item.Handle), Is.EquivalentTo(expectedSourceHandles));
            Assert.That(released.Single(item => item.Handle == confirmedSourceHandle).Confirmed, Is.True);
            Assert.That(released.Single(item => item.Handle == unconfirmedSourceHandle).Confirmed, Is.False);
            Assert.That(released.Single(item => item.Handle == unavailableSourceHandle).Confirmed, Is.True);
            Assert.That(targetProvider.TrackedAcknowledgements, Is.EqualTo(2));
            Assert.That(targetProvider.PendingConfirmations, Is.EqualTo(1));
            Assert.That(decision.Deferred.ToArray(), Is.EqualTo(s_deferredSecondIndex),
                "Only the retained handle that was confirmed at the source remains eligible.");
            Assert.That(targetEngine.RemoveAcknowledgementsForSubscription(targetSubscription.Id), Is.EqualTo(2),
                "Both retained handles are queued on the destination subscription.");
            Assert.That(fixture.TargetObserver.Outcomes, Has.Length.EqualTo(1));
            Assert.That(fixture.TargetObserver.Outcomes[0].Handle, Is.EqualTo(unavailableSourceHandle));
            Assert.That(fixture.TargetObserver.Outcomes[0].Disposition,
                Is.EqualTo(AcknowledgementDisposition.NotRetained));
            Assert.That(fixture.TargetObserver.Outcomes[0].Status, Is.EqualTo(StatusCodes.BadMessageNotAvailable));
            Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).TrackedAcknowledgements,
                Is.Zero);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).PendingConfirmations,
                Is.Zero);
        }

        [Test]
        public async Task HandOverAcknowledgementsRetiresWhenTargetLacksTrackingAsync()
        {
            await using var fixture = await TransferFixture.CreateAsync(
                configureTargetAcknowledgements: false).ConfigureAwait(false);
            List<AcknowledgementHandover> released = ReleaseAcknowledgements(
                fixture.Origin,
                fixture.Subscription,
                (41, true),
                (42, false));
            AcknowledgementScope scope = fixture.Subscription.GetAcknowledgementScope();

            fixture.Target.HandOverAcknowledgements(
                fixture.Origin,
                fixture.Subscription,
                released,
                [41u, 42u],
                [41u, 42u]);

            await fixture.SourceObserver.WaitForCompletionAsync()
                .WaitAsync(s_timeout).ConfigureAwait(false);
            AssertRetiredHandover(
                fixture.SourceObserver.Outcomes,
                new AcknowledgementHandle(scope, 41),
                new AcknowledgementHandle(scope, 42));
            Assert.That(fixture.Target.Acknowledgements, Is.Null);
            Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).TrackedAcknowledgements,
                Is.Zero);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).PendingConfirmations,
                Is.Zero);
        }

        [Test]
        public async Task HandOverAcknowledgementsRetiresForNonClassicTargetAsync()
        {
            await using var fixture = await TransferFixture.CreateAsync().ConfigureAwait(false);
            List<AcknowledgementHandover> released = ReleaseAcknowledgements(
                fixture.Origin,
                fixture.Subscription,
                (51, true),
                (52, false));
            AcknowledgementScope scope = fixture.Subscription.GetAcknowledgementScope();
            await using var nonClassicTarget = CreateNonClassicSession(fixture.Harness, fixture.Endpoint);

            nonClassicTarget.HandOverAcknowledgements(
                fixture.Origin,
                fixture.Subscription,
                released,
                [51u, 52u],
                [51u, 52u]);

            await fixture.SourceObserver.WaitForCompletionAsync()
                .WaitAsync(s_timeout).ConfigureAwait(false);
            AssertRetiredHandover(
                fixture.SourceObserver.Outcomes,
                new AcknowledgementHandle(scope, 51),
                new AcknowledgementHandle(scope, 52));
            Assert.That(nonClassicTarget.SubscriptionEngine, Is.Not.InstanceOf<ClassicSubscriptionEngine>());
            Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).TrackedAcknowledgements,
                Is.Zero);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).PendingConfirmations,
                Is.Zero);
        }

        [Test]
        public async Task CancelledTransferWaitDoesNotStartServiceRequestAsync()
        {
            await using var fixture = await TransferFixture.CreateAsync().ConfigureAwait(false);
            AddTrackedAcknowledgement(fixture.Origin, fixture.Subscription, 61, confirmed: true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await fixture.Target.TransferSubscriptionsAsync(
                    [fixture.Subscription],
                    false,
                    cancellation.Token).ConfigureAwait(false));

            Assert.That(fixture.TransferRequestCount, Is.Zero,
                "Cancellation while waiting for the reconnect lock must happen before service dispatch.");
            AssertSourceStateUnchanged(fixture, 1, 1);
            Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
            Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);
        }

        [TestCase(TransferFailure.ServiceResult, TestName = "FailedTransferReleasesReconnectLockAndPreservesSourceState_ServiceResult")]
        [TestCase(TransferFailure.SubscriptionResult, TestName = "FailedTransferReleasesReconnectLockAndPreservesSourceState_SubscriptionResult")]
        [TestCase(TransferFailure.ServiceException, TestName = "FailedTransferReleasesReconnectLockAndPreservesSourceState_ServiceException")]
        public async Task FailedTransferReleasesReconnectLockAndPreservesSourceStateAsync(
            TransferFailure failure)
        {
            await using var fixture = await TransferFixture.CreateAsync(
                async (channel, request, attempt, _) =>
                {
                    if (attempt == 1 && failure == TransferFailure.ServiceException)
                    {
                        throw new ServiceResultException(StatusCodes.BadCommunicationError);
                    }

                    ResponseHeader header = channel.CreateGoodHeader();
                    StatusCode result = StatusCodes.Good;
                    if (attempt == 1 && failure == TransferFailure.ServiceResult)
                    {
                        header.ServiceResult = StatusCodes.BadCommunicationError;
                    }
                    else if (attempt == 1 && failure == TransferFailure.SubscriptionResult)
                    {
                        result = StatusCodes.BadSubscriptionIdInvalid;
                    }
                    return new TransferSubscriptionsResponse
                    {
                        ResponseHeader = header,
                        Results = request.SubscriptionIds.ConvertAll(_ => new TransferResult
                        {
                            StatusCode = result,
                            AvailableSequenceNumbers = [61]
                        })
                    };
                }).ConfigureAwait(false);
            AddTrackedAcknowledgement(fixture.Origin, fixture.Subscription, 61, confirmed: true);

            Assert.That(
                await fixture.Target.TransferSubscriptionsAsync(
                    [fixture.Subscription],
                    false,
                    CancellationToken.None).ConfigureAwait(false),
                Is.False);
            Assert.That(fixture.TransferRequestCount, Is.EqualTo(1));
            AssertSourceStateUnchanged(fixture, 1, 1);
            Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
            Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);

            Assert.That(
                await fixture.Target.TransferSubscriptionsAsync(
                    [fixture.Subscription],
                    false,
                    CancellationToken.None).WaitAsync(s_timeout).ConfigureAwait(false),
                Is.True, "A fresh valid transfer proves the failed operation released reconnect state.");
            Assert.That(fixture.TransferRequestCount, Is.EqualTo(2));
            Assert.That(fixture.Target.Subscriptions.Contains(fixture.Subscription), Is.True);
            Assert.That(fixture.Origin.Subscriptions.Contains(fixture.Subscription), Is.False);
            Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);
        }

        [Test]
        public async Task SourceSubscriptionChangeExceptionRetiresReleasedAcknowledgementsAsync()
        {
            await using var fixture = await TransferFixture.CreateAsync().ConfigureAwait(false);
            AddTrackedAcknowledgement(fixture.Origin, fixture.Subscription, 61, confirmed: true);
            AcknowledgementScope scope = fixture.Subscription.GetAcknowledgementScope();
            fixture.Origin.SubscriptionsChanged += (_, _) =>
                throw new InvalidOperationException("Test callback failure.");

            Assert.That(
                await fixture.Target.TransferSubscriptionsAsync(
                    [fixture.Subscription],
                    false,
                    CancellationToken.None).WaitAsync(s_timeout).ConfigureAwait(false),
                Is.False);
            await fixture.SourceObserver.WaitForCompletionAsync()
                .WaitAsync(s_timeout).ConfigureAwait(false);

            AssertRetiredHandover(
                fixture.SourceObserver.Outcomes,
                new AcknowledgementHandle(scope, 61));
            Assert.That(fixture.Origin.Subscriptions.Contains(fixture.Subscription), Is.False);
            Assert.That(fixture.Target.Subscriptions.Contains(fixture.Subscription), Is.False);
            Assert.That(
                ((ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!).TrackedAcknowledgements,
                Is.Zero);
            Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
        }

        [Test]
        public async Task CancelledInProgressTransferReleasesReconnectLockAsync()
        {
            var requestEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var allowRequestToFinish = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var fixture = await TransferFixture.CreateAsync(
                async (channel, request, attempt, ct) =>
                {
                    if (attempt == 1)
                    {
                        requestEntered.TrySetResult(true);
                        await allowRequestToFinish.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    return CreateGoodTransferResponse(channel, request);
                }).ConfigureAwait(false);
            AddTrackedAcknowledgement(fixture.Origin, fixture.Subscription, 71, confirmed: true);
            using var cancellation = new CancellationTokenSource();

            try
            {
                Task<bool> interruptedTransfer = fixture.Target.TransferSubscriptionsAsync(
                    [fixture.Subscription],
                    false,
                    cancellation.Token);
                await requestEntered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                cancellation.Cancel();

                Assert.That(await interruptedTransfer.WaitAsync(s_timeout).ConfigureAwait(false), Is.False,
                    "Cancellation inside the transfer core is caught and reported as a failed transfer.");
                Assert.That(fixture.TransferRequestCount, Is.EqualTo(1));
                AssertSourceStateUnchanged(fixture, 1, 1);
                Assert.That(fixture.TargetObserver.Outcomes, Is.Empty);
                Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);

                Assert.That(
                    await fixture.Target.TransferSubscriptionsAsync(
                        [fixture.Subscription],
                        false,
                        CancellationToken.None).WaitAsync(s_timeout).ConfigureAwait(false),
                    Is.True, "A fresh transfer proves cancellation released the reconnect lock.");
                Assert.That(fixture.TransferRequestCount, Is.EqualTo(2));
                Assert.That(fixture.Target.Subscriptions.Contains(fixture.Subscription), Is.True);
                Assert.That(fixture.Origin.Subscriptions.Contains(fixture.Subscription), Is.False);
                Assert.That(fixture.SourceObserver.Outcomes, Is.Empty);
            }
            finally
            {
                cancellation.Cancel();
                allowRequestToFinish.TrySetResult(true);
            }
        }

        private static async Task<Subscription> CreateSubscriptionAsync(Session session)
        {
            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                PublishingEnabled = false,
                DisableMonitoredItemCache = true,
                RepublishAfterTransfer = false
            };
            Assert.That(session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);
            return subscription;
        }

        private static void AddTrackedAcknowledgement(
            Session session,
            Subscription subscription,
            uint sequenceNumber,
            bool confirmed)
        {
            var engine = (ClassicSubscriptionEngine)session.SubscriptionEngine;
            engine.AddPendingAcknowledgement(subscription.Id, sequenceNumber);
            if (confirmed)
            {
                session.Acknowledgements!.Confirm(
                    new AcknowledgementHandle(subscription.GetAcknowledgementScope(), sequenceNumber));
            }
        }

        private static List<AcknowledgementHandover> ReleaseAcknowledgements(
            Session session,
            Subscription subscription,
            params (uint SequenceNumber, bool Confirmed)[] acknowledgements)
        {
            foreach ((uint sequenceNumber, bool confirmed) in acknowledgements)
            {
                AddTrackedAcknowledgement(session, subscription, sequenceNumber, confirmed);
            }
            return ((ClassicSubscriptionEngine)session.SubscriptionEngine)
                .ReleaseAcknowledgementsOf(subscription);
        }

        private static void AssertSourceStateUnchanged(
            TransferFixture fixture,
            int expectedTracked,
            int expectedPendingConfirmations)
        {
            Assert.That(fixture.Origin.Subscriptions.Contains(fixture.Subscription), Is.True,
                "The failed transfer must leave the original subscription owner unchanged.");
            Assert.That(fixture.Target.Subscriptions.Contains(fixture.Subscription), Is.False);
            var provider =
                (ConfirmedAcknowledgementProvider)fixture.SourceAcknowledgements.Provider!;
            Assert.That(provider.TrackedAcknowledgements, Is.EqualTo(expectedTracked));
            Assert.That(provider.PendingConfirmations, Is.EqualTo(expectedPendingConfirmations));
        }

        private static void AssertRetiredHandover(
            AcknowledgementOutcome[] outcomes,
            params AcknowledgementHandle[] expectedHandles)
        {
            Assert.That(outcomes.Select(outcome => outcome.Handle), Is.EquivalentTo(expectedHandles));
            Assert.That(outcomes, Has.Length.EqualTo(expectedHandles.Length));
            foreach (AcknowledgementOutcome outcome in outcomes)
            {
                Assert.That(outcome.Disposition, Is.EqualTo(AcknowledgementDisposition.OwnerRetired));
                Assert.That(outcome.Status, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            }
        }

        private static Session CreateNonClassicSession(
            SessionChannelHarness harness,
            ConfiguredEndpoint endpoint)
        {
            var engine = new Moq.Mock<ISubscriptionEngine>(Moq.MockBehavior.Loose);
            var factory = new Moq.Mock<ISubscriptionEngineFactory>(Moq.MockBehavior.Strict);
            factory.Setup(value => value.Create(Moq.It.IsAny<ISubscriptionEngineContext>()))
                .Returns(engine.Object);
            ScriptedChannel channel = harness.CreateOpenedStandaloneChannel(endpoint);
            return new Session(
                channel.Channel,
                harness.Configuration,
                endpoint,
                engineFactory: factory.Object);
        }

        private static TransferSubscriptionsResponse CreateGoodTransferResponse(
            ScriptedChannel channel,
            TransferSubscriptionsRequest request)
        {
            return new TransferSubscriptionsResponse
            {
                ResponseHeader = channel.CreateGoodHeader(),
                Results = request.SubscriptionIds.ConvertAll(_ => new TransferResult
                {
                    StatusCode = StatusCodes.Good,
                    AvailableSequenceNumbers = [61, 71]
                })
            };
        }

        public enum OwnershipGuard
        {
            PreviousIsNull,
            PreviousIsTarget,
            ReleasedListIsEmpty,
            PreviousIsNonClassic
        }

        public enum TransferFailure
        {
            ServiceResult,
            SubscriptionResult,
            ServiceException
        }

        private static readonly int[] s_deferredSecondIndex = [1];

        private sealed class TransferFixture : IAsyncDisposable
        {
            private readonly SessionChannelHarness m_harness;
            private readonly int[] m_transferRequestCount;

            private TransferFixture(
                SessionChannelHarness harness,
                ConfiguredEndpoint endpoint,
                ScriptedChannel originChannel,
                ScriptedChannel targetChannel,
                SessionAcknowledgements sourceAcknowledgements,
                SessionAcknowledgements? targetAcknowledgements,
                Session origin,
                Session target,
                Subscription subscription,
                CollectingObserver sourceObserver,
                CollectingObserver targetObserver,
                int[] transferRequestCount)
            {
                m_harness = harness;
                m_transferRequestCount = transferRequestCount;
                Endpoint = endpoint;
                OriginChannel = originChannel;
                TargetChannel = targetChannel;
                SourceAcknowledgements = sourceAcknowledgements;
                TargetAcknowledgements = targetAcknowledgements;
                Origin = origin;
                Target = target;
                Subscription = subscription;
                SourceObserver = sourceObserver;
                TargetObserver = targetObserver;
            }

            public ConfiguredEndpoint Endpoint { get; }
            public ScriptedChannel OriginChannel { get; }
            public ScriptedChannel TargetChannel { get; }
            public SessionAcknowledgements SourceAcknowledgements { get; }
            public SessionAcknowledgements? TargetAcknowledgements { get; }
            public Session Origin { get; }
            public Session Target { get; }
            public Subscription Subscription { get; }
            public CollectingObserver SourceObserver { get; }
            public CollectingObserver TargetObserver { get; }
            public SessionChannelHarness Harness => m_harness;
            public int TransferRequestCount => Volatile.Read(ref m_transferRequestCount[0]);

            public static async Task<TransferFixture> CreateAsync(
                TransferResponseHandler? transferHandler = null,
                bool configureTargetAcknowledgements = true)
            {
                var harness = new SessionChannelHarness();
                ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
                ScriptedChannel originChannel = harness.CreateOpenedStandaloneChannel(endpoint);
                ScriptedChannel targetChannel = harness.CreateOpenedStandaloneChannel(endpoint);
                var sourceObserver = new CollectingObserver();
                var targetObserver = new CollectingObserver();
                var sourceAcknowledgements = new SessionAcknowledgements(
                    AcknowledgementMode.AfterConfirmation,
                    null,
                    sourceObserver);
                SessionAcknowledgements? targetAcknowledgements = configureTargetAcknowledgements
                    ? new SessionAcknowledgements(AcknowledgementMode.AfterConfirmation, null, targetObserver)
                    : null;
                var transferRequestCount = new int[1];

                originChannel.RequestHandler = (request, _) =>
                {
                    if (request is CreateSubscriptionRequest create)
                    {
                        return ValueTask.FromResult<IServiceResponse>(
                            CreateSubscriptionResponse(originChannel, create));
                    }
                    return ValueTask.FromResult(originChannel.CreateResponse(request));
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
                        case CreateSubscriptionRequest create:
                            return CreateSubscriptionResponse(targetChannel, create);
                        case TransferSubscriptionsRequest transfer:
                            int attempt = Interlocked.Increment(ref transferRequestCount[0]);
                            return transferHandler == null
                                ? CreateGoodTransferResponse(targetChannel, transfer)
                                : await transferHandler(
                                    targetChannel,
                                    transfer,
                                    attempt,
                                    ct).ConfigureAwait(false);
                        default:
                            return targetChannel.CreateResponse(request);
                    }
                };

                var origin = new Session(
                    originChannel.Channel,
                    harness.Configuration,
                    endpoint,
                    engineFactory: ClassicSubscriptionEngineFactory.Instance)
                {
                    Acknowledgements = sourceAcknowledgements,
                    MinPublishRequestCount = 1,
                    MaxPublishRequestCount = 1
                };
                var target = new Session(
                    targetChannel.Channel,
                    harness.Configuration,
                    endpoint,
                    engineFactory: ClassicSubscriptionEngineFactory.Instance)
                {
                    Acknowledgements = targetAcknowledgements,
                    MinPublishRequestCount = 1,
                    MaxPublishRequestCount = 1
                };

                await origin.OpenAsync("origin", new UserIdentity(), CancellationToken.None)
                    .ConfigureAwait(false);
                await target.OpenAsync("target", new UserIdentity(), CancellationToken.None)
                    .ConfigureAwait(false);
                Subscription subscription = await CreateSubscriptionAsync(origin).ConfigureAwait(false);
                return new TransferFixture(
                    harness,
                    endpoint,
                    originChannel,
                    targetChannel,
                    sourceAcknowledgements,
                    targetAcknowledgements,
                    origin,
                    target,
                    subscription,
                    sourceObserver,
                    targetObserver,
                    transferRequestCount);
            }

            public async ValueTask DisposeAsync()
            {
                Subscription.Dispose();
                await Target.DisposeAsync().ConfigureAwait(false);
                await Origin.DisposeAsync().ConfigureAwait(false);
                await m_harness.DisposeAsync().ConfigureAwait(false);
            }

            private static CreateSubscriptionResponse CreateSubscriptionResponse(
                ScriptedChannel channel,
                CreateSubscriptionRequest request)
            {
                return new CreateSubscriptionResponse
                {
                    ResponseHeader = channel.CreateGoodHeader(),
                    SubscriptionId = 1,
                    RevisedPublishingInterval = request.RequestedPublishingInterval,
                    RevisedLifetimeCount = request.RequestedLifetimeCount,
                    RevisedMaxKeepAliveCount = request.RequestedMaxKeepAliveCount
                };
            }

            public delegate Task<IServiceResponse> TransferResponseHandler(
                ScriptedChannel channel,
                TransferSubscriptionsRequest request,
                int attempt,
                CancellationToken ct);
        }
    }
}
