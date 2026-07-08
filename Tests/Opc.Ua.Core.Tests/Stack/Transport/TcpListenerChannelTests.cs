/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="TcpListenerChannel"/> — focusing on
    /// public API contracts, null-arg guards, and edge-case state transitions
    /// that are not exercised by the full integration paths.
    /// </summary>
    [TestFixture]
    [Category("TcpListenerChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TcpListenerChannelTests
    {
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("listener-channel-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        // ── Construction ──────────────────────────────────────────────────────

        [Test]
        public void ConstructorSixArgSucceeds()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = new TcpListenerChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                serverCertificates: null!,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry);

            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public void ConstructorSevenArgSucceeds()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = new TcpListenerChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                serverCertificates: null!,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry,
                timeProvider: null);

            Assert.That(channel, Is.Not.Null);
        }

        // ── ChannelName ───────────────────────────────────────────────────────

        [Test]
        public void ChannelNameReturnsTcpListenerChannelLiteral()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = BuildChannel(listenerMock);

            Assert.That(channel.ChannelName, Is.EqualTo("TCPLISTENERCHANNEL"));
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        [Test]
        public void SetRequestReceivedCallbackDoesNotThrow()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.SetRequestReceivedCallback(
                    (ch, reqId, req) => { }),
                Throws.Nothing);
        }

        [Test]
        public void SetReportOpenSecureChannelAuditCallbackDoesNotThrow()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.SetReportOpenSecureChannelAuditCallback(
                    (ch, req, cert, ex) => { }),
                Throws.Nothing);
        }

        [Test]
        public void SetReportCloseSecureChannelAuditCallbackDoesNotThrow()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.SetReportCloseSecureChannelAuditCallback(
                    (ch, ex) => { }),
                Throws.Nothing);
        }

        [Test]
        public void SetReportCertificateAuditCallbackDoesNotThrow()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.SetReportCertificateAuditCallback(
                    (cert, ex) => { }),
                Throws.Nothing);
        }

        // ── Attach ────────────────────────────────────────────────────────────

        [Test]
        public void AttachNullTransportThrowsArgumentNullException()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.Attach(channelId: 1, transport: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("transport"));
        }

        [Test]
        public void AttachSecondTimeThrowsInvalidOperationException()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("attach-test", 8192, telemetry);

            (InProcessTransport transportA, InProcessTransport peerA) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);
            (InProcessTransport transportB, InProcessTransport peerB) =
                InProcessTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                channel.Attach(channelId: 1, transport: transportA);

                // Second Attach must fail because Transport is already set.
                Assert.That(
                    () => channel.Attach(channelId: 2, transport: transportB),
                    Throws.TypeOf<InvalidOperationException>());
            }
            finally
            {
                // Close the peer transports to unblock any pending receive.
                peerA.Close();
                peerB.Close();
            }
        }

        // ── Reconnect ─────────────────────────────────────────────────────────

        [Test]
        public void ReconnectThrowsNotImplementedException()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(
                () => channel.Reconnect(null!, 0u, 0u, null!, null!, null!),
                Throws.TypeOf<NotImplementedException>());
        }

        // ── Status properties ─────────────────────────────────────────────────

        [Test]
        public void UsedBySessionIsFalseOnFreshChannel()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(channel.UsedBySession, Is.False);
        }

        [Test]
        public void ElapsedSinceLastActiveTimeReturnsNonNegative()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            Assert.That(channel.ElapsedSinceLastActiveTime, Is.GreaterThanOrEqualTo(0));
        }

        // ── IdleCleanup ───────────────────────────────────────────────────────

        [Test]
        public void IdleCleanupOnFreshChannelDoesNotThrow()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            // Channel is in Closed state before Attach; IdleCleanup should be a no-op.
            Assert.That(channel.IdleCleanup, Throws.Nothing);
        }

        // ── TryCloseForCertificateRotation is internal — tested via the
        // ── ITransportListenerCertificateRotation integration tests.

        // ── OnTokenActivated event ────────────────────────────────────────────

        [Test]
        public void OnTokenActivatedEventCanBeSubscribedAndUnsubscribed()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using var channel = BuildChannel(listenerMock);

            ListenerChannelTokenActivatedEventHandler handler =
                (ch, current, previous) => { };

            Assert.That(() =>
            {
                channel.OnTokenActivated += handler;
                channel.OnTokenActivated -= handler;
            }, Throws.Nothing);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Mock<ITcpChannelListener> CreateListenerMock()
        {
            var mock = new Mock<ITcpChannelListener>();
            mock.Setup(l => l.EndpointUrl)
                .Returns(new Uri("opc.tcp://localhost:4840"));
            mock.Setup(l => l.ChannelClosed(It.IsAny<uint>()));
            mock.Setup(l => l.TransferListenerChannelAsync(
                    It.IsAny<uint>(),
                    It.IsAny<string>(),
                    It.IsAny<Uri>()))
                .Returns(Task.FromResult(false));
            return mock;
        }

        private TcpListenerChannel BuildChannel(Mock<ITcpChannelListener> listenerMock)
        {
            return new TcpListenerChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                serverCertificates: null!,
                endpoints: [],
                telemetry: m_telemetry);
        }
    }
}
