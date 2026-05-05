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
using Opc.Ua.Tests;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Engine;

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
#if OPCUA_V1_CLIENT
            m_mockContext
                .Setup(c => c.Subscriptions)
                .Returns(new List<Subscription>());
#endif
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
                () => new DefaultSubscriptionEngine(null),
                Throws.TypeOf<ArgumentNullException>());
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
                () => new SubscriptionBridge(null),
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
                stringTable: Array.Empty<string>())
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
                stringTable: new List<string> { "ns-entry" })
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
                stringTable: Array.Empty<string>())
                .ConfigureAwait(false);

            Assert.That(sink.Calls, Has.Count.EqualTo(1));
            NotificationMessage captured = sink.Calls[0].Message;
            Assert.That(
                captured.NotificationData.Count, Is.EqualTo(1));
            ExtensionObject ext = captured.NotificationData[0];
            Assert.That(
                ext.TryGetValue(
                    out DataChangeNotification dcn),
                Is.True);
            Assert.That(
                dcn.DiagnosticInfos.Count, Is.EqualTo(1));
            Assert.That(
                dcn.DiagnosticInfos[0].AdditionalInfo,
                Is.EqualTo("test-diag"));
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
            public List<(ArrayOf<uint> AvailableSequenceNumbers, NotificationMessage Message)> Calls
            {
                get;
            } = new();

            public void SaveMessageInCache(
                ArrayOf<uint> availableSequenceNumbers,
                NotificationMessage message)
            {
                Calls.Add((availableSequenceNumbers, message));
            }
        }
    }
}
