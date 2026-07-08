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

#nullable enable

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER

using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for the WebSocket-backed
    /// <see cref="WebSocketServerByteTransport"/>. Each test pairs the
    /// server transport with a peer <see cref="WebSocket"/> created via
    /// <see cref="WebSocket.CreateFromStream(System.IO.Stream, bool, string?, TimeSpan)"/>
    /// over a loopback TCP <see cref="NetworkStream"/> so the chunk-per-frame
    /// contract is verified without a Kestrel host.
    /// </summary>
    [TestFixture]
    [Category("WebSocketByteTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class WebSocketByteTransportTests
    {
        private const int kBufferSize = 8192;

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("WebSocketByteTransportTests", kBufferSize, m_telemetry);
        }

        [Test]
        public async Task SendChunkAsyncEmitsSingleBinaryFrame()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            byte[] payload = new byte[64];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            await transport.SendChunkAsync(payload, CancellationToken.None).ConfigureAwait(false);

            byte[] receive = new byte[256];
            WebSocketReceiveResult result = await pair.Client
                .ReceiveAsync(new ArraySegment<byte>(receive), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Binary));
            Assert.That(result.EndOfMessage, Is.True);
            Assert.That(result, Has.Property(nameof(WebSocketReceiveResult.Count)).EqualTo(payload.Length));
            byte[] received = new byte[result.Count];
            Buffer.BlockCopy(receive, 0, received, 0, result.Count);
            Assert.That(received, Is.EqualTo(payload));
        }

        [Test]
        public async Task ReceiveChunkAsyncReassemblesAcrossFrames()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            byte[] part1 = new byte[64];
            byte[] part2 = new byte[32];
            for (int i = 0; i < part1.Length; i++)
            {
                part1[i] = (byte)(i + 1);
            }
            for (int i = 0; i < part2.Length; i++)
            {
                part2[i] = (byte)(i + 100);
            }

            // Send the chunk across two binary frames; only the last sets EndOfMessage = true.
            await pair.Client.SendAsync(
                new ArraySegment<byte>(part1),
                WebSocketMessageType.Binary,
                endOfMessage: false,
                CancellationToken.None).ConfigureAwait(false);
            await pair.Client.SendAsync(
                new ArraySegment<byte>(part2),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                CancellationToken.None).ConfigureAwait(false);

            ArraySegment<byte> chunk = await transport
                .ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(chunk, Has.Count.EqualTo(part1.Length + part2.Length));
                byte[] copy = new byte[chunk.Count];
                Buffer.BlockCopy(chunk.Array!, chunk.Offset, copy, 0, chunk.Count);
                for (int i = 0; i < part1.Length; i++)
                {
                    Assert.That(copy[i], Is.EqualTo(part1[i]));
                }
                for (int i = 0; i < part2.Length; i++)
                {
                    Assert.That(copy[part1.Length + i], Is.EqualTo(part2[i]));
                }
            }
            finally
            {
                m_bufferManager.ReturnBuffer(chunk.Array!, nameof(ReceiveChunkAsyncReassemblesAcrossFrames));
            }
        }

        [Test]
        public async Task ReceiveChunkAsyncRejectsTextFrame()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            byte[] textBody = System.Text.Encoding.UTF8.GetBytes("hello");
            await pair.Client.SendAsync(
                new ArraySegment<byte>(textBody),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
        }

        [Test]
        public async Task ReceiveChunkAsyncRejectsMessageExceedingBufferSize()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            // Stream a full buffer's worth of bytes without ever setting
            // EndOfMessage. The receive loop must reject with
            // BadTcpMessageTooLarge instead of spinning on a zero-length
            // destination (the zero-progress / capacity CPU-DoS guard).
            byte[] oversized = new byte[kBufferSize];
            await pair.Client.SendAsync(
                new ArraySegment<byte>(oversized),
                WebSocketMessageType.Binary,
                endOfMessage: false,
                CancellationToken.None).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
        }

        [Test]
        public async Task ReceiveChunkAsyncReportsCloseFrameAsBadConnectionClosed()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            await pair.Client.CloseOutputAsync(
                WebSocketCloseStatus.NormalClosure,
                "test",
                CancellationToken.None).ConfigureAwait(false);

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        [Test]
        public async Task SendChunkAsyncBufferCollectionConcatenatesIntoSingleFrame()
        {
            using var pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            byte[] body1 = new byte[16];
            byte[] body2 = new byte[24];
            for (int i = 0; i < body1.Length; i++)
            {
                body1[i] = (byte)i;
            }
            for (int i = 0; i < body2.Length; i++)
            {
                body2[i] = (byte)(i + 16);
            }

            var collection = new BufferCollection
            {
                new ArraySegment<byte>(body1),
                new ArraySegment<byte>(body2)
            };

            await transport.SendChunkAsync(collection, CancellationToken.None).ConfigureAwait(false);

            byte[] receive = new byte[256];
            WebSocketReceiveResult result = await pair.Client
                .ReceiveAsync(new ArraySegment<byte>(receive), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.MessageType, Is.EqualTo(WebSocketMessageType.Binary));
            Assert.That(result.EndOfMessage, Is.True);
            int total = body1.Length + body2.Length;
            Assert.That(result, Has.Property(nameof(WebSocketReceiveResult.Count)).EqualTo(total));
            for (int i = 0; i < body1.Length; i++)
            {
                Assert.That(receive[i], Is.EqualTo(body1[i]));
            }
            for (int i = 0; i < body2.Length; i++)
            {
                Assert.That(receive[body1.Length + i], Is.EqualTo(body2[i]));
            }
        }

        [Test]
        public async Task CloseIsIdempotent()
        {
            using WebSocketPair pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);
            transport.Close();
            transport.Close();
            transport.Dispose();
        }

        /// <summary>
        /// <see cref="WebSocketServerByteTransport"/> exposes the
        /// <see cref="EndPoint"/> values passed to its constructor.
        /// </summary>
        [Test]
        public async Task ServerTransportEndpointPropertiesReturnConstructorValues()
        {
            using WebSocketPair pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            var local = new IPEndPoint(IPAddress.Loopback, 4840);
            var remote = new IPEndPoint(IPAddress.Loopback, 12345);

            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: local,
                remoteEndpoint: remote,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            Assert.That(transport.LocalEndpoint, Is.EqualTo(local));
            Assert.That(transport.RemoteEndpoint, Is.EqualTo(remote));
        }

        /// <summary>
        /// <see cref="WebSocketServerByteTransport.ConnectAsync"/> must
        /// throw <see cref="NotSupportedException"/> because the server
        /// transport is built from an already-accepted WebSocket.
        /// </summary>
        [Test]
        public async Task ServerTransportConnectAsyncThrowsNotSupported()
        {
            using WebSocketPair pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            Assert.ThrowsAsync<NotSupportedException>(
                async () => await transport.ConnectAsync(
                    new Uri("opc.wss://localhost:4840"),
                    CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// Sending on a closed <see cref="WebSocketServerByteTransport"/>
        /// must throw <see cref="StatusCodes.BadConnectionClosed"/>.
        /// </summary>
        [Test]
        public async Task SendChunkAsyncOnClosedTransportThrowsBadConnectionClosed()
        {
            using WebSocketPair pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            transport.Close();

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport.SendChunkAsync(
                    new byte[8].AsMemory(),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        /// <summary>
        /// <see cref="WebSocketServerByteTransport"/> must reject null
        /// WebSocket in the constructor.
        /// </summary>
        [Test]
        public void ServerTransportConstructorRejectsNullSocket()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _ = new WebSocketServerByteTransport(
                    null!,
                    localEndpoint: null,
                    remoteEndpoint: null,
                    m_bufferManager,
                    kBufferSize,
                    m_telemetry));
        }

        /// <summary>
        /// <see cref="WebSocketClientByteTransport.ConnectAsync"/> must
        /// throw <see cref="ArgumentNullException"/> when the URL is null.
        /// </summary>
        [Test]
        public void ClientTransportConnectAsyncRejectsNullUrl()
        {
            using var transport = new WebSocketClientByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await transport.ConnectAsync(null!, CancellationToken.None)
                    .ConfigureAwait(false));
        }

        /// <summary>
        /// <see cref="WebSocketClientByteTransport.LocalEndpoint"/> is
        /// always <c>null</c> (client sockets have no bound local address
        /// exposed via this property).
        /// </summary>
        [Test]
        public void ClientTransportLocalEndpointIsNull()
        {
            using var transport = new WebSocketClientByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            Assert.That(transport.LocalEndpoint, Is.Null);
        }

        /// <summary>
        /// <see cref="WebSocketClientByteTransport.RemoteEndpoint"/> is
        /// <c>null</c> before a successful <c>ConnectAsync</c>.
        /// </summary>
        [Test]
        public void ClientTransportRemoteEndpointIsNullBeforeConnect()
        {
            using var transport = new WebSocketClientByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            Assert.That(transport.RemoteEndpoint, Is.Null);
        }

        /// <summary>
        /// The Implementation and Features properties are set correctly.
        /// </summary>
        [Test]
        public async Task TransportImplementationAndFeaturesAreCorrect()
        {
            using WebSocketPair pair = await CreatePeeredWebSocketsAsync().ConfigureAwait(false);
            using var transport = new WebSocketServerByteTransport(
                pair.Server,
                localEndpoint: null,
                remoteEndpoint: null,
                m_bufferManager,
                kBufferSize,
                m_telemetry);

            Assert.That(transport.Implementation, Is.EqualTo("UA-WSS"));
            Assert.That(transport.Features, Is.EqualTo(TransportChannelFeatures.Reconnect));
        }

        /// <summary>
        /// Creates a pair of <see cref="WebSocket"/> instances connected
        /// to each other over a loopback TCP <see cref="NetworkStream"/>.
        /// The handshake is bypassed via <see cref="WebSocket.CreateFromStream"/>.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Listener is stopped in finally before returning; TCP clients are owned by the returned WebSocketPair.")]
        private static async Task<WebSocketPair> CreatePeeredWebSocketsAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();

                var clientTcp = new TcpClient();
                await clientTcp.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
                TcpClient serverTcp = await acceptTask.ConfigureAwait(false);

                var keepAlive = TimeSpan.FromSeconds(30);
                WebSocket server = WebSocket.CreateFromStream(
                    serverTcp.GetStream(),
                    isServer: true,
                    subProtocol: Profiles.OpcUaWsSubProtocolUacp,
                    keepAliveInterval: keepAlive);
                WebSocket client = WebSocket.CreateFromStream(
                    clientTcp.GetStream(),
                    isServer: false,
                    subProtocol: Profiles.OpcUaWsSubProtocolUacp,
                    keepAliveInterval: keepAlive);

                return new WebSocketPair(server, client, serverTcp, clientTcp);
            }
            finally
            {
                listener.Stop();
            }
        }

        private sealed class WebSocketPair : IDisposable
        {
            internal WebSocketPair(WebSocket server, WebSocket client, TcpClient serverTcp, TcpClient clientTcp)
            {
                Server = server;
                Client = client;
                m_serverTcp = serverTcp;
                m_clientTcp = clientTcp;
            }

            internal WebSocket Server { get; }
            internal WebSocket Client { get; }

            public void Dispose()
            {
                try
                {
                    Server.Dispose();
                }
                catch
                {
                }
                try
                {
                    Client.Dispose();
                }
                catch
                {
                }
                try
                {
                    m_serverTcp.Dispose();
                }
                catch
                {
                }
                try
                {
                    m_clientTcp.Dispose();
                }
                catch
                {
                }
            }

            private readonly TcpClient m_serverTcp;
            private readonly TcpClient m_clientTcp;
        }
    }
}

#endif // NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
