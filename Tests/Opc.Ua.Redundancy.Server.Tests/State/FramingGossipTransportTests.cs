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

#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crdt.Transport;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="FramingGossipTransport"/>: it wraps raw CRDT snapshots as
    /// <see cref="MessageType.State"/> frames on send and unwraps them on receive, so the CRDT stores stay
    /// wire-compatible with the frame-expecting TCP/UDP gossip transports.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class FramingGossipTransportTests
    {
        [Test]
        public void ConstructorRejectsNullInner()
        {
            Assert.That(() => new FramingGossipTransport(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task SendProducesDecodableStateFrameWrappingPayloadAsync()
        {
            var inner = new CapturingTransport();
            await using var framing = new FramingGossipTransport(inner);
            byte[] payload = [1, 2, 3, 4, 5];

            await framing.SendAsync(payload).ConfigureAwait(false);

            Assert.That(inner.LastSent, Is.Not.Null);
            // The raw store payload is NOT a valid frame; the decorator must have framed it.
            DecodedFrame decoded = FrameCodec.Decode(inner.LastSent!);
            Assert.That(decoded.MessageType, Is.EqualTo(MessageType.State));
            Assert.That(decoded.Payload.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task ReceiveUnwrapsStateFrameToPayloadAsync()
        {
            var inner = new CapturingTransport();
            await using var framing = new FramingGossipTransport(inner);
            byte[]? received = null;
            framing.FrameReceived += f => received = f.ToArray();
            byte[] payload = [9, 8, 7];

            inner.RaiseFrameReceived(FrameCodec.Encode(MessageType.State, payload));

            Assert.That(received, Is.EqualTo(payload));
        }

        [Test]
        public async Task ReceiveIgnoresNonStateFramesAsync()
        {
            var inner = new CapturingTransport();
            await using var framing = new FramingGossipTransport(inner);
            bool raised = false;
            framing.FrameReceived += _ => raised = true;

            // Transport control frames (e.g. acknowledgements) must not reach the CRDT store.
            inner.RaiseFrameReceived(FrameCodec.Encode(MessageType.Ack, default));
            // A malformed (unframed) buffer must be ignored rather than throw.
            inner.RaiseFrameReceived(new byte[] { 0xFF, 0xFF, 0xFF });

            Assert.That(raised, Is.False);
        }

        [Test]
        public async Task DisposeDisposesInnerAndUnsubscribesAsync()
        {
            var inner = new CapturingTransport();
            var framing = new FramingGossipTransport(inner);
            bool raised = false;
            framing.FrameReceived += _ => raised = true;

            await framing.DisposeAsync().ConfigureAwait(false);

            Assert.That(inner.Disposed, Is.True);
            // After dispose the decorator no longer forwards inner frames.
            inner.RaiseFrameReceived(FrameCodec.Encode(MessageType.State, [1]));
            Assert.That(raised, Is.False);
        }

        private sealed class CapturingTransport : ITransport
        {
            public event Action<ReadOnlyMemory<byte>>? FrameReceived;

            public byte[]? LastSent { get; private set; }

            public bool Disposed { get; private set; }

            public void RaiseFrameReceived(ReadOnlyMemory<byte> frame)
            {
                FrameReceived?.Invoke(frame);
            }

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
            {
                // Mirror the TCP/UDP transport contract: a sent buffer must be a valid frame.
                FrameCodec.Decode(frame);
                LastSent = frame.ToArray();
                return default;
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return default;
            }
        }
    }
}
