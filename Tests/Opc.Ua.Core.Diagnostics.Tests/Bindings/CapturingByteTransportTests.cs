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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;

namespace Opc.Ua.Pcap.Tests.Bindings
{
    [TestFixture]
    public sealed class CapturingByteTransportTests
    {
        [Test]
        public void NotInstalledObserverIsNullAndHotPathPassesThrough()
        {
            var registry = new ChannelCaptureRegistry();
            Assert.That(registry.CurrentObserver, Is.Null);
        }

        [Test]
        public async Task SendForwardsToInnerAndTapsBytesWhenObserverIsRegisteredAsync()
        {
            var registry = new ChannelCaptureRegistry();
            using var inner = new RecordingByteTransport();
            var sink = new RecordingFrameCaptureSink();
            using var transport = new CapturingByteTransport(inner, registry);

            // No observer installed: forwarded but not tapped.
            await transport.SendChunkAsync(new byte[] { 1, 2, 3, 4 }, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inner.SentChunks, Has.Count.EqualTo(1));
            Assert.That(sink.SentChunks, Is.Empty);

            // Install observer; subsequent sends must tap.
            registry.SetObserver(sink);
            await transport.SendChunkAsync(new byte[] { 5, 6, 7 }, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(inner.SentChunks, Has.Count.EqualTo(2));
            Assert.That(sink.SentChunks, Has.Count.EqualTo(1));
            Assert.That(sink.SentChunks[0].ChannelId, Is.Zero);
            Assert.That(sink.SentChunks[0].Bytes, Is.EqualTo(new byte[] { 5, 6, 7 }));
        }

        [Test]
        public async Task ReceiveForwardsFromInnerAndTapsBytesWhenObserverIsRegisteredAsync()
        {
            var registry = new ChannelCaptureRegistry();
            using var inner = new RecordingByteTransport();
            var sink = new RecordingFrameCaptureSink();
            using var transport = new CapturingByteTransport(inner, registry);

            inner.EnqueueReceive([10, 20, 30]);
            ArraySegment<byte> received = await transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(received, Has.Count.EqualTo(3));
            Assert.That(sink.ReceivedChunks, Is.Empty);

            registry.SetObserver(sink);
            inner.EnqueueReceive([40, 50]);
            received = await transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(received, Has.Count.EqualTo(2));
            Assert.That(sink.ReceivedChunks, Has.Count.EqualTo(1));
            Assert.That(sink.ReceivedChunks[0].ChannelId, Is.Zero);
            Assert.That(sink.ReceivedChunks[0].Bytes, Is.EqualTo("(2"u8.ToArray()));
        }

        [Test]
        public void ConstructorRejectsNulls()
        {
            Assert.That(
                () => new CapturingByteTransport(null!, new ChannelCaptureRegistry()),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => new CapturingByteTransport(new RecordingByteTransport(), null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ImplementationStringIncludesPcapSuffix()
        {
            using var inner = new RecordingByteTransport();
            using var transport = new CapturingByteTransport(inner, new ChannelCaptureRegistry());
            Assert.That(transport.Implementation, Is.EqualTo("UA-TEST+pcap"));
        }

        private sealed class RecordingByteTransport : IUaSCByteTransport, IDisposable
        {
            public List<byte[]> SentChunks { get; } = [];
            private readonly Queue<byte[]> m_inbound = new();

            public string Implementation => "UA-TEST";
            public TransportChannelFeatures Features => TransportChannelFeatures.None;
            public EndPoint? LocalEndpoint => null;
            public EndPoint? RemoteEndpoint => null;

            public void EnqueueReceive(byte[] chunk)
            {
                m_inbound.Enqueue(chunk);
            }

            public ValueTask ConnectAsync(Uri url, CancellationToken ct)
            {
                return default;
            }

            public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
            {
                SentChunks.Add(chunk.ToArray());
                return default;
            }

            public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
            {
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
                SentChunks.Add(copy);
                return default;
            }

            public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
            {
                if (m_inbound.Count == 0)
                {
                    throw new InvalidOperationException("No inbound chunks queued.");
                }
                byte[] chunk = m_inbound.Dequeue();
                return new ValueTask<ArraySegment<byte>>(new ArraySegment<byte>(chunk));
            }

            public void Close()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingFrameCaptureSink : IFrameCaptureSink
        {
            public List<(uint ChannelId, byte[] Bytes)> SentChunks { get; } = [];
            public List<(uint ChannelId, byte[] Bytes)> ReceivedChunks { get; } = [];
            public List<(uint ChannelId, ChannelToken Current, ChannelToken? Previous)> Tokens { get; } = [];

            public void OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
            {
                SentChunks.Add((channelId, chunk.ToArray()));
            }

            public void OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
            {
                ReceivedChunks.Add((channelId, chunk.ToArray()));
            }

            public void OnTokenActivated(
                uint channelId,
                ChannelToken currentToken,
                ChannelToken? previousToken)
            {
                Tokens.Add((channelId, currentToken, previousToken));
            }
        }
    }
}
