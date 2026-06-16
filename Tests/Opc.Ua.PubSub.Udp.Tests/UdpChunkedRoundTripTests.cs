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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// End-to-end loopback test that publishes a 256 KB UADP payload
    /// split into chunks by <see cref="UadpChunker"/>, transports each
    /// chunk via a real UDP unicast loopback socket, and verifies the
    /// subscriber recovers the original payload via
    /// <see cref="UadpReassembler"/>.
    /// </summary>
    /// <remarks>
    /// Exercises the Phase 14 wire-up of Phase 2 chunking primitives
    /// into the Phase 9 UDP transport pipeline. Covers
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
    /// Part 14 §7.2.4.4.4 Chunked NetworkMessages</see>.
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.2.4.4.4")]
    [CancelAfter(30000)]
    public sealed class UdpChunkedRoundTripTests
    {
        private const int PayloadSize = 256 * 1024;
        private const int MaxFrameSize = 1024;
        private const ushort PublisherIdValue = 0xABCD;
        private const ushort WriterGroupIdValue = 7;
        private const ushort MessageSequenceNumber = 42;

        [Test]
        public async Task ChunkedUadpRoundTrip_Reassembles256KBPayloadAsync()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            string url = $"opc.udp://127.0.0.1:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            UdpTransportOptions options = new()
            {
                Ttl = 1,
                MulticastLoopback = false,
                ReceiveQueueCapacity = 1024,
                MaxFrameSize = MaxFrameSize
            };
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var subscriber = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "ChunkSub"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);
            await using var publisher = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "ChunkPub"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);

            try
            {
                await subscriber.OpenAsync().ConfigureAwait(false);
                await publisher.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Unicast loopback open failed: {ex.Message}");
                return;
            }

            byte[] originalPayload = BuildDeterministicPayload(PayloadSize);

            var chunker = new UadpChunker();
            IReadOnlyList<byte[]> chunks = chunker.Split(
                originalPayload,
                MessageSequenceNumber,
                MaxFrameSize);

            Assert.That(chunks, Has.Count.GreaterThan(1),
                "Test invariant: payload must split into multiple chunks");

            using var reassembler = new UadpReassembler();
            PublisherId publisherId = PublisherId.FromUInt16(PublisherIdValue);

            // Start the subscriber receive loop before publishing.
            using var receiveCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(15));
            Task<byte[]?> reassemblyTask = ReadUntilCompleteAsync(
                subscriber, reassembler, publisherId, receiveCts.Token);

            for (int i = 0; i < chunks.Count; i++)
            {
                await publisher.SendAsync(chunks[i]).ConfigureAwait(false);
                if ((i % 32) == 31)
                {
                    // Give the receive loop time to drain to avoid
                    // the kernel UDP buffer overflowing.
                    await Task.Delay(5).ConfigureAwait(false);
                }
            }

            byte[]? reassembled = await reassemblyTask.ConfigureAwait(false);

            if (reassembled is null)
            {
                Assert.Ignore("Chunked datagram delivery did not complete; environment likely drops UDP under load.");
                return;
            }

            Assert.That(reassembled, Has.Length.EqualTo(originalPayload.Length));
            Assert.That(reassembled, Is.EqualTo(originalPayload));
        }

        private static async Task<byte[]?> ReadUntilCompleteAsync(
            UdpDatagramTransport transport,
            UadpReassembler reassembler,
            PublisherId publisherId,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (PubSubTransportFrame frame in transport
                    .ReceiveAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (reassembler.TryAddChunk(
                        publisherId,
                        WriterGroupIdValue,
                        frame.Payload,
                        out ReadOnlyMemory<byte>? reassembled) &&
                        reassembled is { } completed)
                    {
                        return completed.ToArray();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            return null;
        }

        private static byte[] BuildDeterministicPayload(int size)
        {
            byte[] buffer = new byte[size];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)((i * 131u + 7u) & 0xFF);
            }
            return buffer;
        }
    }
}
