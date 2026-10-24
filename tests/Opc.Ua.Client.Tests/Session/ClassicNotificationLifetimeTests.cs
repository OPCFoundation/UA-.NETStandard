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
 *
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

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Covers notification lifetimes on the classic engine: stamping on delivery, resolution in the
    /// acknowledgement callback, and every terminal disposition.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    public sealed class ClassicNotificationLifetimeTests
    {
        /// <summary>
        /// The lifetime obtained in the notification callback is the one the acknowledgement callback
        /// resolves, and it ends once as acknowledged when the server confirms it.
        /// </summary>
        [Test]
        public async Task DeliveredLifetimeIsResolvedAndAcknowledgedAsync()
        {
            await using var fixture = new LifetimeFixture();
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            fixture.Respond(fixture.KeepAlive(2, available: []));
            await fixture.StartAsync().ConfigureAwait(false);

            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            NotificationTermination terminated = await fixture.NextTerminationAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(delivered.IsNull, Is.False);
                Assert.That(delivered.SubscriptionId, Is.EqualTo(1));
                Assert.That(delivered.SequenceNumber, Is.EqualTo(1));
                Assert.That(delivered.Provenance, Is.EqualTo(NotificationProvenance.Created));
                Assert.That(fixture.Presented, Has.Some.EqualTo(delivered));
                Assert.That(terminated, Is.EqualTo(
                    new NotificationTermination(delivered, NotificationDisposition.Acknowledged)));
            });
        }

        /// <summary>
        /// A deferred acknowledgement whose message the server no longer lists ends as not retained,
        /// without waiting for the subscription to be deleted.
        /// </summary>
        [Test]
        public async Task EvictedDeferredMessageEndsAsNotRetainedAsync()
        {
            await using var fixture = new LifetimeFixture { Defer = true };
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            fixture.Respond(fixture.DataMessage(30, available: [30]));
            await fixture.StartAsync().ConfigureAwait(false);

            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            NotificationTermination terminated = await fixture.NextTerminationAsync().ConfigureAwait(false);

            Assert.That(terminated, Is.EqualTo(
                new NotificationTermination(delivered, NotificationDisposition.NotRetained)));
            Assert.That(fixture.SentAcknowledgements.Select(ack => ack.SequenceNumber), Has.None.EqualTo(1));
        }

        /// <summary>
        /// When the server reuses the subscription id for a new subscription, a deferred
        /// acknowledgement of the old lifetime is never sent and ends as owner retired.
        /// </summary>
        [Test]
        public async Task ReusedIdRetiresTheDeferredLifetimeAsync()
        {
            await using var fixture = new LifetimeFixture { Defer = true };
            var reuse = new AsyncOperationGate();
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            fixture.Respond(fixture.KeepAlive(2, available: [1]), reuse);
            await fixture.StartAsync().ConfigureAwait(false);
            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            await reuse.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
            await fixture.CreateSecondSubscriptionAsync().ConfigureAwait(false);
            reuse.Release();
            NotificationTermination terminated = await fixture.NextTerminationAsync().ConfigureAwait(false);

            Assert.That(terminated, Is.EqualTo(
                new NotificationTermination(delivered, NotificationDisposition.OwnerRetired)));
            Assert.That(fixture.Session.StampNotificationLifetime(1, 1), Is.Not.EqualTo(delivered));
        }

        /// <summary>
        /// An acknowledgement the policy drops from both lists ends as abandoned instead of being
        /// tracked forever.
        /// </summary>
        [Test]
        public async Task DroppedAcknowledgementEndsAsAbandonedAsync()
        {
            await using var fixture = new LifetimeFixture { Drop = true };
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            fixture.Respond(fixture.KeepAlive(2, available: [1]));
            await fixture.StartAsync().ConfigureAwait(false);

            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            NotificationTermination terminated = await fixture.NextTerminationAsync().ConfigureAwait(false);

            Assert.That(terminated, Is.EqualTo(
                new NotificationTermination(delivered, NotificationDisposition.Abandoned)));
        }

        /// <summary>
        /// Two acknowledgement objects for the same message (a re-ack after a transfer) share one
        /// lifetime: the server confirms one and reports the copy unknown, and the lifetime is
        /// reported once, as acknowledged.
        /// </summary>
        [Test]
        public async Task DuplicateAcknowledgementReportsTheLifetimeOnceAsync()
        {
            await using var fixture = new LifetimeFixture { Defer = true };
            var release = new AsyncOperationGate();
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            fixture.Respond(fixture.KeepAlive(2, available: [1]), release);
            fixture.Respond(fixture.KeepAlive(3, available: []), firstGoodOnly: true);
            fixture.Respond(fixture.KeepAlive(4, available: []));
            await fixture.StartAsync().ConfigureAwait(false);
            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            await release.Entered.WaitAsync(s_timeout).ConfigureAwait(false);
            ((ClassicSubscriptionEngine)fixture.Session.SubscriptionEngine).AddPendingAcknowledgement(1, 1);
            fixture.Defer = false;
            release.Release();
            NotificationTermination terminated = await fixture.NextTerminationAsync().ConfigureAwait(false);
            await fixture.PublishHeld.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            Assert.That(terminated, Is.EqualTo(
                new NotificationTermination(delivered, NotificationDisposition.Acknowledged)));
            Assert.That(fixture.SentAcknowledgements.Count(ack => ack.SequenceNumber == 1), Is.EqualTo(2));
            Assert.That(fixture.Terminations, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Disposing the session ends the lifetimes still pending as owner retired.
        /// </summary>
        [Test]
        public async Task DisposeRetiresPendingLifetimesAsync()
        {
            await using var fixture = new LifetimeFixture { Defer = true };
            fixture.Respond(fixture.DataMessage(1, available: [1]));
            await fixture.StartAsync().ConfigureAwait(false);
            NotificationLifetime delivered = await fixture.Delivered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
            await fixture.PublishHeld.Task.WaitAsync(s_timeout).ConfigureAwait(false);

            await fixture.Session.DisposeAsync().ConfigureAwait(false);

            Assert.That(fixture.Terminations, Has.Some.EqualTo(
                new NotificationTermination(delivered, NotificationDisposition.OwnerRetired)));
        }

        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// A classic session on a scripted channel that answers Publish requests in order.
        /// </summary>
        private sealed class LifetimeFixture : IAsyncDisposable
        {
            public LifetimeFixture()
            {
                m_harness = new SessionChannelHarness();
                ConfiguredEndpoint endpoint = SessionChannelHarness.CreateEndpoint();
                m_channel = m_harness.CreateOpenedStandaloneChannel(endpoint);
                m_channel.RequestHandler = HandleAsync;
                Session = new Session(
                    m_channel.Channel, m_harness.Configuration, endpoint,
                    engineFactory: ClassicSubscriptionEngineFactory.Instance)
                {
                    MinPublishRequestCount = 1,
                    MaxPublishRequestCount = 1
                };
                Session.PublishSequenceNumbersToAcknowledge += OnAcknowledge;
                Session.NotificationsTerminated += (_, args) =>
                {
                    foreach (NotificationTermination termination in args.Terminations)
                    {
                        m_terminations.Enqueue(termination);
                        m_terminated.Writer.TryWrite(termination);
                    }
                };
            }

            /// <summary>
            /// Gets the session under test.
            /// </summary>
            public Session Session { get; }

            /// <summary>
            /// Gets or sets whether the acknowledgement policy defers every acknowledgement.
            /// </summary>
            public bool Defer
            {
                get => Volatile.Read(ref m_defer);
                set => Volatile.Write(ref m_defer, value);
            }

            /// <summary>
            /// Gets or sets whether the acknowledgement policy drops every acknowledgement.
            /// </summary>
            public bool Drop { get; init; }

            /// <summary>
            /// Gets the lifetime the first data notification callback obtained.
            /// </summary>
            public TaskCompletionSource<NotificationLifetime> Delivered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets a task completed when a Publish request waits for a scripted answer.
            /// </summary>
            public TaskCompletionSource<bool> PublishHeld { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the lifetimes resolved in the acknowledgement callback.
            /// </summary>
            public ConcurrentQueue<NotificationLifetime> Presented { get; } = new();

            /// <summary>
            /// Gets the acknowledgements Publish requests carried.
            /// </summary>
            public ConcurrentQueue<SubscriptionAcknowledgement> SentAcknowledgements { get; } = new();

            /// <summary>
            /// Gets every reported termination.
            /// </summary>
            public IReadOnlyCollection<NotificationTermination> Terminations => m_terminations;

            /// <summary>
            /// Queues the answer to the next Publish request, optionally held at a gate. With
            /// <paramref name="firstGoodOnly"/> the server confirms only the first acknowledgement
            /// of a message and reports later copies unknown.
            /// </summary>
            public void Respond(
                Func<PublishRequest, PublishResponse> response,
                AsyncOperationGate? gate = null,
                bool firstGoodOnly = false)
            {
                m_responses.Enqueue((response, gate, firstGoodOnly));
            }

            /// <summary>
            /// Answers with a data change message of subscription 1.
            /// </summary>
            public Func<PublishRequest, PublishResponse> DataMessage(uint sequenceNumber, ArrayOf<uint> available)
            {
                return request => new PublishResponse
                {
                    ResponseHeader = m_channel.CreateGoodHeader(),
                    SubscriptionId = 1,
                    AvailableSequenceNumbers = available,
                    Results = request.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                    NotificationMessage = new NotificationMessage
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
                    }
                };
            }

            /// <summary>
            /// Answers with a keep-alive of subscription 1.
            /// </summary>
            public Func<PublishRequest, PublishResponse> KeepAlive(uint sequenceNumber, ArrayOf<uint> available)
            {
                return request => new PublishResponse
                {
                    ResponseHeader = m_channel.CreateGoodHeader(),
                    SubscriptionId = 1,
                    AvailableSequenceNumbers = available,
                    Results = request.SubscriptionAcknowledgements.ConvertAll(_ => StatusCodes.Good),
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = sequenceNumber,
                        PublishTime = DateTime.UtcNow
                    }
                };
            }

            /// <summary>
            /// Opens the session and creates subscription 1, which starts publishing.
            /// </summary>
            public async Task StartAsync()
            {
                await Session.OpenAsync("lifetimes", new UserIdentity(), CancellationToken.None).ConfigureAwait(false);
                m_subscription = CreateSubscription();
                m_subscription.FastDataChangeCallback = (subscription, notification, _) =>
                {
                    if (subscription.TryGetNotificationLifetime(
                        notification.SequenceNumber, out NotificationLifetime lifetime))
                    {
                        Delivered.TrySetResult(lifetime);
                    }
                };
                Assert.That(Session.AddSubscription(m_subscription), Is.True);
                await m_subscription.CreateAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Creates another subscription, which the server gives the reused id 1.
            /// </summary>
            public async Task CreateSecondSubscriptionAsync()
            {
                m_second = CreateSubscription();
                Assert.That(Session.AddSubscription(m_second), Is.True);
                await m_second.CreateAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// Waits for the next reported termination.
            /// </summary>
            public async Task<NotificationTermination> NextTerminationAsync()
            {
                using var timeout = new CancellationTokenSource(s_timeout);
                return await m_terminated.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_shutdown.Cancel();
                m_subscription?.Dispose();
                m_second?.Dispose();
                await Session.DisposeAsync().ConfigureAwait(false);
                await m_harness.DisposeAsync().ConfigureAwait(false);
                m_shutdown.Dispose();
            }

            private Subscription CreateSubscription()
            {
                return new Subscription(Session.DefaultSubscription)
                {
                    PublishingInterval = 1_000,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    PublishingEnabled = true,
                    DisableMonitoredItemCache = true
                };
            }

            private void OnAcknowledge(ISession session, PublishSequenceNumbersToAcknowledgeEventArgs e)
            {
                foreach (SubscriptionAcknowledgement acknowledgement in e.AcknowledgementsToSend)
                {
                    if (e.TryGetLifetime(acknowledgement, out NotificationLifetime lifetime))
                    {
                        Presented.Enqueue(lifetime);
                    }
                }
                if (Defer)
                {
                    e.DeferredAcknowledgementsToSend.AddRange(e.AcknowledgementsToSend);
                    e.AcknowledgementsToSend.Clear();
                }
                else if (Drop)
                {
                    e.AcknowledgementsToSend.Clear();
                }
            }

            private async ValueTask<IServiceResponse> HandleAsync(IServiceRequest request, CancellationToken ct)
            {
                switch (request)
                {
                    case CreateSubscriptionRequest create:
                        return new CreateSubscriptionResponse
                        {
                            ResponseHeader = m_channel.CreateGoodHeader(),
                            SubscriptionId = 1,
                            RevisedPublishingInterval = create.RequestedPublishingInterval,
                            RevisedLifetimeCount = create.RequestedLifetimeCount,
                            RevisedMaxKeepAliveCount = create.RequestedMaxKeepAliveCount
                        };
                    case SetPublishingModeRequest publishing:
                        return new SetPublishingModeResponse
                        {
                            ResponseHeader = m_channel.CreateGoodHeader(),
                            Results = publishing.SubscriptionIds.ConvertAll(_ => StatusCodes.Good)
                        };
                    case PublishRequest publish:
                        foreach (SubscriptionAcknowledgement acknowledgement in publish.SubscriptionAcknowledgements)
                        {
                            SentAcknowledgements.Enqueue(acknowledgement);
                        }
                        if (m_responses.TryDequeue(
                            out (Func<PublishRequest, PublishResponse> Response, AsyncOperationGate? Gate,
                                bool FirstGoodOnly) next))
                        {
                            if (next.Gate != null)
                            {
                                await next.Gate.WaitAsync(ct).ConfigureAwait(false);
                            }
                            PublishResponse response = next.Response(publish);
                            if (next.FirstGoodOnly)
                            {
                                var seen = new HashSet<(uint, uint)>();
                                response.Results = publish.SubscriptionAcknowledgements.ConvertAll(ack =>
                                    seen.Add((ack.SubscriptionId, ack.SequenceNumber))
                                        ? (StatusCode)StatusCodes.Good
                                        : StatusCodes.BadSequenceNumberUnknown);
                            }
                            return response;
                        }
                        PublishHeld.TrySetResult(true);
                        await Task.Delay(Timeout.Infinite, m_shutdown.Token).ConfigureAwait(false);
                        throw new ServiceResultException(StatusCodes.BadSessionClosed);
                    default:
                        return m_channel.CreateResponse(request);
                }
            }

            private readonly SessionChannelHarness m_harness;
            private readonly ScriptedChannel m_channel;
            private readonly CancellationTokenSource m_shutdown = new();
            private readonly ConcurrentQueue<(Func<PublishRequest, PublishResponse> Response, AsyncOperationGate? Gate,
                bool FirstGoodOnly)> m_responses = new();
            private readonly ConcurrentQueue<NotificationTermination> m_terminations = new();
            private readonly System.Threading.Channels.Channel<NotificationTermination> m_terminated =
                System.Threading.Channels.Channel.CreateUnbounded<NotificationTermination>();
            private Subscription? m_subscription;
            private Subscription? m_second;
            private bool m_defer;
        }
    }
}
