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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class SubscriptionManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockSession = new Mock<ISubscriptionManagerContext>();
            m_mockLoggerFactory = new Mock<ILoggerFactory>();
            m_mockLogger = new Mock<ILogger<SubscriptionManager>>();
            m_mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
            m_mockLoggerFactory
                .Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(m_mockLogger.Object);
            m_subscriptionManager = new SubscriptionManager(
                m_mockSession.Object, m_mockLoggerFactory.Object, DiagnosticsMasks.All);
        }

        [TearDown]
        public void TearDown()
        {
            m_subscriptionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        [Test]
        public async Task AddAndRemoveOfSubscription1Async()
        {
            ILoggerFactory loggerFactory = m_mockLoggerFactory.Object;
            var session = new Mock<ISubscriptionManagerContext>();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new Mock<IManagedSubscription>();
            ms1.SetupGet(s => s.Id).Returns(1);
            var ms2 = new Mock<IManagedSubscription>();
            ms2.SetupGet(s => s.Id).Returns(2);

            var sut = new SubscriptionManager(session.Object,
                loggerFactory, DiagnosticsMasks.None);

            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so1),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms1.Object)
                .Verifiable(Times.Once);
            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so2),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms2.Object);

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
            session.Verify();
        }

        [Test]
        public async Task AddAndRemoveOfSubscription2Async()
        {
            ILoggerFactory loggerFactory = m_mockLoggerFactory.Object;
            var session = new Mock<ISubscriptionManagerContext>();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new Mock<IManagedSubscription>();
            ms1.SetupGet(s => s.Id).Returns(1);
            var ms2 = new Mock<IManagedSubscription>();
            ms2.SetupGet(s => s.Id).Returns(2);

            var sut = new SubscriptionManager(session.Object,
                loggerFactory, DiagnosticsMasks.None);

            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so1),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms1.Object);
            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so2),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms2.Object);

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            ms1.SetupGet(s => s.Created).Returns(true);
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
            session.Verify();
        }

        [Test]
        public async Task ScaleOutAndInOfPublishWorkersAsync()
        {
            ILoggerFactory loggerFactory = m_mockLoggerFactory.Object;
            var session = new Mock<ISubscriptionManagerContext>();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            OptionsMonitor<SubscriptionOptions> so2 = OptionsFactory.Create<SubscriptionOptions>();

            var ms1 = new Mock<IManagedSubscription>();
            ms1.SetupGet(s => s.Id).Returns(1);
            var ms2 = new Mock<IManagedSubscription>();
            ms2.SetupGet(s => s.Id).Returns(2);

            var sut = new SubscriptionManager(session.Object,
                loggerFactory, DiagnosticsMasks.None);

            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so1),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms1.Object)
                .Verifiable(Times.Once);
            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so2),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms2.Object)
                .Verifiable(Times.Once);

            Assert.That(sut.PublishWorkerCount, Is.Zero);

            // Test adding and removing a subscription from
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            ISubscription s2 = sut.Add(m_mockNotificationDataHandler.Object, so2);
            Assert.That(sut.Count, Is.EqualTo(2));

            sut.MinPublishWorkerCount = 0;
            ms1.SetupGet(s => s.Created).Returns(true);
            sut.Update();
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(sut.PublishWorkerCount, Is.EqualTo(1));

            sut.MinPublishWorkerCount = 8;
            ms2.SetupGet(s => s.Created).Returns(true);
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

            session.Verify();
        }

        [Test]
        public async Task SendPublishRequestsWithSuccessAsync()
        {
            ILoggerFactory loggerFactory = m_mockLoggerFactory.Object;
            var session = new Mock<ISubscriptionManagerContext>();
            OptionsMonitor<SubscriptionOptions> so1 = OptionsFactory.Create<SubscriptionOptions>();
            var ms1 = new Mock<IManagedSubscription>();
            ms1.SetupGet(s => s.Id).Returns(1);
            ms1.SetupGet(s => s.Created).Returns(true);
            var sut = new SubscriptionManager(session.Object,
                loggerFactory, DiagnosticsMasks.None);
            session
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.Is<IOptionsMonitor<SubscriptionOptions>>(o => o == so1),
                    It.Is<IMessageAckQueue>(q => q == sut)))
                .Returns(() => ms1.Object)
                .Verifiable(Times.Once);
            // Test adding subscription
            ISubscription s1 = sut.Add(m_mockNotificationDataHandler.Object, so1);
            Assert.That(sut.Count, Is.EqualTo(1));
            sut.MaxPublishWorkerCount = 1;

            // Ack received immediately
            ms1.Setup(subscription => subscription.OnPublishReceivedAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<IReadOnlyList<uint>>(),
                It.IsAny<IReadOnlyList<string>>()))
                .Returns((NotificationMessage n,
                    IReadOnlyList<uint> v, IReadOnlyList<string> s)
                    => sut.QueueAsync(new SubscriptionAcknowledgement
                    {
                        SubscriptionId = 1,
                        SequenceNumber = n.SequenceNumber
                    }, default));

            session.Setup(session => session.PublishAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader h,
                    ArrayOf<SubscriptionAcknowledgement> s, CancellationToken ct)
                    => new PublishResponse
                    {
                        AvailableSequenceNumbers = ArrayOf<uint>.Empty,
                        NotificationMessage = new NotificationMessage
                        {
                            SequenceNumber = h.RequestHandle
                        },
                        Results = s.ToArray().Select(_ => (StatusCode)StatusCodes.Good).ToArrayOf(),
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
            session.Verify();
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
            var mockSubscription = new Mock<IManagedSubscription>();
            m_mockSession.Setup(s => s.CreateSubscription(
                It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            Assert.That(m_subscriptionManager.Items, Has.Exactly(1).Items);
        }

        [Test]
        public void CountReturnsSubscriptionCount()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            m_mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

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
            var mockSubscription = new Mock<IManagedSubscription>();
            m_mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.DisposeAsync().ConfigureAwait(false);
            mockSubscription.Verify(s => s.DisposeAsync(), Times.Once);
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
            var mockSubscription = new Mock<IManagedSubscription>();
            mockSubscription.SetupGet(s => s.Id).Returns(1);
            m_mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.CompleteAsync(1, CancellationToken.None).ConfigureAwait(false);
            Assert.That(m_subscriptionManager.Items, Is.Empty);
        }

        [Test]
        public void AddAddsSubscription()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            m_mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

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
            var mockSubscription = new Mock<IManagedSubscription>();
            m_mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            m_subscriptionManager.Add(m_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await m_subscriptionManager.RecreateSubscriptionsAsync(null, CancellationToken.None).ConfigureAwait(false);
            mockSubscription.Verify(s => s.RecreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        private Mock<ISubscriptionManagerContext> m_mockSession;
        private Mock<ILoggerFactory> m_mockLoggerFactory;
        private Mock<ILogger<SubscriptionManager>> m_mockLogger;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
        private SubscriptionManager m_subscriptionManager;
    }
}
