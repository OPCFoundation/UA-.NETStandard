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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Worked-example custom implementation of <see cref="IUaSCByteTransport"/>
    /// referenced by <c>Docs/CustomTransport.md</c>. Demonstrates that the
    /// public byte-transport surface is sufficient to wire a brand-new
    /// transport into the UASC binary channel pipeline using only
    /// public API.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The implementation is an in-process loopback: two peers
    /// (<see cref="InProcessByteTransport"/> instances) share a pair of
    /// in-memory <see cref="System.Threading.Channels.Channel{T}"/>s, one
    /// for each direction. Send writes a chunk onto the outgoing channel;
    /// Receive reads the next chunk off the incoming channel.
    /// </para>
    /// <para>
    /// The example deliberately uses only types that exist in the public
    /// <c>Opc.Ua</c> / <c>Opc.Ua.Bindings</c> surface so it doubles as a
    /// contract validation for the <see cref="IUaSCByteTransport"/>
    /// extension point.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("CustomByteTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class InProcessTransportExample
    {
        [Test]
        public async Task PairedInProcessTransportsRoundTripChunksAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);

            (InProcessByteTransport client, InProcessByteTransport server) =
                InProcessByteTransport.CreatePair(buffers, receiveBufferSize: 8192, telemetry);

            try
            {
                byte[] payload = new byte[64];
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(i & 0xFF);
                }

                await client.SendChunkAsync(payload, CancellationToken.None).ConfigureAwait(false);

                ArraySegment<byte> received = await server
                    .ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(received, Has.Count.EqualTo(payload.Length));
                    byte[] copy = new byte[received.Count];
                    Buffer.BlockCopy(received.Array!, received.Offset, copy, 0, received.Count);
                    Assert.That(copy, Is.EqualTo(payload));
                }
                finally
                {
                    buffers.ReturnBuffer(received.Array!, nameof(PairedInProcessTransportsRoundTripChunksAsync));
                }
            }
            finally
            {
                client.Close();
                server.Close();
            }
        }

        [Test]
        public async Task ReceiveAfterCloseReportsBadConnectionClosedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var buffers = new BufferManager("inproc-test", 8192, telemetry);

            (InProcessByteTransport client, InProcessByteTransport server) =
                InProcessByteTransport.CreatePair(buffers, 8192, telemetry);

            try
            {
                client.Close();
                ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReceiveChunkAsync(CancellationToken.None)
                        .ConfigureAwait(false))!;
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConnectionClosed));
                await Task.CompletedTask.ConfigureAwait(false);
            }
            finally
            {
                server.Close();
            }
        }

        /// <summary>
        /// Reference implementation of <see cref="IUaSCByteTransport"/>
        /// suitable as a copy-paste starting point for downstream
        /// custom transports.
        /// </summary>
        public sealed class InProcessByteTransport : IUaSCByteTransport, IDisposable
        {
            /// <summary>
            /// Creates a connected pair of transports that read each
            /// other's writes.
            /// </summary>
            public static (InProcessByteTransport, InProcessByteTransport) CreatePair(
                BufferManager buffers,
                int receiveBufferSize,
                ITelemetryContext telemetry)
            {
                var aToB = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
                var bToA = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
                var a = new InProcessByteTransport(buffers, receiveBufferSize, telemetry, bToA.Reader, aToB.Writer);
                var b = new InProcessByteTransport(buffers, receiveBufferSize, telemetry, aToB.Reader, bToA.Writer);
                return (a, b);
            }

            private InProcessByteTransport(
                BufferManager buffers,
                int receiveBufferSize,
                ITelemetryContext telemetry,
                ChannelReader<byte[]> inbound,
                ChannelWriter<byte[]> outbound)
            {
                m_buffers = buffers;
                m_receiveBufferSize = receiveBufferSize;
                m_telemetry = telemetry;
                m_inbound = inbound;
                m_outbound = outbound;
            }

            public string Implementation => "UA-INPROC";
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
                => throw new NotSupportedException("Use CreatePair to wire two in-process transports.");

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                ThrowIfClosed();
                byte[] copy = chunk.ToArray();
                if (!m_outbound.TryWrite(copy))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Outbound channel closed.");
                }
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
                ThrowIfClosed();
                int total = buffers.TotalSize;
                byte[] copy = new byte[total];
                int offset = 0;
                foreach (ArraySegment<byte> segment in buffers)
                {
                    if (segment.Array == null)
                    {
                        continue;
                    }
                    Buffer.BlockCopy(segment.Array, segment.Offset, copy, offset, segment.Count);
                    offset += segment.Count;
                }
                if (!m_outbound.TryWrite(copy))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Outbound channel closed.");
                }
                return default;
            }

            public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                try
                {
                    byte[] payload = await m_inbound.ReadAsync(ct).ConfigureAwait(false);
                    byte[] buffer = m_buffers.TakeBuffer(m_receiveBufferSize, nameof(ReceiveChunkAsync));
                    if (payload.Length > buffer.Length)
                    {
                        m_buffers.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "In-process chunk exceeds receive buffer size.");
                    }
                    Buffer.BlockCopy(payload, 0, buffer, 0, payload.Length);
                    return new ArraySegment<byte>(buffer, 0, payload.Length);
                }
                catch (System.Threading.Channels.ChannelClosedException)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "In-process transport peer closed.");
                }
            }

            public void Close()
            {
                if (Interlocked.Exchange(ref m_closed, 1) != 0)
                {
                    return;
                }
                m_outbound.TryComplete();
            }

            public void Dispose() => Close();

            private void ThrowIfClosed()
            {
                if (Volatile.Read(ref m_closed) != 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "Transport is closed.");
                }
            }

            private readonly BufferManager m_buffers;
            private readonly int m_receiveBufferSize;
            private readonly ITelemetryContext m_telemetry;
            private readonly ChannelReader<byte[]> m_inbound;
            private readonly ChannelWriter<byte[]> m_outbound;
            private int m_closed;
        }
    }
}
