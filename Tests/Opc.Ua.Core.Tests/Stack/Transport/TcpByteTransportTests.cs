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

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Unit tests for the TCP-backed <see cref="TcpByteTransport"/>.
    /// Each test pairs a real loopback <see cref="Socket"/> with the
    /// transport so the chunk-framing, validation, and cancellation paths
    /// are exercised end-to-end without a full UA listener.
    /// </summary>
    [TestFixture]
    [Category("TcpByteTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TcpByteTransportTests
    {
        private const int kBufferSize = 8192;

        private ITelemetryContext m_telemetry = null!;
        private BufferManager m_bufferManager = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_bufferManager = new BufferManager("TcpByteTransportTests", kBufferSize, m_telemetry);
        }

        [Test]
        public async Task ConnectAsyncSucceedsAgainstLoopbackListener()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Task<Socket> acceptTask = listener.AcceptSocketAsync();

            using var transport = new TcpByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            var url = new Uri($"opc.tcp://127.0.0.1:{port}");
            await transport.ConnectAsync(url, CancellationToken.None).ConfigureAwait(false);

            using Socket serverSocket = await acceptTask.ConfigureAwait(false);
            Assert.That(transport.RemoteEndpoint, Is.Not.Null);
            Assert.That(transport.LocalEndpoint, Is.Not.Null);
        }

        [Test]
        public async Task SendChunkAsyncRoundTripsBytesOverTheSocket()
        {
            (TcpByteTransport client, Socket serverSocket, TcpListener listener) =
                await CreateConnectedPairAsync().ConfigureAwait(false);
            using var _l = listener;
            using var _s = serverSocket;
            using (client)
            {
                byte[] payload = BuildValidChunk(TcpMessageType.Hello, 64);
                await client.SendChunkAsync(payload, CancellationToken.None).ConfigureAwait(false);

                byte[] received = new byte[payload.Length];
                int total = 0;
                while (total < payload.Length)
                {
                    int n = await serverSocket
                        .ReceiveAsync(
                            new ArraySegment<byte>(received, total, payload.Length - total),
                            SocketFlags.None)
                        .ConfigureAwait(false);
                    Assert.That(n, Is.GreaterThan(0));
                    total += n;
                }
                Assert.That(received, Is.EqualTo(payload));
            }
        }

        [Test]
        public async Task ReceiveChunkAsyncReturnsCompleteChunk()
        {
            (TcpByteTransport client, Socket serverSocket, TcpListener listener) =
                await CreateConnectedPairAsync().ConfigureAwait(false);
            using var _l = listener;
            using var _s = serverSocket;
            using (client)
            {
                byte[] payload = BuildValidChunk(TcpMessageType.Acknowledge, 16);
                await serverSocket
                    .SendAsync(new ArraySegment<byte>(payload), SocketFlags.None)
                    .ConfigureAwait(false);

                ArraySegment<byte> chunk = await client
                    .ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(chunk, Has.Count.EqualTo(payload.Length));
                    byte[] copy = new byte[chunk.Count];
                    Buffer.BlockCopy(chunk.Array!, chunk.Offset, copy, 0, chunk.Count);
                    Assert.That(copy, Is.EqualTo(payload));
                }
                finally
                {
                    m_bufferManager.ReturnBuffer(chunk.Array!, nameof(ReceiveChunkAsyncReturnsCompleteChunk));
                }
            }
        }

        [Test]
        public async Task ReceiveChunkAsyncRejectsInvalidMessageType()
        {
            (TcpByteTransport client, Socket serverSocket, TcpListener listener) =
                await CreateConnectedPairAsync().ConfigureAwait(false);
            using var _l = listener;
            using var _s = serverSocket;
            using (client)
            {
                // Header with a bogus message type but a valid size.
                byte[] header = new byte[16];
                BitConverter.GetBytes(0xDEADBEEFu).CopyTo(header, 0);
                BitConverter.GetBytes(16).CopyTo(header, 4);
                await serverSocket
                    .SendAsync(new ArraySegment<byte>(header), SocketFlags.None)
                    .ConfigureAwait(false);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTypeInvalid));
            }
        }

        [Test]
        public async Task ReceiveChunkAsyncRejectsOversizeChunk()
        {
            (TcpByteTransport client, Socket serverSocket, TcpListener listener) =
                await CreateConnectedPairAsync().ConfigureAwait(false);
            using var _l = listener;
            using var _s = serverSocket;
            using (client)
            {
                byte[] header = new byte[8];
                BitConverter.GetBytes(TcpMessageType.Hello).CopyTo(header, 0);
                BitConverter.GetBytes(kBufferSize + 1).CopyTo(header, 4);
                await serverSocket
                    .SendAsync(new ArraySegment<byte>(header), SocketFlags.None)
                    .ConfigureAwait(false);

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTcpMessageTooLarge));
            }
        }

        [Test]
        public async Task ReceiveChunkAsyncReportsRemoteCloseAsBadConnectionClosed()
        {
            (TcpByteTransport client, Socket serverSocket, TcpListener listener) =
                await CreateConnectedPairAsync().ConfigureAwait(false);
            using var _l = listener;
            using (client)
            {
                // Cleanly close the server side before the client reads.
                serverSocket.Shutdown(SocketShutdown.Both);
                serverSocket.Dispose();

                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await client.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
            }
        }

        [Test]
        public void CloseIsIdempotent()
        {
            using var transport = new TcpByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            // Closing a never-connected transport is a no-op.
            transport.Close();
            transport.Close();
            transport.Dispose();
        }

        [Test]
        public void SendChunkAsyncOnDisconnectedTransportFails()
        {
            using var transport = new TcpByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            byte[] payload = BuildValidChunk(TcpMessageType.Hello, 16);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await transport
                    .SendChunkAsync(payload, CancellationToken.None)
                    .ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
        }

        private async Task<(TcpByteTransport, Socket, TcpListener)> CreateConnectedPairAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            Task<Socket> acceptTask = listener.AcceptSocketAsync();

            var transport = new TcpByteTransport(m_bufferManager, kBufferSize, m_telemetry);
            await transport
                .ConnectAsync(new Uri($"opc.tcp://127.0.0.1:{port}"), CancellationToken.None)
                .ConfigureAwait(false);
            Socket serverSocket = await acceptTask.ConfigureAwait(false);
            return (transport, serverSocket, listener);
        }

        private static byte[] BuildValidChunk(uint messageType, int size)
        {
            if (size < TcpMessageLimits.MessageTypeAndSize)
            {
                size = TcpMessageLimits.MessageTypeAndSize;
            }
            byte[] buffer = new byte[size];
            BitConverter.GetBytes(messageType).CopyTo(buffer, 0);
            BitConverter.GetBytes(size).CopyTo(buffer, 4);
            // Fill the body with deterministic data so the round-trip
            // assertions actually compare meaningful bytes.
            for (int i = 8; i < size; i++)
            {
                buffer[i] = (byte)(i & 0xFF);
            }
            return buffer;
        }
    }
}
