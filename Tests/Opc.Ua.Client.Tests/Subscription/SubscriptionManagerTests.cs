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
            await sut.CompleteAsync(1, default).ConfigureAwait(false);
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
            await Task.Delay(100).ConfigureAwait(false);

            await sut.CompleteAsync(2, default).ConfigureAwait(false); // Remove s2
            Assert.That(sut.Count, Is.EqualTo(1));
            Assert.That(sut.Items, Does.Not.Contain(s2));

            await Task.Delay(1000).ConfigureAwait(false); // Give time to workers to start

            Assert.That(sut.PublishWorkerCount, Is.EqualTo(2));
            Assert.That(sut.PublishControlCycles, Is.GreaterThan(0));

            await sut.CompleteAsync(1, default).ConfigureAwait(false); // Remove s1
            Assert.That(sut.Count, Is.Zero);
            Assert.That(sut.Items, Does.Not.Contain(s1));

            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.Zero);

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
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));

            sut.MinPublishWorkerCount = 8;
            ms2.Created = true;
            sut.Update();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(8));

            sut.MinPublishWorkerCount = 4;
            sut.Update();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(4));

            sut.MinPublishWorkerCount = 0;
            sut.Update();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(2));

            sut.MinPublishWorkerCount = 0;
            sut.MaxPublishWorkerCount = 1;
            sut.Update();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));

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
                        SequenceNumber = h!.RequestHandle
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
            await m_subscriptionManager.CompleteAsync(1, CancellationToken.None).ConfigureAwait(false);
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
                    Results = ArrayOf<StatusCode>.Empty,
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

        private FakeSubscriptionManagerContext m_session;
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
#pragma warning disable NUnit1032 // Disposed via DisposeAsync in TearDown
        private SubscriptionManager m_subscriptionManager;
#pragma warning restore NUnit1032
    }
}
