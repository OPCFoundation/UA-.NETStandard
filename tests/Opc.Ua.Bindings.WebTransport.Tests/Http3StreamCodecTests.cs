// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies reliable-stream framing for HTTP/3 frames and WebTransport capsules.
    /// </summary>
    [TestFixture]
    public sealed class Http3StreamCodecTests
    {
        /// <summary>
        /// Reads a frame whose header and payload arrive in separate short reads.
        /// </summary>
        [Test]
        public async Task ReadFrameAsync_FragmentedInput_ReturnsCompleteFrame()
        {
            await using var stream = new OneByteReadStream([0x00, 0x03, 0x01, 0x02, 0x03]);

            var frame = await Http3StreamCodec.ReadFrameAsync(stream, 3, CancellationToken.None);

            Assert.That(frame.HasValue, Is.True);
            Assert.That(frame.Value.Type, Is.Zero);
            Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>
        /// Treats EOF before a new record as clean closure but a partial length as malformed closure.
        /// </summary>
        [Test]
        public async Task ReadFrameAsync_EofDistinguishesCleanAndPartialHeader()
        {
            await using var empty = new MemoryStream();
            await using var partial = new MemoryStream([0x00]);

            Assert.That(await Http3StreamCodec.ReadFrameAsync(empty, 8, CancellationToken.None), Is.Null);
            _ = await Assert.ThrowsAsync<EndOfStreamException>(
                async () => await Http3StreamCodec.ReadFrameAsync(partial, 8, CancellationToken.None));
        }

        /// <summary>
        /// Rejects an over-limit declaration before allocating or consuming its payload.
        /// </summary>
        [Test]
        public async Task ReadFrameAsync_OversizedPayload_IsRejected()
        {
            await using var stream = new MemoryStream([0x00, 0x04, 0xaa, 0xbb, 0xcc, 0xdd]);

            _ = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await Http3StreamCodec.ReadFrameAsync(stream, 3, CancellationToken.None));
            Assert.That(stream.Position, Is.EqualTo(2));
        }

        /// <summary>
        /// Reads and writes capsule records with the same bounded reliable framing.
        /// </summary>
        [Test]
        public async Task CapsuleRoundTrip_PreservesTypeAndPayload()
        {
            await using var stream = new MemoryStream();
            byte[] payload = [0x11, 0x22, 0x33];

            await Http3StreamCodec.WriteCapsuleAsync(stream, 0x32, payload, CancellationToken.None);
            stream.Position = 0;
            var capsule = await Http3StreamCodec.ReadCapsuleAsync(stream, payload.Length, CancellationToken.None);

            Assert.That(capsule.HasValue, Is.True);
            Assert.That(capsule.Value.Type, Is.EqualTo(0x32));
            Assert.That(capsule.Value.Payload.ToArray(), Is.EqualTo(payload));
            Assert.That(await Http3StreamCodec.ReadCapsuleAsync(stream, 8, CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Writes HTTP/3 integer headers and payload bytes in their original order.
        /// </summary>
        [Test]
        public async Task WriteFrameAsync_EncodesHeaderAndPayload()
        {
            await using var stream = new MemoryStream();

            await Http3StreamCodec.WriteFrameAsync(stream, 0x21, new byte[] { 0xaa, 0xbb }, CancellationToken.None);

            Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 0x21, 0x02, 0xaa, 0xbb }));
        }

        private sealed class OneByteReadStream(byte[] content) : MemoryStream(content)
        {
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
            }
        }
    }
}
