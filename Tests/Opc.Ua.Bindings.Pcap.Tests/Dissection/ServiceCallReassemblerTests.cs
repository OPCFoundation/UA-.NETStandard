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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Dissection;
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Tests.Dissection
{
    /// <summary>
    /// Tests for the <see cref="ServiceCallReassembler"/> public surface —
    /// input validation, control-message paths, and asymmetric
    /// OpenSecureChannel reporting (none of which require loaded key
    /// material).
    /// </summary>
    [TestFixture]
    public sealed class ServiceCallReassemblerTests
    {
        [Test]
        public void ConstructorRejectsNullLoggerFactory()
        {
            Assert.That(
                () => new ServiceCallReassembler(loggerFactory: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("loggerFactory"));
        }

        [Test]
        public void DefaultConstructorDoesNotThrow()
        {
            ServiceCallReassembler reassembler = new();

            // Brand-new reassembler reports no completed calls.
            IReadOnlyList<DecodedServiceCall> empty = reassembler.DrainCompleted();
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void LoadKeyMaterialRejectsNull()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);

            Assert.That(
                () => reassembler.LoadKeyMaterial(material: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("material"));
        }

        [Test]
        public void ProcessAllRejectsNullFrames()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);

            Assert.That(
                () => reassembler.ProcessAll(frames: null!),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("frames"));
        }

        [Test]
        public void ProcessAllAsyncRejectsNullFrames()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);

            Assert.That(
                async () => await reassembler.ProcessAllAsync(
                    frames: null!,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("frames"));
        }

        [Test]
        public void PushIgnoresFrameShorterThanFourBytes()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.ClientToServer,
                "client:1",
                "server:1",
                new byte[] { 1, 2, 3 });

            reassembler.Push(frame);

            Assert.That(reassembler.DrainCompleted(), Is.Empty,
                "A frame shorter than the 4-byte message-type marker must be skipped.");
        }

        [Test]
        public void PushIgnoresFrameWithInvalidMessageType()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, 0xDEADBEEF);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.ClientToServer,
                "client:1",
                "server:1",
                payload);

            reassembler.Push(frame);

            Assert.That(reassembler.DrainCompleted(), Is.Empty,
                "Frames whose first 4 bytes do not encode a recognised TcpMessageType must be ignored.");
        }

        [Test]
        public void PushIgnoresFrameWithUnknownDirection()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = BuildOpnChunk(channelId: 1, tokenId: 2, size: 64);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.Unknown,
                clientEndpoint: string.Empty,
                serverEndpoint: string.Empty,
                payload);

            reassembler.Push(frame);

            Assert.That(reassembler.DrainCompleted(), Is.Empty,
                "Frames with Unknown direction must be skipped — we cannot decide " +
                "client vs server keys.");
        }

        [Test]
        public void PushOpenChunkFromClientProducesOpenSecureChannelRequestCall()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = BuildOpnChunk(channelId: 0x0A0B0C0D, tokenId: 0x12345678, size: 64);
            var timestamp = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
            var frame = new CaptureFrame(
                timestamp,
                CaptureFrameDirection.ClientToServer,
                clientEndpoint: "10.0.0.1:55000",
                serverEndpoint: "10.0.0.2:4840",
                payload);

            reassembler.Push(frame);
            IReadOnlyList<DecodedServiceCall> completed = reassembler.DrainCompleted();

            Assert.That(completed, Has.Count.EqualTo(1));
            DecodedServiceCall call = completed[0];
            Assert.That(call.RequestName, Is.EqualTo("OpenSecureChannelRequest"));
            Assert.That(call.ChannelId, Is.EqualTo(0x0A0B0C0DU));
            Assert.That(call.TokenId, Is.EqualTo(0x12345678U));
            Assert.That(call.RequestTimestamp, Is.EqualTo(timestamp));
            Assert.That(call.RequestBodySize, Is.EqualTo(payload.Length));
            Assert.That(call.RequestSummary, Is.EqualTo("(asymmetric OpenSecureChannel chunk)"));
        }

        [Test]
        public void PushOpenChunkFromServerProducesOpenSecureChannelResponseCall()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = BuildOpnChunk(channelId: 0x99999999, tokenId: 0x77777777, size: 64);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.ServerToClient,
                string.Empty,
                string.Empty,
                payload);

            reassembler.Push(frame);
            IReadOnlyList<DecodedServiceCall> completed = reassembler.DrainCompleted();

            Assert.That(completed, Has.Count.EqualTo(1));
            Assert.That(completed[0].RequestName, Is.EqualTo("OpenSecureChannelResponse"));
        }

        [Test]
        public void DrainCompletedClearsInternalList()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = BuildOpnChunk(channelId: 1, tokenId: 2, size: 64);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.ClientToServer,
                string.Empty,
                string.Empty,
                payload);

            reassembler.Push(frame);
            IReadOnlyList<DecodedServiceCall> first = reassembler.DrainCompleted();
            IReadOnlyList<DecodedServiceCall> second = reassembler.DrainCompleted();

            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(second, Is.Empty,
                "DrainCompleted must clear the internal list so the next call sees " +
                "only freshly completed entries.");
        }

        [Test]
        public void ProcessAllFeedsEveryFrameThenReturnsCompleted()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            var frames = new List<CaptureFrame>
            {
                MakeOpenFrame(channelId: 1, tokenId: 10, fromClient: true),
                MakeOpenFrame(channelId: 2, tokenId: 20, fromClient: false)
            };

            IReadOnlyList<DecodedServiceCall> calls = reassembler.ProcessAll(frames);

            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(calls[0].ChannelId, Is.EqualTo(1U));
            Assert.That(calls[0].TokenId, Is.EqualTo(10U));
            Assert.That(calls[0].RequestName, Is.EqualTo("OpenSecureChannelRequest"));
            Assert.That(calls[1].ChannelId, Is.EqualTo(2U));
            Assert.That(calls[1].TokenId, Is.EqualTo(20U));
            Assert.That(calls[1].RequestName, Is.EqualTo("OpenSecureChannelResponse"));
        }

        [Test]
        public async Task ProcessAllAsyncFeedsEveryFrameThenReturnsCompleted()
        {
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            var frames = new List<CaptureFrame>
            {
                MakeOpenFrame(channelId: 5, tokenId: 6, fromClient: true),
                MakeOpenFrame(channelId: 7, tokenId: 8, fromClient: true)
            };

            IReadOnlyList<DecodedServiceCall> calls = await reassembler.ProcessAllAsync(
                AsAsyncEnumerable(frames),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(calls[0].ChannelId, Is.EqualTo(5U));
            Assert.That(calls[1].ChannelId, Is.EqualTo(7U));
        }

        [Test]
        public void PushUnknownMessageTypeBaseDoesNotThrow()
        {
            // A 4-byte type that passes IsValid but whose masked base is none
            // of HEL/ACK/ERR/RHE/OPN/MSG/CLO — defensively ensure nothing
            // crashes. The Final-flag (0x46000000) combined with a fabricated
            // 3-byte tail "ZZZ" yields 0x465A5A5A which IsValid rejects, so
            // build a recognized-but-unhandled type instead: Hello.
            // NOTE: due to the bug in Push() (mask vs case label mismatch),
            // Hello control messages produce no DecodedServiceCall today.
            // The test pins the current behaviour so we can spot regressions
            // — if the production bug is fixed, this assertion needs to flip
            // to expect a completed control-message entry.
            var reassembler = new ServiceCallReassembler(NullLoggerFactory.Instance);
            byte[] payload = new byte[32];
            // Hello = 0x464C4548 ("HELF" with little-endian read).
            BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x464C4548U);
            var frame = new CaptureFrame(
                DateTimeOffset.UtcNow,
                CaptureFrameDirection.ClientToServer,
                string.Empty,
                string.Empty,
                payload);

            Assert.That(() => reassembler.Push(frame), Throws.Nothing);
            Assert.That(reassembler.DrainCompleted(), Is.Empty,
                "Current production code drops Hello/Ack/Error/ReverseHello because " +
                "ServiceCallReassembler.Push masks the type with MessageTypeMask but " +
                "compares against the un-masked case labels. Document the behaviour.");
        }

        // ---- helpers ----

        private static byte[] BuildOpnChunk(uint channelId, uint tokenId, int size)
        {
            if (size < 16)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            byte[] data = new byte[size];
            // "OPNF" little-endian = 0x464E504F (Open + Final flag).
            BinaryPrimitives.WriteUInt32LittleEndian(data, 0x464E504FU);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)size);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), channelId);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), tokenId);
            return data;
        }

        private static CaptureFrame MakeOpenFrame(uint channelId, uint tokenId, bool fromClient)
        {
            return new CaptureFrame(
                DateTimeOffset.UtcNow,
                fromClient ? CaptureFrameDirection.ClientToServer : CaptureFrameDirection.ServerToClient,
                string.Empty,
                string.Empty,
                BuildOpnChunk(channelId, tokenId, size: 32));
        }

        private static async IAsyncEnumerable<CaptureFrame> AsAsyncEnumerable(IEnumerable<CaptureFrame> source)
        {
            foreach (CaptureFrame frame in source)
            {
                yield return frame;
                await Task.Yield();
            }
        }
    }
}
