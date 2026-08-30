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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    [Category("Client")]
    [Category("SubscriptionManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            m_session = new FakeSubscriptionManagerContext();
            m_telemetry = NUnitTelemetryContext.Create();
            m_mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
            m_subscriptionManager = new SubscriptionManager(
                m_session, m_telemetry.LoggerFactory, DiagnosticsMasks.All);
        }

        [TearDown]
        public void TearDown()
        {
            m_subscriptionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        [Test]
        public async Task AddAndRemoveOfSubscription1Async()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() => sut.Add(m_mockNotificationDataHandler.Object, so2));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));
            await Task.Delay(100).ConfigureAwait(false); // Give time to workers to start
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            await sut.CompleteAsync(ms1, 1, default).ConfigureAwait(false);
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Contain(s2));

            Assert.That(sut.PublishControlCycles, Is.GreaterThan(0));
            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            // Verify the so1 subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
        }

        [Test]
        public async Task AddAndRemoveOfSubscription2Async()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            ms1.Created = true;
            sut.Update();
            // Wait for the worker controller to spin up workers. CreatedCount=1
            // but MinPublishWorkerCount defaults to 2, so the desired count is 2.
            await WaitForPublishWorkerCountAsync(sut, 2).ConfigureAwait(false);

            await sut.CompleteAsync(ms2, 2, default).ConfigureAwait(false); // Remove s2
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Not.Contain(s2));

            // Workers stay at 2 because MinPublishWorkerCount=2 (default).
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(2));
            Assert.That(sut.PublishControlCycles, Is.GreaterThan(0));

            await sut.CompleteAsync(ms1, 1, default).ConfigureAwait(false); // Remove s1
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.Items, Does.Not.Contain(s1));

            await WaitForPublishWorkerCountAsync(sut, 0).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task CompleteForUncreatedIdDoesNotEvictPendingSubscriptionAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();

            // Subscription awaiting CreateSubscription
            var ms1 = new FakeManagedSubscription();

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                throw new InvalidOperationException("unexpected options");
            };

            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            Assert.That(sut.Count, Is.EqualTo(1));

            // A deleted subscription acknowledges completion with an id already reset to 0
            await sut.CompleteAsync(new FakeManagedSubscription(), 0, default).ConfigureAwait(false);

            // Verify that the pending subscription still exists
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Contain(s1));

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task ScaleOutAndInOfPublishWorkersAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new FakeManagedSubscription { Id = 1 };
            var ms2 = new FakeManagedSubscription { Id = 2 };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(queue, Is.SameAs(sut));
                if (ReferenceEquals(options, so1))
                {
                    return ms1;
                }
                if (ReferenceEquals(options, so2))
                {
                    return ms2;
                }
                throw new InvalidOperationException("unexpected options");
            };

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            sut.MinPublishWorkerCount = 0;
            ms1.Created = true;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 8;
            ms2.Created = true;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 8).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 4;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 4).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 0;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 2).ConfigureAwait(false);

            sut.MinPublishWorkerCount = 0;
            sut.MaxPublishWorkerCount = 1;
            sut.Update();
            await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Each subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so2)), Is.EqualTo(1));
        }

        [Test]
        public async Task SendPublishRequestsWithSuccessAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new FakeManagedSubscription { Id = 1, Created = true };
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            session.CreateSubscriptionFactory = (handler, options, queue) =>
            {
                Assert.That(ReferenceEquals(options, so1), Is.True);
                Assert.That(queue, Is.SameAs(sut));
                return ms1;
            };
            // Test adding subscription
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            Assert.That(sut.Count, Is.EqualTo(1));
            sut.MaxPublishWorkerCount = 1;

            // Ack received immediately
            ms1.OnPublishReceivedAsyncFunc = (n, _, _) =>
                sut.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1,
                    SequenceNumber = n.SequenceNumber
                }, default);

            session.OnPublishAsync = (h, s, ct) =>
                new ValueTask<PublishResponse>(new PublishResponse
                {
                    AvailableSequenceNumbers = [],
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = h.RequestHandle
                    },
                    Results = s.ConvertAll(_ => StatusCodes.Good),
                    SubscriptionId = 1,
                    MoreNotifications = false,
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good,
                        StringTable = []
                    }
                });

            sut.Resume();
            await Task.Delay(1000).ConfigureAwait(false);

            await sut.DisposeAsync().ConfigureAwait(false);
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.PublishWorkerCount, Is.Zero);
            // The so1 subscription was created exactly once.
            Assert.That(session.CreateSubscriptionCalls
                .Count(c => ReferenceEquals(c.Options, so1)), Is.EqualTo(1));
        }

        [Test]
        public void TransferSubscriptionsOnRecreateSetAndGet()
        {
            m_subscriptionManager.TransferSubscriptionsOnRecreate = true;
            Assert.That(m_subscriptionManager.TransferSubscriptionsOnRecreate, Is.True);
        }

        [Test]
        public void ReturnDiagnosticsSetAndGet()
        {
            m_subscriptionManager.ReturnDiagnostics = DiagnosticsMasks.All;
            Assert.That(m_subscriptionManager.ReturnDiagnostics, Is.EqualTo(DiagnosticsMasks.All));
        }

        [Test]
        public void MinPublishWorkerCountSetAndGet()
        {
            m_subscriptionManager.MinPublishWorkerCount = 5;
            Assert.That(m_subscriptionManager.MinPublishWorkerCount, Is.EqualTo(5));
        }

        [Test]
        public void MaxPublishWorkerCountSetAndGet()
        {
            m_subscriptionManager.MaxPublishWorkerCount = 10;
            Assert.That(m_subscriptionManager.MaxPublishWorkerCount, Is.EqualTo(10));
        }

        [Test]
        public void ItemsReturnsSubscriptions()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            Assert.That(m_subscriptionManager.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void CountReturnsSubscriptionCount()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            Assert.That(m_subscriptionManager.Count, Is.EqualTo(1));
        }

        [Test]
        public void GoodPublishRequestCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.GoodPublishRequestCount, Is.Zero);
        }

        [Test]
        public void BadPublishRequestCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.BadPublishRequestCount, Is.Zero);
        }

        [Test]
        public void PublishWorkerCountReturnsCount()
        {
            Assert.That(m_subscriptionManager.PublishWorkerCount, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncDisposesSubscriptionsAsync()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.DisposeAsync().ConfigureAwait(false);
            Assert.That(mockSubscription.DisposeAsyncCalls, Is.EqualTo(1));
        }

        [Test]
        public void UpdateTriggersPublishController()
        {
            m_subscriptionManager.Update();// No exception means success
        }

        [Test]
        public async Task QueueAsyncQueuesAcknowledgementAsync()
        {
            var ack = new SubscriptionAcknowledgement();
            await m_subscriptionManager.QueueAsync(ack, CancellationToken.None).ConfigureAwait(false);
            // No exception means success
        }

        [Test]
        public async Task CompleteAsyncCompletesSubscriptionAsync()
        {
            var mockSubscription = new FakeManagedSubscription { Id = 1 };
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.CompleteAsync(mockSubscription, 1, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(m_subscriptionManager.Items, Is.Empty);
        }

        [Test]
        public void AddAddsSubscription()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            ISubscription subscription = m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());

            Assert.That(subscription, Is.Not.Null);
            Assert.That(m_subscriptionManager.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void ResumeResumesSubscriptions()
        {
            m_subscriptionManager.Resume();// No exception means success
        }

        [Test]
        public void PausePausesSubscriptions()
        {
            m_subscriptionManager.Pause();// No exception means success
        }

        [Test]
        public async Task PublishingQuiescencePreservesConcurrentPauseAsync()
        {
            var subscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => subscription;
            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            m_subscriptionManager.Resume();

            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task quiesced = m_subscriptionManager
                .RunWithPublishingQuiescedAsync(async ct =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                }, CancellationToken.None)
                .AsTask();

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            m_subscriptionManager.Pause();
            release.TrySetResult(true);
            await quiesced.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);

            Assert.That(subscription.NotifySubscriptionManagerPausedCalls,
                Is.EqualTo(s_resumedThenPaused));
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DisposeAsyncCancelsActivePublishingQuiescenceAsync(
            CancellationToken testCt)
        {
            var entered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task quiesced = m_subscriptionManager
                .RunWithPublishingQuiescedAsync(async ct =>
                {
                    entered.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        cancelled.TrySetResult(true);
                        throw;
                    }
                }, testCt)
                .AsTask();

            await entered.Task.WaitAsync(testCt).ConfigureAwait(false);
            Task dispose = m_subscriptionManager.DisposeAsync().AsTask();

            await cancelled.Task.WaitAsync(testCt).ConfigureAwait(false);
            await dispose.WaitAsync(testCt).ConfigureAwait(false);
            Assert.That(async () => await quiesced.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task PublishingQuiescenceWaitsForAckRollbackAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var subscription = new FakeManagedSubscription
            {
                Id = 1,
                Created = true
            };
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            try
            {
                session.CreateSubscriptionFactory = (_, _, _) => subscription;
                var publishCalled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var publishGate = new TaskCompletionSource<PublishResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.OnPublishAsync = (_, acknowledgements, _) =>
                {
                    Assert.That(acknowledgements, Has.Count.EqualTo(1));
                    publishCalled.TrySetResult(true);
                    return new ValueTask<PublishResponse>(publishGate.Task);
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Add(m_mockNotificationDataHandler.Object,
                    Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
                await sut.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1,
                    SequenceNumber = 7
                }, testCt).ConfigureAwait(false);
                sut.Resume();
                await publishCalled.Task.WaitAsync(testCt).ConfigureAwait(false);

                var actionEntered = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var dropped = 0;
                Task quiesced = sut.RunWithPublishingQuiescedAsync(_ =>
                {
                    dropped = sut.DropPendingForSubscription(1);
                    sut.Pause();
                    actionEntered.TrySetResult(true);
                    return default;
                }, testCt).AsTask();

                await Task.Delay(100, testCt).ConfigureAwait(false);
                Assert.That(actionEntered.Task.IsCompleted, Is.False);

                publishGate.TrySetException(
                    new ServiceResultException(StatusCodes.BadNotConnected));
                await actionEntered.Task.WaitAsync(testCt).ConfigureAwait(false);
                await quiesced.WaitAsync(testCt).ConfigureAwait(false);

                Assert.That(dropped, Is.EqualTo(1));
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RecreateSubscriptionsAsyncRecreatesSubscriptionsAsync()
        {
            var mockSubscription = new FakeManagedSubscription();
            m_session.CreateSubscriptionFactory =
                (handler, options, queue) => mockSubscription;

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.RecreateSubscriptionsAsync(null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(mockSubscription.RecreateAsyncCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task DrainAsyncReturnsImmediatelyWhenNoPublishesActiveAsync()
        {
            // No worker started — counter is zero — DrainAsync should
            // complete synchronously.
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await m_subscriptionManager
                .DrainAsync(ct.Token).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30_000)]
        public async Task DrainAsyncWaitsForInFlightPublishToCompleteAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            try
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) =>
                {
                    Assert.That(ReferenceEquals(opts, options), Is.True);
                    Assert.That(queue, Is.SameAs(sut));
                    return ms1;
                };

                // Block the publish call so a worker stays "in flight".
                var publishGate = new TaskCompletionSource<PublishResponse>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var publishCalled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                session.OnPublishAsync = (h, a, ct) =>
                {
                    publishCalled.TrySetResult(true);
                    return new ValueTask<PublishResponse>(publishGate.Task);
                };

                sut.MaxPublishWorkerCount = 1;
                sut.MinPublishWorkerCount = 1;
                ISubscription _ = sut.Add(
                    m_mockNotificationDataHandler.Object, options);
                sut.Resume();

                // Wait until the worker has called PublishAsync at least
                // once so the active-publish counter is non-zero.
                await publishCalled.Task.WaitAsync(testCt).ConfigureAwait(false);

                // Pause is soft: it stops *new* publishes from being
                // issued, but the in-flight publish call is still
                // outstanding. Drain must wait for it.
                sut.Pause();

                using var drainCts = CancellationTokenSource
                    .CreateLinkedTokenSource(testCt);
                drainCts.CancelAfter(TimeSpan.FromMilliseconds(300));
                try
                {
                    await sut.DrainAsync(drainCts.Token).ConfigureAwait(false);
                    Assert.Fail(
                        "DrainAsync must not return while a publish is " +
                        "in flight; expected OperationCanceledException.");
                }
                catch (OperationCanceledException)
                {
                    // expected — drain timed out because the publish is
                    // still in flight.
                }

                // Complete the publish, releasing the worker.
                publishGate.TrySetResult(new PublishResponse
                {
                    AvailableSequenceNumbers = [],
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = 1u
                    },
                    Results = [],
                    SubscriptionId = 1,
                    MoreNotifications = false,
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Good,
                        StringTable = []
                    }
                });

                // The publish worker decrements the counter in finally,
                // so DrainAsync now returns.
                using var drainCts2 = CancellationTokenSource
                    .CreateLinkedTokenSource(testCt);
                drainCts2.CancelAfter(TimeSpan.FromSeconds(5));
                await sut.DrainAsync(drainCts2.Token).ConfigureAwait(false);
            }
            finally
            {
                await sut.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Regression coverage for the ack-pruning helper added by
        /// #3540. <see cref="IMessageAckQueue.DropPendingForSubscription"/>
        /// must remove every queued acknowledgement targeting the
        /// given subscription id while leaving acks for other
        /// subscriptions intact. The recovery path relies on this
        /// before recreating a subscription so stale acks for the
        /// dead id do not leak <c>BadSubscriptionIdInvalid</c> to
        /// the server when the server re-uses the id.
        /// </summary>
        [Test]
        public async Task DropPendingForSubscriptionRemovesOnlyMatchingAcksAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                // Variable typed as the interface on purpose to test the
                // public contract; the analyzer's perf hint does not
                // apply to a coverage test.
#pragma warning disable CA1859
                IMessageAckQueue queue = sut;
                await queue.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1u,
                    SequenceNumber = 10u
                }).ConfigureAwait(false);
                await queue.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 2u,
                    SequenceNumber = 11u
                }).ConfigureAwait(false);
                await queue.QueueAsync(new SubscriptionAcknowledgement
                {
                    SubscriptionId = 1u,
                    SequenceNumber = 12u
                }).ConfigureAwait(false);

                int dropped = queue.DropPendingForSubscription(1u);

                Assert.That(dropped, Is.EqualTo(2),
                    "All queued acks for the dead subscription id must be dropped.");

                int droppedAgain = queue.DropPendingForSubscription(1u);
                Assert.That(droppedAgain, Is.Zero,
                    "Second call must drop nothing (idempotent).");

                int droppedOther = queue.DropPendingForSubscription(2u);
                Assert.That(droppedOther, Is.EqualTo(1),
                    "Ack for the surviving subscription id must still be queued " +
                    "and drainable on a subsequent call.");
#pragma warning restore CA1859
            }
        }

        /// <summary>
        /// Calling <see cref="IMessageAckQueue.DropPendingForSubscription"/>
        /// on an empty queue is a no-op and must not throw.
        /// </summary>
        [Test]
        public async Task DropPendingForSubscriptionOnEmptyQueueReturnsZeroAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
#pragma warning disable CA1859
                IMessageAckQueue queue = sut;
                int dropped = queue.DropPendingForSubscription(7u);
                Assert.That(dropped, Is.Zero);
#pragma warning restore CA1859
            }
        }

        /// <summary>
        /// Regression for #4113. A subscription resets its server side id to
        /// zero before its message processor drains and completes, so
        /// <see cref="IMessageAckQueue.CompleteAsync"/> must retire the
        /// subscription by instance. Resolving it by id would evict an
        /// arbitrary other subscription that has been added but not created
        /// yet — those all share id zero — leaving that subscription without
        /// publish dispatch.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task CompleteAsyncRetiresOnlyTheCompletedSubscriptionWhenOthersArePendingCreationAsync()
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> disposingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            // Not created yet — the server has not assigned an id.
            var pending = new FakeManagedSubscription { Id = 0u };
            // Created, then deleted on the server, which resets Id to zero.
            var disposing = new FakeManagedSubscription { Id = 7u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, pendingOptions) ? pending : disposing;

                ISubscription pendingSubscription = sut.Add(
                    m_mockNotificationDataHandler.Object, pendingOptions);
                ISubscription disposingSubscription = sut.Add(
                    m_mockNotificationDataHandler.Object, disposingOptions);
                Assert.That(sut.Count, Is.EqualTo(2));

                // Server side delete resets the id; the message processor
                // still passes the last known server id to CompleteAsync.
                disposing.Id = 0u;
                disposing.Created = false;

                await sut.CompleteAsync(disposing, 7u, CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(sut.Count, Is.EqualTo(1));
                Assert.That(sut.Items, Does.Contain(pendingSubscription));
                Assert.That(sut.Items, Does.Not.Contain(disposingSubscription));
            }
        }

        /// <summary>
        /// A publish response that arrives after a subscription was retired
        /// must not make the manager delete anything on the server: the
        /// retired server side id is remembered even though the subscription
        /// already reset its own id to zero.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task CompleteAsyncRemembersRetiredServerIdAfterIdWasResetAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> retiredOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> keptOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var retired = new FakeManagedSubscription { Id = 7u, Created = true };
            var kept = new FakeManagedSubscription { Id = 8u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, retiredOptions) ? retired : kept;

                sut.Add(m_mockNotificationDataHandler.Object, retiredOptions);
                sut.Add(m_mockNotificationDataHandler.Object, keptOptions);

                // Server side delete of the retired subscription.
                retired.Id = 0u;
                retired.Created = false;
                await sut.CompleteAsync(retired, 7u, CancellationToken.None)
                    .ConfigureAwait(false);

                // The still late in-flight publish response for id 7.
                var publishSeen = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.OnPublishAsync = (h, a, ct) =>
                {
                    publishSeen.TrySetResult(true);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(7u, h.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await publishSeen.Task.WaitAsync(testCt).ConfigureAwait(false);
                await Task.Delay(250, testCt).ConfigureAwait(false);

                Assert.That(session.DeleteCalls, Is.Empty,
                    "A publish response for a retired subscription must not " +
                    "trigger DeleteSubscriptions.");
            }
        }

        /// <summary>
        /// A publish response can overtake the CreateSubscription continuation
        /// that assigns the server side id. While any subscription is still
        /// awaiting its id, an unresolved subscription id must not be deleted
        /// on the server — doing so would silently kill a healthy subscription.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDefersDeletingUnknownSubscriptionWhileCreationIsPendingAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };
            var pending = new FakeManagedSubscription { Id = 0u };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, createdOptions) ? created : pending;

                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);
                sut.Add(m_mockNotificationDataHandler.Object, pendingOptions);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 3,
                    testCt).ConfigureAwait(false);
                Assert.That(session.DeleteCalls, Is.Empty,
                    "An unresolved subscription id must not be deleted while " +
                    "another subscription is still awaiting its id.");

                // Once nothing is pending creation the orphan is cleaned up.
                pending.Id = 2u;
                pending.Created = true;
                sut.Update();

                await WaitUntilAsync(() => session.DeleteCallsCount > 0, testCt)
                    .ConfigureAwait(false);
                IReadOnlyList<FakeSubscriptionManagerContext.DeleteCall> deleteCalls =
                    session.DeleteCalls;
                Assert.That(deleteCalls[0].SubscriptionIds.ToList(),
                    Does.Contain(4242u));
            }
        }

        /// <summary>
        /// A subscription created through the classic <c>Session.AddSubscription</c>
        /// API is unknown to this manager's registry, but it is live and owned by
        /// the application. Deleting it as abandoned takes it down on the server
        /// while the caller still believes it is streaming, which shows up as a
        /// twin that silently stops updating.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerKeepsSubscriptionOwnedBySessionAsync(
            CancellationToken testCt)
        {
            using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue) => created;
                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);

                // The session holds this one outside the manager's registry.
                session.SessionOwnedSubscriptionIds.Add(4242u);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 5,
                    testCt).ConfigureAwait(false);

                Assert.That(session.DeleteCalls, Is.Empty,
                    "A subscription the session owns must never be deleted as abandoned.");
                Assert.That(session.SessionDispatchCount, Is.GreaterThan(0),
                    "Notifications for a session-owned subscription must be delivered to it.");
            }
        }

        /// <summary>
        /// A session with only a classic subscription still needs a Publish worker.
        /// The manager does not own that subscription, but it owns the shared Publish
        /// pipeline that dispatches responses to it.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task ClassicSessionSubscriptionStartsPublishWorkerAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            session.SessionOwnedSubscriptionIds.Add(4242u);
            var publishSeen = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.OnPublishAsync = (header, acknowledgements, ct) =>
            {
                publishSeen.TrySetResult(true);
                return new ValueTask<PublishResponse>(
                    CreatePublishResponse(4242u, header.RequestHandle));
            };

            var sut = new SubscriptionManager(
                session,
                loggerFactory,
                DiagnosticsMasks.None)
            {
                MinPublishWorkerCount = 1,
                MaxPublishWorkerCount = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                sut.Resume();
                sut.Update();

                await publishSeen.Task.WaitAsync(testCt).ConfigureAwait(false);
                await WaitUntilAsync(
                    () => session.SessionDispatchCount > 0,
                    testCt).ConfigureAwait(false);
                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(sut.Count, Is.Zero);
                    Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));
                    Assert.That(session.DeleteCalls, Is.Empty);
                });
            }
        }

        /// <summary>
        /// The publish controller must never size the pool from the default
        /// Min/MaxPublishWorkerCount. It runs synchronously up to its first
        /// wait, i.e. inside the constructor, so sizing there happened before
        /// the owner could configure the pool: a session that already carries
        /// subscriptions got MinPublishWorkerCount (default 2) workers that
        /// were torn down again by the first resize, and PublishWorkerCount
        /// transiently reported that overshoot.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerPoolIsNeverSizedFromDefaultsBeforeConfigurationAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            session.SessionOwnedSubscriptionIds.Add(4242u);
            // Model a transport that does not complete synchronously - the
            // manager has no created subscription to derive a publish
            // interval from, so an instantly completing fake would spin the
            // worker at full speed for the duration of the test.
            session.OnPublishAsync = async (header, acknowledgements, ct) =>
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                return CreatePublishResponse(4242u, header.RequestHandle);
            };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);

            await using (sut.ConfigureAwait(false))
            {
                // Nothing has signalled the controller yet, so the defaults
                // are still in force. Guard that they still make the
                // scenario meaningful: the bug only shows when the default
                // minimum inflates the count the session alone asks for.
                Assert.Multiple(() =>
                {
                    Assert.That(sut.MinPublishWorkerCount,
                        Is.GreaterThan(session.SessionSubscriptionCount),
                        "Defaults no longer inflate the pool; pick a scenario " +
                        "that still exercises constructor-time sizing.");
                    Assert.That(sut.PublishWorkerCount, Is.Zero,
                        "The constructor must not size the publish worker pool.");
                });

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();
                sut.Update();

                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                // The pool must stay at the configured maximum while the
                // workers publish - a worker that exits is reaped before its
                // replacement is created, so the two are never counted
                // together.
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(1))
                {
                    Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));
                    await Task.Delay(10, testCt).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// While an identifier stays unresolved the server keeps answering with
        /// the same undeliverable response. The worker must back off instead of
        /// republishing immediately - otherwise it busy-spins, hammers the
        /// server and floods the log until the creation window closes.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerThrottlesWhileSubscriptionIdIsUnresolvedAsync(
            CancellationToken testCt)
        {
            const int kMaxExpectedPublishes = 100;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> createdOptions =
                OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> pendingOptions =
                OptionsFactory.Create<SubscriptionOptions>();

            var created = new FakeManagedSubscription { Id = 1u, Created = true };
            var pending = new FakeManagedSubscription { Id = 0u };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, options, queue)
                    => ReferenceEquals(options, createdOptions) ? created : pending;

                sut.Add(m_mockNotificationDataHandler.Object, createdOptions);
                sut.Add(m_mockNotificationDataHandler.Object, pendingOptions);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => Volatile.Read(ref publishCount) >= 3,
                    testCt).ConfigureAwait(false);
                await Task.Delay(500, testCt).ConfigureAwait(false);

                Assert.That(Volatile.Read(ref publishCount),
                    Is.LessThan(kMaxExpectedPublishes),
                    "The publish worker must throttle while a subscription id " +
                    "cannot be resolved instead of republishing in a tight loop.");
            }
        }

        /// <summary>
        /// A genuine orphan must be deleted exactly once. The server keeps
        /// answering with the orphan until the delete takes effect, so without
        /// retiring the identifier the worker issues a DeleteSubscriptions call
        /// for every single publish response.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishWorkerDeletesUnknownSubscriptionOnlyOnceAsync(
            CancellationToken testCt)
        {
            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var created = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) => created;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                int publishCount = 0;
                session.OnPublishAsync = (h, a, ct) =>
                {
                    Interlocked.Increment(ref publishCount);
                    return new ValueTask<PublishResponse>(
                        CreatePublishResponse(4242u, h.RequestHandle));
                };

                sut.MinPublishWorkerCount = 1;
                sut.MaxPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(() => session.DeleteCallsCount > 0, testCt)
                    .ConfigureAwait(false);
                await Task.Delay(500, testCt).ConfigureAwait(false);

                IReadOnlyList<FakeSubscriptionManagerContext.DeleteCall> deleteCalls =
                    session.DeleteCalls;
                Assert.That(deleteCalls, Has.Count.EqualTo(1),
                    "The orphaned subscription must be deleted exactly once.");
                Assert.That(deleteCalls[0].SubscriptionIds.ToList(),
                    Does.Contain(4242u));
            }
        }

        /// <summary>
        /// The publish controller must not lose worker pool resize signals.
        /// Re-arming its control wait on every loop iteration abandoned the
        /// previous waiter inside the auto reset event, so a later signal was
        /// handed to the dead waiter and the pool stopped resizing.
        /// </summary>
        [Test]
        [CancelAfter(30_000)]
        public async Task PublishControllerKeepsResizingAfterWorkersExitAsync(
            CancellationToken testCt)
        {
            const int kForcedWorkerExits = 2;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            OptionsMonitor<SubscriptionOptions> options =
                OptionsFactory.Create<SubscriptionOptions>();
            var subscription = new FakeManagedSubscription { Id = 1u, Created = true };

            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                session.CreateSubscriptionFactory = (handler, opts, queue) => subscription;
                sut.Add(m_mockNotificationDataHandler.Object, options);

                int publishCount = 0;
                session.OnPublishAsync = async (h, a, ct) =>
                {
                    if (Interlocked.Increment(ref publishCount) <= kForcedWorkerExits)
                    {
                        // Terminates the worker task, which wakes the
                        // controller through the worker task instead of
                        // through its control wait.
                        throw new OperationCanceledException();
                    }
                    // Park the replacement worker so the controller is idle
                    // when the resize signal below is raised.
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return new PublishResponse();
                };

                sut.MaxPublishWorkerCount = 5;
                sut.MinPublishWorkerCount = 1;
                sut.Resume();

                await WaitUntilAsync(
                    () => Volatile.Read(ref publishCount) > kForcedWorkerExits,
                    testCt).ConfigureAwait(false);
                await WaitForPublishWorkerCountAsync(sut, 1).ConfigureAwait(false);

                // Single signal — under the defect it was swallowed by one of
                // the waiters abandoned while the workers were exiting.
                sut.MinPublishWorkerCount = 3;

                await WaitForPublishWorkerCountAsync(sut, 3).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adding and retiring subscriptions concurrently must leave exactly
        /// the subscriptions that were not retired registered — the scenario
        /// from #4113 reduced to the manager surface. The surviving
        /// subscriptions are deliberately left pending creation (id zero),
        /// which is what made them eligible victims of the id based lookup.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ConcurrentAddAndCompleteRetireOnlyCompletedSubscriptionsAsync()
        {
            const int kWorkers = 8;
            const int kIterations = 25;

            ILoggerFactory loggerFactory = m_telemetry.LoggerFactory;
            var session = new FakeSubscriptionManagerContext();
            var sut = new SubscriptionManager(session,
                loggerFactory, DiagnosticsMasks.None);
            await using (sut.ConfigureAwait(false))
            {
                var nextId = 0;
                var survivors = new ConcurrentBag<ISubscription>();
                var partitions = new ConcurrentDictionary<
                    IOptionsMonitor<SubscriptionOptions>, FakeManagedSubscription>();
                session.CreateSubscriptionFactory =
                    (handler, options, queue) => partitions[options];

                await Task.WhenAll(Enumerable.Range(0, kWorkers)
                    .Select(_ => Task.Run(async () =>
                    {
                        for (int i = 0; i < kIterations; i++)
                        {
                            // Added but not created yet — shares id zero with
                            // every other subscription awaiting creation.
                            OptionsMonitor<SubscriptionOptions> survivorOptions =
                                OptionsFactory.Create<SubscriptionOptions>();
                            partitions[survivorOptions] = new FakeManagedSubscription();
                            survivors.Add(sut.Add(
                                m_mockNotificationDataHandler.Object, survivorOptions));

                            OptionsMonitor<SubscriptionOptions> churnOptions =
                                OptionsFactory.Create<SubscriptionOptions>();
                            uint churnId = (uint)Interlocked.Increment(ref nextId);
                            var churn = new FakeManagedSubscription
                            {
                                Id = churnId,
                                Created = true
                            };
                            partitions[churnOptions] = churn;
                            sut.Add(m_mockNotificationDataHandler.Object, churnOptions);

                            // Mimic the server side delete, which resets the id
                            // before the message processor drains and completes.
                            churn.Id = 0u;
                            churn.Created = false;
                            await sut.CompleteAsync(churn, churnId, CancellationToken.None)
                                .ConfigureAwait(false);
                            await Task.Yield();
                        }
                    }))).ConfigureAwait(false);

                Assert.That(sut.Count, Is.EqualTo(kWorkers * kIterations));
                Assert.That(sut.Items, Is.EquivalentTo(survivors));
            }
        }

        private static PublishResponse CreatePublishResponse(
            uint subscriptionId, uint sequenceNumber)
        {
            return new PublishResponse
            {
                AvailableSequenceNumbers = [],
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = sequenceNumber
                },
                Results = [],
                SubscriptionId = subscriptionId,
                MoreNotifications = false,
                ResponseHeader = new ResponseHeader
                {
                    ServiceResult = StatusCodes.Good,
                    StringTable = []
                }
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition,
            CancellationToken ct, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(10);
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.That(sw.Elapsed, Is.LessThan(timeout.Value),
                    "Timed out waiting for the expected condition.");
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Polls <see cref="SubscriptionManager.PublishWorkerCount"/> until it
        /// matches the expected value or the timeout elapses. Replaces fixed
        /// <c>Task.Delay</c>-based waits, which are flaky on slow CI runners.
        /// </summary>
        private static async Task WaitForPublishWorkerCountAsync(
            SubscriptionManager sut,
            int expected,
            TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout.Value && sut.PublishWorkerCount != expected)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(expected),
                $"Expected PublishWorkerCount={expected} within {timeout.Value}; " +
                $"got {sut.PublishWorkerCount} after {sw.ElapsedMilliseconds} ms.");
        }

        private static readonly bool[] s_resumedThenPaused = [false, true];
        private FakeSubscriptionManagerContext m_session;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
#pragma warning disable NUnit1032 // Disposed via DisposeAsync in TearDown
        private SubscriptionManager m_subscriptionManager;
#pragma warning restore NUnit1032
    }
}
