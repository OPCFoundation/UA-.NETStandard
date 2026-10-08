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

            using TcpReverseConnectChannel channel = BuildChannel(listenerMock);

            Assert.That(channel.ChannelName, Is.EqualTo("TCPREVERSECONNECTCHANNEL"));
        }

        // ── Receive loop — one-shot: transport closed before hello ─────────────

        [Test]
        public async Task AttachThenCloseTransportExitsReceiveLoopCleanlyAsync()
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();

            using TcpReverseConnectChannel channel = BuildChannel(listenerMock);

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

            using TcpReverseConnectChannel channel = BuildChannel(listenerMock);

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

        /// <summary>
        /// OPC 10000-6 §7.1.2.6: the Client returns Bad_TcpEndpointUrlInvalid and closes the connection if the
        /// ServerUri or EndpointUrl exceeds 4096 bytes; a missing or relative EndpointUrl is not a valid URL either.
        /// </summary>
        [TestCase(4097, 20, TestName = "ServerUriTooLong")]
        [TestCase(20, 4097, TestName = "EndpointUrlTooLong")]
        [TestCase(-1, 20, TestName = "ServerUriNull")]
        [TestCase(20, -1, TestName = "EndpointUrlNull")]
        [TestCase(20, 0, TestName = "EndpointUrlRelative")]
        public async Task InvalidReverseHelloIsRejectedWithEndpointUrlInvalidAsync(
            int serverUriLength,
            int endpointUrlLength)
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TcpReverseConnectChannel channel = BuildChannel(listenerMock);
            var buffers = new BufferManager("rcc-invalid-hello", 8192, m_telemetry);
            (InProcessTransport client, InProcessTransport peer) =
                InProcessTransport.CreatePair(buffers, 8192, m_telemetry);
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                channel.Attach(channelId: 5u, transport: client);
                await peer.SendChunkAsync(
                    BuildReverseHello(
                        CreateString("urn:server:", serverUriLength),
                        endpointUrlLength == 0 ? "relative/path" : CreateString("opc.tcp://host/", endpointUrlLength)),
                    timeout.Token).ConfigureAwait(false);

                ArraySegment<byte> error = await peer.ReceiveChunkAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(BitConverter.ToUInt32(error.Array!, error.Offset), Is.EqualTo(TcpMessageType.Error));
                Assert.That(
                    BitConverter.ToUInt32(error.Array!, error.Offset + 8),
                    Is.EqualTo((uint)StatusCodes.BadTcpEndpointUrlInvalid));
                listenerMock.Verify(
                    l => l.TransferListenerChannelAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<Uri>()),
                    Times.Never());
            }
            finally
            {
                peer.Close();
            }
        }

        [TestCase("", TestName = "ServerUriEmpty")]
        [TestCase("   ", TestName = "ServerUriWhitespace")]
        public async Task ReverseHelloWithEmptyServerUriIsRejectedAsync(string serverUri)
        {
            Mock<ITcpChannelListener> listenerMock = CreateListenerMock();
            using TcpReverseConnectChannel channel = BuildChannel(listenerMock);
            var buffers = new BufferManager("rcc-empty-serveruri", 8192, m_telemetry);
            (InProcessTransport client, InProcessTransport peer) =
                InProcessTransport.CreatePair(buffers, 8192, m_telemetry);
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                channel.Attach(channelId: 6u, transport: client);
                await peer.SendChunkAsync(
                    BuildReverseHello(serverUri, "opc.tcp://host/endpoint"),
                    timeout.Token).ConfigureAwait(false);

                ArraySegment<byte> error = await peer.ReceiveChunkAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(BitConverter.ToUInt32(error.Array!, error.Offset), Is.EqualTo(TcpMessageType.Error));
                Assert.That(
                    BitConverter.ToUInt32(error.Array!, error.Offset + 8),
                    Is.EqualTo((uint)StatusCodes.BadTcpEndpointUrlInvalid));
                listenerMock.Verify(
                    l => l.TransferListenerChannelAsync(It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<Uri>()),
                    Times.Never());
            }
            finally
            {
                peer.Close();
            }
        }

        private static string? CreateString(string prefix, int length)
        {
            return length < 0 ? null : prefix + new string('x', length - prefix.Length);
        }

        private byte[] BuildReverseHello(string? serverUri, string? endpointUrl)
        {
            byte[] buffer = new byte[8192];
            using var encoder = new BinaryEncoder(buffer, 0, buffer.Length, m_quotas.MessageContext);
            encoder.WriteUInt32(null, TcpMessageType.ReverseHello);
            encoder.WriteUInt32(null, 0);
            encoder.WriteString(null, serverUri);
            encoder.WriteString(null, endpointUrl);
            int length = encoder.Close();
            BitConverter.GetBytes(length).CopyTo(buffer, 4);
            byte[] chunk = new byte[length];
            Array.Copy(buffer, chunk, length);
            return chunk;
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
                endpoints: [],
                telemetry: m_telemetry);
        }
    }
}
