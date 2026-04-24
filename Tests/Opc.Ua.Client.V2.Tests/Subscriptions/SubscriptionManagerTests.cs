// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Moq;
    using Neovolve.Logging.Xunit;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;
    using Xunit.Abstractions;

    public sealed class SubscriptionManagerTests : IDisposable
    {
        public SubscriptionManagerTests(ITestOutputHelper output)
        {
            _output = output;
            _mockSession = new Mock<ISubscriptionManagerContext>();
            _mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockLogger = new Mock<ILogger<SubscriptionManager>>();
            _mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
            _mockLoggerFactory
                .Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
            _subscriptionManager = new SubscriptionManager(
                _mockSession.Object, _mockLoggerFactory.Object, DiagnosticsMasks.All);
        }

        public void Dispose()
        {
            _subscriptionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task AddAndRemoveOfSubscription1Async()
        {
            var loggerFactory = LogFactory.Create(_output);
            var session = new Mock<ISubscriptionManagerContext>();
            var so1 = OptionsFactory.Create<SubscriptionOptions>();
            var so2 = OptionsFactory.Create<SubscriptionOptions>();

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

            sut.PublishWorkerCount.Should().Be(0);

            // Test adding and removing a subscription from
            var s1 = sut.Add(_mockNotificationDataHandler.Object, so1);
            var s2 = sut.Add(_mockNotificationDataHandler.Object, so2);
            sut.Count.Should().Be(2);

            sut.Invoking(s => s.Add(_mockNotificationDataHandler.Object, so2)).Should().Throw<ServiceResultException>()
                .Which.StatusCode.Should().Be(StatusCodes.BadAlreadyExists);
            await Task.Delay(100); // Give time to workers to start
            sut.PublishWorkerCount.Should().Be(0);
            await sut.CompleteAsync(1, default);
            sut.Count.Should().Be(1);
            sut.Items.Should().Contain(s2);

            sut.PublishControlCycles.Should().BeGreaterThan(0);
            await sut.DisposeAsync();
            sut.Count.Should().Be(0);
            sut.PublishWorkerCount.Should().Be(0);
            session.Verify();
        }

        [Fact]
        public async Task AddAndRemoveOfSubscription2Async()
        {
            var loggerFactory = LogFactory.Create(_output);
            var session = new Mock<ISubscriptionManagerContext>();
            var so1 = OptionsFactory.Create<SubscriptionOptions>();
            var so2 = OptionsFactory.Create<SubscriptionOptions>();

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

            sut.PublishWorkerCount.Should().Be(0);

            // Test adding and removing a subscription from
            var s1 = sut.Add(_mockNotificationDataHandler.Object, so1);
            var s2 = sut.Add(_mockNotificationDataHandler.Object, so2);
            sut.Count.Should().Be(2);

            ms1.SetupGet(s => s.Created).Returns(true);
            sut.Update();
            await Task.Delay(100);

            await sut.CompleteAsync(2, default); // Remove s2
            sut.Count.Should().Be(1);
            sut.Items.Should().NotContain(s2);

            await Task.Delay(1000); // Give time to workers to start

            sut.PublishWorkerCount.Should().Be(2);
            sut.PublishControlCycles.Should().BeGreaterThan(0);

            await sut.CompleteAsync(1, default); // Remove s1
            sut.Count.Should().Be(0);
            sut.Items.Should().NotContain(s1);

            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(0);

            await sut.DisposeAsync();
            sut.Count.Should().Be(0);
            sut.PublishWorkerCount.Should().Be(0);
            session.Verify();
        }

        [Fact]
        public async Task ScaleOutAndInOfPublishWorkersAsync()
        {
            var loggerFactory = LogFactory.Create(_output);
            var session = new Mock<ISubscriptionManagerContext>();
            var so1 = OptionsFactory.Create<SubscriptionOptions>();
            var so2 = OptionsFactory.Create<SubscriptionOptions>();

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

            sut.PublishWorkerCount.Should().Be(0);

            // Test adding and removing a subscription from
            var s1 = sut.Add(_mockNotificationDataHandler.Object, so1);
            var s2 = sut.Add(_mockNotificationDataHandler.Object, so2);
            sut.Count.Should().Be(2);

            sut.MinPublishWorkerCount = 0;
            ms1.SetupGet(s => s.Created).Returns(true);
            sut.Update();
            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(1);

            sut.MinPublishWorkerCount = 8;
            ms2.SetupGet(s => s.Created).Returns(true);
            sut.Update();
            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(8);

            sut.MinPublishWorkerCount = 4;
            sut.Update();
            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(4);

            sut.MinPublishWorkerCount = 0;
            sut.Update();
            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(2);

            sut.MinPublishWorkerCount = 0;
            sut.MaxPublishWorkerCount = 1;
            sut.Update();
            await Task.Delay(100);
            sut.PublishWorkerCount.Should().Be(1);

            await sut.DisposeAsync();
            sut.Count.Should().Be(0);
            sut.PublishWorkerCount.Should().Be(0);

            session.Verify();
        }

        [Fact]
        public async Task SendPublishRequestsWithSuccessAsync()
        {
            var loggerFactory = LogFactory.Create(_output);
            var session = new Mock<ISubscriptionManagerContext>();
            var so1 = OptionsFactory.Create<SubscriptionOptions>();
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
            var s1 = sut.Add(_mockNotificationDataHandler.Object, so1);
            sut.Count.Should().Be(1);
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
                It.IsAny<SubscriptionAcknowledgementCollection>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader h,
                    SubscriptionAcknowledgementCollection s, CancellationToken ct)
                    => new PublishResponse
                    {
                        AvailableSequenceNumbers = Array.Empty<uint>(),
                        NotificationMessage = new NotificationMessage
                        {
                            SequenceNumber = h.RequestHandle
                        },
                        Results = new StatusCodeCollection(
                            s.Select(_ => (StatusCode)StatusCodes.Good)),
                        SubscriptionId = 1,
                        MoreNotifications = false,
                        ResponseHeader = new ResponseHeader
                        {
                            ServiceResult = StatusCodes.Good,
                            StringTable = []
                        }
                    });

            sut.Resume();
            await Task.Delay(1000);

            await sut.DisposeAsync();
            sut.Count.Should().Be(0);
            sut.PublishWorkerCount.Should().Be(0);
            session.Verify();
        }

        [Fact]
        public void TransferSubscriptionsOnRecreateSetAndGet()
        {
            _subscriptionManager.TransferSubscriptionsOnRecreate = true;
            _subscriptionManager.TransferSubscriptionsOnRecreate.Should().BeTrue();
        }

        [Fact]
        public void ReturnDiagnosticsSetAndGet()
        {
            _subscriptionManager.ReturnDiagnostics = DiagnosticsMasks.All;
            _subscriptionManager.ReturnDiagnostics.Should().Be(DiagnosticsMasks.All);
        }

        [Fact]
        public void MinPublishWorkerCountSetAndGet()
        {
            _subscriptionManager.MinPublishWorkerCount = 5;
            _subscriptionManager.MinPublishWorkerCount.Should().Be(5);
        }

        [Fact]
        public void MaxPublishWorkerCountSetAndGet()
        {
            _subscriptionManager.MaxPublishWorkerCount = 10;
            _subscriptionManager.MaxPublishWorkerCount.Should().Be(10);
        }

        [Fact]
        public void ItemsReturnsSubscriptions()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            _mockSession.Setup(s => s.CreateSubscription(
                It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            _subscriptionManager.Add(_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            _subscriptionManager.Items.Should().ContainSingle();
        }

        [Fact]
        public void CountReturnsSubscriptionCount()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            _mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            _subscriptionManager.Add(_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            _subscriptionManager.Count.Should().Be(1);
        }

        [Fact]
        public void GoodPublishRequestCountReturnsCount()
        {
            _subscriptionManager.GoodPublishRequestCount.Should().Be(0);
        }

        [Fact]
        public void BadPublishRequestCountReturnsCount()
        {
            _subscriptionManager.BadPublishRequestCount.Should().Be(0);
        }

        [Fact]
        public void PublishWorkerCountReturnsCount()
        {
            _subscriptionManager.PublishWorkerCount.Should().Be(0);
        }

        [Fact]
        public async Task DisposeAsyncDisposesSubscriptionsAsync()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            _mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            _subscriptionManager.Add(_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await _subscriptionManager.DisposeAsync();
            mockSubscription.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public void UpdateTriggersPublishController()
        {
            _subscriptionManager.Update();// No exception means success
        }

        [Fact]
        public async Task QueueAsyncQueuesAcknowledgementAsync()
        {
            var ack = new SubscriptionAcknowledgement();
            await _subscriptionManager.QueueAsync(ack, CancellationToken.None);
            // No exception means success
        }

        [Fact]
        public async Task CompleteAsyncCompletesSubscriptionAsync()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            mockSubscription.SetupGet(s => s.Id).Returns(1);
            _mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            _subscriptionManager.Add(_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await _subscriptionManager.CompleteAsync(1, CancellationToken.None);
            _subscriptionManager.Items.Should().BeEmpty();
        }

        [Fact]
        public void AddAddsSubscription()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            _mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            var subscription = _subscriptionManager.Add(_mockNotificationDataHandler.Object,
                Mock.Of<IOptionsMonitor<SubscriptionOptions>>());

            subscription.Should().NotBeNull();
            _subscriptionManager.Items.Should().ContainSingle();
        }

        [Fact]
        public void ResumeResumesSubscriptions()
        {
            _subscriptionManager.Resume();// No exception means success
        }

        [Fact]
        public void PausePausesSubscriptions()
        {
            _subscriptionManager.Pause();// No exception means success
        }

        [Fact]
        public async Task RecreateSubscriptionsAsyncRecreatesSubscriptionsAsync()
        {
            var mockSubscription = new Mock<IManagedSubscription>();
            _mockSession
                .Setup(s => s.CreateSubscription(
                    It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>(),
                    It.IsAny<IMessageAckQueue>()))
                .Returns(mockSubscription.Object);

            _subscriptionManager.Add(_mockNotificationDataHandler.Object, Mock.Of<IOptionsMonitor<SubscriptionOptions>>());
            await _subscriptionManager.RecreateSubscriptionsAsync(null, CancellationToken.None);
            mockSubscription.Verify(s => s.RecreateAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        private readonly ITestOutputHelper _output;
        private readonly Mock<ISubscriptionManagerContext> _mockSession;
        private readonly Mock<ILoggerFactory> _mockLoggerFactory;
        private readonly Mock<ILogger<SubscriptionManager>> _mockLogger;
        private readonly Mock<ISubscriptionNotificationHandler> _mockNotificationDataHandler;
        private readonly SubscriptionManager _subscriptionManager;
    }
}
