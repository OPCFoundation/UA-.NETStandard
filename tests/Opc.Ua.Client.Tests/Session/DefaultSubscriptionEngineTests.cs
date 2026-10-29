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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Engine;
using Opc.Ua.Tests;

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite (the existing
// narrow-scope pragma below at line ~163 predates the file-level decision).
#pragma warning disable CA2000

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("DefaultSubscriptionEngine")]
    public class DefaultSubscriptionEngineTests
    {
        private ITelemetryContext m_telemetry;
        private Mock<ISubscriptionEngineContext> m_mockContext;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            m_mockContext = new Mock<ISubscriptionEngineContext>();
            m_mockContext
                .Setup(c => c.SessionId)
                .Returns(new NodeId(1));
            m_mockContext
                .Setup(c => c.Disposed)
                .Returns(false);
            m_mockContext
                .Setup(c => c.Reconnecting)
                .Returns(false);
            m_mockContext
                .Setup(c => c.Connected)
                .Returns(false);
            m_mockContext
                .Setup(c => c.Closing)
                .Returns(false);
            m_mockContext
                .Setup(c => c.Telemetry)
                .Returns(m_telemetry);
            m_mockContext
                .Setup(c => c.OperationTimeout)
                .Returns(60000);
            m_mockContext
                .Setup(c => c.ReturnDiagnostics)
                .Returns(DiagnosticsMasks.None);
            m_mockContext
                .Setup(c => c.ReconnectLock)
                .Returns(new SemaphoreSlim(1, 1));
            m_mockContext
                .Setup(c => c.GoodPublishRequestCount)
                .Returns(0);
            m_mockContext
                .Setup(c => c.PrepareAcknowledgementsToSend(It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns((List<SubscriptionAcknowledgement> acknowledgements) => (acknowledgements, []));
            m_mockContext
                .Setup(c => c.Subscriptions)
                .Returns([]);
        }

        [Test]
        public void FactoryCreateReturnsV2Engine()
        {
            ISubscriptionEngine engine =
                DefaultSubscriptionEngineFactory.Instance.Create(
                    m_mockContext.Object);

            Assert.That(engine, Is.Not.Null);
            Assert.That(
                engine,
                Is.InstanceOf<DefaultSubscriptionEngine>());

            engine.Dispose();
        }

        /// <summary>
        /// Definitive Publish failures must reach the session's recovery callback.
        /// </summary>
        [TestCaseSource(nameof(s_transportDownStatuses))]
        [CancelAfter(10_000)]
        public async Task PublishFailureSignalsSessionRecoveryAsync(StatusCode statusCode, CancellationToken testCt)
        {
            ConfigureRecoverySubscription();
            var recovery = new TaskCompletionSource<ServiceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockContext.Setup(value => value.OnKeepAliveError(It.IsAny<ServiceResult>()))
                .Callback<ServiceResult>(error => recovery.TrySetResult(error));
            m_mockContext.Setup(value => value.PublishAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<PublishResponse>(
                    Task.FromException<PublishResponse>(new ServiceResultException(statusCode))));

            await using var engine = new DefaultSubscriptionEngine(m_mockContext.Object);
            AddRecoverySubscription(engine);

            ServiceResult error = await recovery.Task.WaitAsync(TimeSpan.FromSeconds(3), testCt).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(statusCode));
        }

        /// <summary>
        /// A failed attempt from a retired or unavailable session cannot initiate recovery.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The test case specifies an unknown state.</exception>
        [TestCase("closing")]
        [TestCase("disposed")]
        [TestCase("disconnected")]
        [TestCase("reconnecting")]
        [TestCase("replaced")]
        [CancelAfter(10_000)]
        public async Task PublishFailureIgnoresUnavailableSessionAsync(string state, CancellationToken testCt)
        {
            ConfigureRecoverySubscription();
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<PublishResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;
            m_mockContext.Setup(value => value.PublishAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, ArrayOf<SubscriptionAcknowledgement> _, CancellationToken ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        sent.TrySetResult(true);
                        return await response.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    nextSent.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    throw new InvalidOperationException("The pending Publish must be cancelled during cleanup.");
                });
            await using var engine = new DefaultSubscriptionEngine(m_mockContext.Object);
            AddRecoverySubscription(engine);
            await sent.Task.WaitAsync(testCt).ConfigureAwait(false);
            switch (state)
            {
                case "closing":
                    m_mockContext.SetupGet(value => value.Closing).Returns(true);
                    break;
                case "disposed":
                    m_mockContext.SetupGet(value => value.Disposed).Returns(true);
                    break;
                case "disconnected":
                    m_mockContext.SetupGet(value => value.Connected).Returns(false);
                    break;
                case "reconnecting":
                    m_mockContext.SetupGet(value => value.Reconnecting).Returns(true);
                    break;
                case "replaced":
                    m_mockContext.SetupGet(value => value.SessionId).Returns(new NodeId(2));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }

            response.SetException(new ServiceResultException(StatusCodes.BadSecureChannelClosed));
            await nextSent.Task.WaitAsync(testCt).ConfigureAwait(false);

            m_mockContext.Verify(value => value.OnKeepAliveError(It.IsAny<ServiceResult>()), Times.Never);
        }

        /// <summary>
        /// Retirement while dispatch awaits recovery admission prevents a callback against the recovered session.
        /// </summary>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishRecoveryRetirementBeforeCallbackIsIgnoredAsync(CancellationToken testCt)
        {
            ConfigureRecoverySubscription();
            using var reconnect = new SemaphoreSlim(0, 1);
            var admission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var nextSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<PublishResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_mockContext.SetupGet(value => value.ReconnectLock).Returns(() =>
            {
                admission.TrySetResult(true);
                return reconnect;
            });
            int calls = 0;
            m_mockContext.Setup(value => value.PublishAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, ArrayOf<SubscriptionAcknowledgement> _, CancellationToken ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        sent.TrySetResult(true);
                        return await response.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    nextSent.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    throw new InvalidOperationException("The pending Publish must be cancelled during cleanup.");
                });
            await using var engine = new DefaultSubscriptionEngine(m_mockContext.Object);
            AddRecoverySubscription(engine);
            await sent.Task.WaitAsync(testCt).ConfigureAwait(false);
            response.SetException(new ServiceResultException(StatusCodes.BadConnectionClosed));
            await admission.Task.WaitAsync(testCt).ConfigureAwait(false);

            ((SubscriptionManager)engine.SubscriptionManager).OnSessionReactivated();
            reconnect.Release();
            await nextSent.Task.WaitAsync(testCt).ConfigureAwait(false);

            m_mockContext.Verify(value => value.OnKeepAliveError(It.IsAny<ServiceResult>()), Times.Never);
        }

        /// <summary>
        /// The V2 engine forwards acknowledgement preparation through the session
        /// context and reconsiders deferred entries without another notification.
        /// </summary>
        /// <param name="testCt">Test cancellation token.</param>
        [Test]
        [CancelAfter(10_000)]
        public async Task PublishAcknowledgementsUseSessionDeferralPolicyAsync(CancellationToken testCt)
        {
            var services = new Mock<ISubscriptionServiceSetClientMethods>();
            services.Setup(value => value.CreateSubscriptionAsync(
                It.IsAny<RequestHeader?>(),
                It.IsAny<double>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<uint>(),
                It.IsAny<bool>(),
                It.IsAny<byte>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 7,
                    RevisedPublishingInterval = 10,
                    RevisedMaxKeepAliveCount = 1,
                    RevisedLifetimeCount = 100,
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            services.Setup(value => value.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good],
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            m_mockContext.SetupGet(value => value.SubscriptionServiceSet).Returns(services.Object);
            m_mockContext.SetupGet(value => value.Connected).Returns(true);
            m_mockContext.SetupGet(value => value.SessionTimeout).Returns(60000);
            var deferred = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int release = 0;
            m_mockContext
                .Setup(value => value.PrepareAcknowledgementsToSend(It.IsAny<List<SubscriptionAcknowledgement>>()))
                .Returns((List<SubscriptionAcknowledgement> acknowledgements) =>
                {
                    if (Volatile.Read(ref release) == 0)
                    {
                        deferred.TrySetResult(true);
                        return ([], acknowledgements);
                    }
                    return (acknowledgements, []);
                });
            m_mockContext.Setup(value => value.PublishAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<SubscriptionAcknowledgement>>(),
                It.IsAny<CancellationToken>()))
                .Returns(async (RequestHeader _, ArrayOf<SubscriptionAcknowledgement> acknowledgements,
                    CancellationToken ct) =>
                {
                    foreach (SubscriptionAcknowledgement acknowledgement in acknowledgements)
                    {
                        if (acknowledgement.SubscriptionId == 7 && acknowledgement.SequenceNumber == 42)
                        {
                            sent.TrySetResult(true);
                        }
                    }
                    await Task.Delay(20, ct).ConfigureAwait(false);
                    return new PublishResponse
                    {
                        SubscriptionId = 7,
                        NotificationMessage = new NotificationMessage(),
                        AvailableSequenceNumbers = [],
                        Results = acknowledgements.ConvertAll(_ => StatusCodes.Good),
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                    };
                });
            using var engine = new DefaultSubscriptionEngine(m_mockContext.Object);
            try
            {
                engine.MinPublishRequestCount = 1;
                engine.MaxPublishRequestCount = 1;
                ISubscription subscription = engine.SubscriptionManager.Add(
                    Mock.Of<ISubscriptionNotificationHandler>(),
                    new OptionsMonitor<Subscriptions.SubscriptionOptions>(new Subscriptions.SubscriptionOptions
                    {
                        PublishingEnabled = true,
                        PublishingInterval = TimeSpan.FromMilliseconds(10),
                        KeepAliveCount = 1,
                        LifetimeCount = 100,
                        DisableUnboundedItemMode = true
                    }));
                while (!subscription.Created)
                {
                    await Task.Delay(10, testCt).ConfigureAwait(false);
                }
                var queue = (IMessageAckQueue)engine.SubscriptionManager;
                IManagedSubscription partition = ((LogicalSubscription)subscription).Partitions[0];
                await queue.QueueAsync(partition, partition.MessageGeneration,
                    new SubscriptionAcknowledgement { SubscriptionId = 7, SequenceNumber = 42 },
                    testCt).ConfigureAwait(false);
                await deferred.Task.WaitAsync(testCt).ConfigureAwait(false);
                Assert.That(sent.Task.IsCompleted, Is.False);

                Volatile.Write(ref release, 1);

                await sent.Task.WaitAsync(testCt).ConfigureAwait(false);
                m_mockContext.Verify(
                    value => value.PrepareAcknowledgementsToSend(It.IsAny<List<SubscriptionAcknowledgement>>()),
                    Times.AtLeast(2));
            }
            finally
            {
                await engine.StopPublishingAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test]
        public void FactoryInstanceIsSingleton()
        {
            DefaultSubscriptionEngineFactory first =
                DefaultSubscriptionEngineFactory.Instance;
            DefaultSubscriptionEngineFactory second =
                DefaultSubscriptionEngineFactory.Instance;

            Assert.That(first, Is.SameAs(second));
        }

        [Test]
        public void ConstructorThrowsOnNullContext()
        {
            Assert.That(
                () => new DefaultSubscriptionEngine(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SubscriptionContextReportsRevisedSessionTimeout()
        {
            m_mockContext
                .Setup(c => c.OperationTimeout)
                .Returns(120000);
            m_mockContext
                .Setup(c => c.SessionTimeout)
                .Returns(60000);

            var context =
                new DefaultSubscriptionEngine.SubscriptionContextAdapter(
                    m_mockContext.Object);

            Assert.That(
                context.SessionTimeout,
                Is.EqualTo(TimeSpan.FromSeconds(60)));
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(1e300)]
        public void SubscriptionContextReportsZeroForUnusableSessionTimeout(
            double sessionTimeout)
        {
            m_mockContext
                .Setup(c => c.SessionTimeout)
                .Returns(sessionTimeout);

            var context =
                new DefaultSubscriptionEngine.SubscriptionContextAdapter(
                    m_mockContext.Object);

            Assert.That(context.SessionTimeout, Is.EqualTo(TimeSpan.Zero));
        }

        [Test]
        public void StartPublishingDoesNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.StartPublishing(timeout: 5000, fullQueue: false),
                Throws.Nothing);
        }

        [Test]
        public void PauseAndResumeDoNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(engine.PausePublishing, Throws.Nothing);
            Assert.That(engine.ResumePublishing, Throws.Nothing);
        }

        [Test]
        public async Task StopPublishingDisposesCleanlyAsync()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            await engine.StopPublishingAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);
#pragma warning restore CA2000 // Dispose objects before losing scope

            Assert.That(engine.Dispose, Throws.Nothing);
            Assert.That(engine.Dispose, Throws.Nothing);
        }

        [Test]
        public void NotifySubscriptionsChangedDoesNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.NotifySubscriptionsChanged,
                Throws.Nothing);
        }

        [Test]
        public void GoodPublishRequestCountDefaultsToZero()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.GoodPublishRequestCount, Is.Zero);
        }

        [Test]
        public void BadPublishRequestCountDefaultsToZero()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.BadPublishRequestCount, Is.Zero);
        }

        [Test]
        public void MinMaxPublishRequestCountCanBeSet()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            engine.MinPublishRequestCount = 3;
            engine.MaxPublishRequestCount = 20;

            Assert.That(
                engine.MinPublishRequestCount, Is.EqualTo(3));
            Assert.That(
                engine.MaxPublishRequestCount, Is.EqualTo(20));
        }

        [Test]
        public void BridgeConstructorThrowsOnNullSink()
        {
            Assert.That(
                () => new SubscriptionBridge(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void BridgeCanBeInstantiated()
        {
            var sink = new RecordingSubscriptionMessageSink();

            var bridge = new SubscriptionBridge(sink);

            Assert.That(bridge, Is.Not.Null);
        }

        [Test]
        public async Task BridgeOnKeepAliveForwardsToCacheSinkAsync()
        {
            var sink = new RecordingSubscriptionMessageSink();
            var bridge = new SubscriptionBridge(sink);
            var mockSubscription = new Mock<ISubscription>();

            await bridge.OnKeepAliveNotificationAsync(
                mockSubscription.Object,
                sequenceNumber: 1,
                publishTime: DateTime.UtcNow,
                publishStateMask: PublishState.None)
                .ConfigureAwait(false);

            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            Assert.That(sink.Calls[0].Message.SequenceNumber, Is.EqualTo(1u));
        }

        [Test]
        public async Task BridgeOnDataChangeForwardsToCacheSinkAsync()
        {
            var sink = new RecordingSubscriptionMessageSink();
            var bridge = new SubscriptionBridge(sink);
            var mockSubscription = new Mock<ISubscription>();

            var changes = new DataValueChange[]
            {
                new(
                    MonitoredItem: null,
                    Value: new DataValue(new Variant(42)),
                    DiagnosticInfo: null)
            };

            await bridge.OnDataChangeNotificationAsync(
                mockSubscription.Object,
                sequenceNumber: 5,
                publishTime: DateTime.UtcNow,
                notification: changes.AsMemory(),
                publishStateMask: PublishState.None,
                stringTable: [])
                .ConfigureAwait(false);

            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            Assert.That(sink.Calls[0].Message.SequenceNumber, Is.EqualTo(5u));
        }

        [Test]
        public async Task BridgeOnEventNotificationForwardsToCacheSinkAsync()
        {
            var sink = new RecordingSubscriptionMessageSink();
            var bridge = new SubscriptionBridge(sink);
            var mockSubscription = new Mock<ISubscription>();

            var events = new EventNotification[]
            {
                new(
                    MonitoredItem: null,
                    Fields: new ArrayOf<Variant>(
                        new[] { new Variant("event-data") }))
            };

            await bridge.OnEventDataNotificationAsync(
                mockSubscription.Object,
                sequenceNumber: 10,
                publishTime: DateTime.UtcNow,
                notification: events.AsMemory(),
                publishStateMask: PublishState.None,
                stringTable: ["ns-entry"])
                .ConfigureAwait(false);

            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            Assert.That(sink.Calls[0].Message.SequenceNumber, Is.EqualTo(10u));
        }

        [Test]
        public async Task BridgeDataChangeWithDiagnosticsPreservesDiagnosticInfoAsync()
        {
            var sink = new RecordingSubscriptionMessageSink();
            var bridge = new SubscriptionBridge(sink);
            var mockSubscription = new Mock<ISubscription>();

            var diag = new DiagnosticInfo
            {
                AdditionalInfo = "test-diag"
            };
            var changes = new DataValueChange[]
            {
                new(
                    MonitoredItem: null,
                    Value: new DataValue(new Variant(1)),
                    DiagnosticInfo: diag)
            };

            await bridge.OnDataChangeNotificationAsync(
                mockSubscription.Object,
                sequenceNumber: 7,
                publishTime: DateTime.UtcNow,
                notification: changes.AsMemory(),
                publishStateMask: PublishState.None,
                stringTable: [])
                .ConfigureAwait(false);

            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            NotificationMessage captured = sink.Calls[0].Message;
            Assert.That(
                captured.NotificationData.Count, Is.EqualTo(1));
            ExtensionObject ext = captured.NotificationData[0];
            Assert.That(
                ext.TryGetValue(
                    out DataChangeNotification? dcn),
                Is.True);
            Assert.That(
                dcn!.DiagnosticInfos.Count, Is.EqualTo(1));
            Assert.That(
                dcn.DiagnosticInfos[0].AdditionalInfo,
                Is.EqualTo("test-diag"));
        }

        /// <summary>
        /// Sets up the server-side subscription operations used by recovery tests.
        /// </summary>
        private void ConfigureRecoverySubscription()
        {
            var services = new Mock<ISubscriptionServiceSetClientMethods>();
            services.Setup(value => value.CreateSubscriptionAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<uint>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<bool>(), It.IsAny<byte>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 7,
                    RevisedPublishingInterval = 10,
                    RevisedMaxKeepAliveCount = 100,
                    RevisedLifetimeCount = 6000,
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            m_mockContext.Setup(value => value.DeleteSubscriptionsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<uint>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good],
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good }
                });
            m_mockContext.SetupGet(value => value.SubscriptionServiceSet).Returns(services.Object);
            m_mockContext.SetupGet(value => value.Connected).Returns(true);
            m_mockContext.SetupGet(value => value.SessionTimeout).Returns(60000);
        }

        /// <summary>
        /// Adds one subscription to start a single Publish worker.
        /// </summary>
        private static void AddRecoverySubscription(DefaultSubscriptionEngine engine)
        {
            engine.MinPublishRequestCount = 1;
            engine.MaxPublishRequestCount = 1;
            engine.SubscriptionManager.Add(
                Mock.Of<ISubscriptionNotificationHandler>(),
                new OptionsMonitor<Subscriptions.SubscriptionOptions>(new Subscriptions.SubscriptionOptions
                {
                    PublishingEnabled = true,
                    PublishingInterval = TimeSpan.FromMilliseconds(10),
                    KeepAliveCount = 100,
                    LifetimeCount = 6000,
                    DisableUnboundedItemMode = true
                }));
        }

        /// <summary>
        /// Hand-rolled fake for <see cref="ISubscriptionMessageSink"/>
        /// (internal interface) that records every <c>SaveMessageInCache</c>
        /// call. Replaces a Moq-driven mock so that the test assembly does
        /// not need <c>InternalsVisibleTo("DynamicProxyGenAssembly2")</c>
        /// on the production assembly.
        /// </summary>
        private sealed class RecordingSubscriptionMessageSink
            : ISubscriptionMessageSink
        {
            public List<(ArrayOf<uint> AvailableSequenceNumbers, NotificationMessage Message)> Calls { get; } = [];

            public void SaveMessageInCache(
                ArrayOf<uint> availableSequenceNumbers,
                NotificationMessage message)
            {
                Calls.Add((availableSequenceNumbers, message));
            }
        }

        /// <summary>Publish failures that require session or channel recovery.</summary>
        private static readonly StatusCode[] s_transportDownStatuses =
        [
            StatusCodes.BadSessionIdInvalid,
            StatusCodes.BadSecureChannelIdInvalid,
            StatusCodes.BadSecureChannelClosed,
            StatusCodes.BadNotConnected,
            StatusCodes.BadConnectionClosed
        ];
    }
}
