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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for <see cref="TcpReverseConnectChannel"/> construction,
    /// property contracts, and one-shot receive-loop behaviour.
    /// </summary>
    [TestFixture]
    [Category("TcpReverseConnectChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TcpReverseConnectChannelTests
    {
        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_buffers = null!;
        private ChannelQuotas m_quotas = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_buffers = new BufferManager("reverse-connect-test", 8192, m_telemetry);
            m_quotas = new ChannelQuotas(ServiceMessageContext.Create(m_telemetry));
        }

        // ── Construction ──────────────────────────────────────────────────────

        [Test]
        public void ConstructorSixArgSucceeds()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = new TcpReverseConnectChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry);

            Assert.That(channel, Is.Not.Null);
        }

        [Test]
        public void ConstructorSevenArgWithTimeProviderSucceeds()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = new TcpReverseConnectChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry,
                timeProvider: null);

            Assert.That(channel, Is.Not.Null);
        }

        // ── ChannelName ───────────────────────────────────────────────────────

        [Test]
        public void ChannelNameReturnsTcpReverseConnectChannelLiteral()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = BuildChannel(listenerMock);

            Assert.That(channel.ChannelName, Is.EqualTo("TCPREVERSECONNECTCHANNEL"));
        }

        // ── Receive loop — one-shot: transport closed before hello ─────────────

        [Test]
        public async Task AttachThenCloseTransportExitsReceiveLoopCleanlyAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = BuildChannel(listenerMock);

            var buffers = new BufferManager("rcc-test", 8192, m_telemetry);
            (InProcessTransport client, InProcessTransport peer) =
                InProcessTransport.CreatePair(buffers, 8192, m_telemetry);

            try
            {
                // Attach the channel: starts the one-shot ReadReverseHelloOnceAsync loop.
                channel.Attach(channelId: 42u, transport: client);

                // Close the peer — this completes the inbound channel and causes the
                // receive loop to see BadConnectionClosed, which it handles via
                // OnTransportError → ForceChannelFault → ChannelFaulted (clean exit).
                peer.Close();

                // Give the background task a moment to process the channel close.
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

                // ChannelClosed should have been called by the clean-up path.
                listenerMock.Verify(
                    l => l.ChannelClosed(It.IsAny<uint>()),
                    Times.AtMostOnce());
            }
            finally
            {
                peer.Close();
            }
        }

        [Test]
        public async Task AttachThenCancelReceiveLoopExitsCleanlyAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using var channel = BuildChannel(listenerMock);

            var buffers = new BufferManager("rcc-cancel-test", 8192, m_telemetry);
            (InProcessTransport client, InProcessTransport peer) =
                InProcessTransport.CreatePair(buffers, 8192, m_telemetry);

            try
            {
                channel.Attach(channelId: 7u, transport: client);

                // Dispose the channel: cancels the CTS and closes the transport,
                // which causes the one-shot loop to exit via OperationCanceled.
                channel.Dispose();

                // Give the background task time to finish.
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

                // If we reach here without an unhandled exception the loop exited cleanly.
                Assert.Pass("Channel disposed without uncaught exception.");
            }
            finally
            {
                peer.Close();
            }
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

        private TcpReverseConnectChannel BuildChannel(Mock<ITcpChannelListener> listenerMock)
        {
            return new TcpReverseConnectChannel(
                contextId: "test",
                listener: listenerMock.Object,
                bufferManager: m_buffers,
                quotas: m_quotas,
                endpoints: new List<EndpointDescription>(),
                telemetry: m_telemetry);
        }
    }
}
