#if OPCUA_CLIENT_V2
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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Engine;

namespace Opc.Ua.Client.V2.Tests
{
    [TestFixture]
    [Parallelizable]
    [Category("Client")]
    [Category("V2SubscriptionEngine")]
    public class V2SubscriptionEngineTests
    {
        private Mock<Opc.Ua.ITelemetryContext> m_mockTelemetry;
        private Mock<ILoggerFactory> m_mockLoggerFactory;
        private Mock<ISubscriptionEngineContext> m_mockContext;

        [SetUp]
        public void SetUp()
        {
            m_mockLoggerFactory = new Mock<ILoggerFactory>();
            m_mockLoggerFactory
                .Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(new Mock<ILogger>().Object);

            m_mockTelemetry = new Mock<Opc.Ua.ITelemetryContext>();
            m_mockTelemetry
                .Setup(t => t.LoggerFactory)
                .Returns(m_mockLoggerFactory.Object);

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
                .Returns(m_mockTelemetry.Object);
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
#if OPCUA_V1_CLIENT
            m_mockContext
                .Setup(c => c.Subscriptions)
                .Returns(new List<Subscription>());
#endif
        }

        #region V2SubscriptionEngineFactory tests

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

        [Test]
        public void FactoryInstanceIsSingleton()
        {
            DefaultSubscriptionEngineFactory first =
                DefaultSubscriptionEngineFactory.Instance;
            DefaultSubscriptionEngineFactory second =
                DefaultSubscriptionEngineFactory.Instance;

            Assert.That(first, Is.SameAs(second));
        }

        #endregion

        #region V2SubscriptionEngine tests

        [Test]
        public void ConstructorThrowsOnNullContext()
        {
            Assert.That(
                () => new DefaultSubscriptionEngine(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void StartPublishingDoesNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.StartPublishing(
                    timeout: 5000, fullQueue: false),
                Throws.Nothing);
        }

        [Test]
        public void PauseAndResumeDoNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.PausePublishing(),
                Throws.Nothing);
            Assert.That(
                () => engine.ResumePublishing(),
                Throws.Nothing);
        }

        [Test]
        public async Task StopPublishingDisposesCleanly()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            await engine.StopPublishingAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(() => engine.Dispose(), Throws.Nothing);
            Assert.That(() => engine.Dispose(), Throws.Nothing);
        }

        [Test]
        public void NotifySubscriptionsChangedDoesNotThrow()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                () => engine.NotifySubscriptionsChanged(),
                Throws.Nothing);
        }

        [Test]
        public void GoodPublishRequestCountDefaultsToZero()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.GoodPublishRequestCount, Is.EqualTo(0));
        }

        [Test]
        public void BadPublishRequestCountDefaultsToZero()
        {
            using var engine =
                new DefaultSubscriptionEngine(m_mockContext.Object);

            Assert.That(
                engine.BadPublishRequestCount, Is.EqualTo(0));
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

        #endregion

        #region V2SubscriptionBridge tests

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
            var mockSink = new Mock<ISubscriptionMessageSink>();

            var bridge = new SubscriptionBridge(
                mockSink.Object);

            Assert.That(bridge, Is.Not.Null);
        }

        [Test]
        public async Task BridgeOnKeepAliveForwardsToCacheSink()
        {
            var mockSink = new Mock<ISubscriptionMessageSink>();
            var bridge = new SubscriptionBridge(
                mockSink.Object);
            var mockSubscription = new Mock<ISubscription>();

            await bridge.OnKeepAliveNotificationAsync(
                mockSubscription.Object,
                sequenceNumber: 1,
                publishTime: DateTime.UtcNow,
                publishStateMask: PublishState.None)
                .ConfigureAwait(false);

            mockSink.Verify(
                s => s.SaveMessageInCache(
                    It.IsAny<ArrayOf<uint>>(),
                    It.Is<NotificationMessage>(
                        m => m.SequenceNumber == 1)),
                Times.Once);
        }

        [Test]
        public async Task BridgeOnDataChangeForwardsToCacheSink()
        {
            var mockSink = new Mock<ISubscriptionMessageSink>();
            var bridge = new SubscriptionBridge(
                mockSink.Object);
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
                stringTable: Array.Empty<string>())
                .ConfigureAwait(false);

            mockSink.Verify(
                s => s.SaveMessageInCache(
                    It.IsAny<ArrayOf<uint>>(),
                    It.Is<NotificationMessage>(
                        m => m.SequenceNumber == 5)),
                Times.Once);
        }

        [Test]
        public async Task BridgeOnEventNotificationForwardsToCacheSink()
        {
            var mockSink = new Mock<ISubscriptionMessageSink>();
            var bridge = new SubscriptionBridge(
                mockSink.Object);
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
                stringTable: new List<string> { "ns-entry" })
                .ConfigureAwait(false);

            mockSink.Verify(
                s => s.SaveMessageInCache(
                    It.IsAny<ArrayOf<uint>>(),
                    It.Is<NotificationMessage>(
                        m => m.SequenceNumber == 10)),
                Times.Once);
        }

        [Test]
        public async Task BridgeDataChangeWithDiagnosticsPreservesDiagnosticInfo()
        {
            var mockSink = new Mock<ISubscriptionMessageSink>();
            NotificationMessage captured = null;
            mockSink
                .Setup(s => s.SaveMessageInCache(
                    It.IsAny<ArrayOf<uint>>(),
                    It.IsAny<NotificationMessage>()))
                .Callback<ArrayOf<uint>, NotificationMessage>(
                    (_, msg) => captured = msg);

            var bridge = new SubscriptionBridge(
                mockSink.Object);
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
                stringTable: Array.Empty<string>())
                .ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(
                captured!.NotificationData.Count, Is.EqualTo(1));

            var ext = captured.NotificationData[0];
            Assert.That(
                ext.TryGetEncodeable<DataChangeNotification>(
                    out var dcn),
                Is.True);
            Assert.That(
                dcn.DiagnosticInfos.Count, Is.EqualTo(1));
            Assert.That(
                dcn.DiagnosticInfos[0].AdditionalInfo,
                Is.EqualTo("test-diag"));
        }

        #endregion
    }
}
#endif
