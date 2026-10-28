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
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Core.DataChannels.Tests.ClientServer
{
    /// <summary>
    /// Proves reliable-stream framing and ownership independently of transport or authentication claims.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class UascStreamByteTransportTests
    {
        /// <summary>
        /// Every segmentation retains exact baseline UACP bytes, including MSG continuation and abort chunks.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(65536)]
        public async Task SegmentedAndCoalescedChunksPreserveAllBaselineMessageTypes(int segmentSize)
        {
            uint[] types =
            [
                TcpMessageType.Hello, TcpMessageType.ReverseHello, TcpMessageType.Acknowledge, TcpMessageType.Error,
                TcpMessageType.Open | TcpMessageType.Final,
                TcpMessageType.MessageIntermediate, TcpMessageType.Message | TcpMessageType.Abort,
                TcpMessageType.MessageFinal, TcpMessageType.Close | TcpMessageType.Final
            ];
            byte[][] expected = types.Select(type => Chunk(37, type)).ToArray();
            using var fixture = new Fixture([.. expected.SelectMany(bytes => bytes)], segmentSize);
            foreach (byte[] bytes in expected)
            {
                ArraySegment<byte> received = await fixture.Transport.ReceiveChunkAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    Assert.That(received.ToArray(), Is.EqualTo(bytes));
                    Assert.That(fixture.Pool.Outstanding, Is.EqualTo(1));
                }
                finally
                {
                    fixture.Buffers.ReturnBuffer(
                        received.Array, nameof(SegmentedAndCoalescedChunksPreserveAllBaselineMessageTypes));
                }
            }
            Assert.That(fixture.Pool.Outstanding, Is.Zero);
        }

        /// <summary>
        /// Superseded FLO/FLC headers are rejected before retaining any advertised application body.
        /// </summary>
        [TestCase(TcpMessageType.FlowData)]
        [TestCase(TcpMessageType.FlowControl)]
        public async Task SupersededFlowHeaderIsRejectedBeforeRenting(uint type)
        {
            using var fixture = new Fixture(Chunk(37, type | TcpMessageType.Final));
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTypeInvalid));
                Assert.That(fixture.Pool.TotalRented, Is.Zero);
                Assert.That(fixture.Stream.Position, Is.EqualTo(8));
            });
        }

        /// <summary>
        /// Malformed and oversize headers fail before body retention or a pooled allocation.
        /// </summary>
        [TestCase(8u, false)]
        [TestCase(65u, false)]
        [TestCase(uint.MaxValue, false)]
        [TestCase(64u, true)]
        public async Task InvalidHeaderIsRejectedBeforeRenting(uint length, bool invalidType)
        {
            byte[] header = Chunk(8);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), length);
            if (invalidType)
            {
                header[0] = 0xFF;
            }
            using var fixture = new Fixture(header, maximum: 64);
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(invalidType
                ? StatusCodes.BadTcpMessageTypeInvalid : StatusCodes.BadTcpMessageTooLarge));
            Assert.That(fixture.Pool.TotalRented, Is.Zero);
            Assert.That(fixture.Stream.Position, Is.EqualTo(8));
        }

        /// <summary>
        /// EOF at any partial header/body prefix cannot produce a partial chunk or leak rented storage.
        /// </summary>
        [Test]
        public async Task EveryTruncatedChunkReleasesItsBuffer()
        {
            byte[] chunk = Chunk(24);
            for (int length = 0; length < chunk.Length; length++)
            {
                using var fixture = new Fixture(chunk.AsSpan(0, length).ToArray(), 3);
                ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed), $"Prefix {length}.");
                Assert.That(fixture.Pool.Outstanding, Is.Zero, $"Prefix {length}.");
            }
        }

        /// <summary>
        /// The negotiated maximum is inclusive and can be lowered without reading the body of an oversized chunk.
        /// </summary>
        [Test]
        public async Task NegotiatedReceiveLimitIsInclusiveAndMutable()
        {
            byte[] chunk = Chunk(64);
            using var fixture = new Fixture([.. chunk, .. chunk], maximum: 64);
            ArraySegment<byte> received = await fixture.Transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            fixture.Buffers.ReturnBuffer(received.Array, nameof(NegotiatedReceiveLimitIsInclusiveAndMutable));
            ((IUaSCByteTransportLimits)fixture.Transport).SetReceiveBufferSize(63);
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.That(fixture.Pool.TotalRented, Is.EqualTo(1));
            Assert.That(fixture.Stream.Position, Is.EqualTo(72));
        }

        /// <summary>
        /// An interrupted fixed header is retained and resumed without retaining a maximum-sized body.
        /// </summary>
        [Test]
        public async Task CancelledPartialHeaderCanBeResumed()
        {
            byte[] chunk = Chunk(32);
            using var fixture = new Fixture(chunk, 3);
            fixture.Stream.CancelAt = 3;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(fixture.Pool.TotalRented, Is.Zero);
            fixture.Stream.CancelAt = -1;
            ArraySegment<byte> received = await fixture.Transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                Assert.That(received.ToArray(), Is.EqualTo(chunk));
            }
            finally
            {
                fixture.Buffers.ReturnBuffer(received.Array, nameof(CancelledPartialHeaderCanBeResumed));
            }
        }

        /// <summary>
        /// Consumed body bytes are never reinterpreted as another chunk after cancellation.
        /// </summary>
        [Test]
        public async Task CancelledBodyFaultsFurtherReadsAndReleasesItsBuffer()
        {
            using var fixture = new Fixture(Chunk(32), 4);
            fixture.Stream.CancelAt = 12;
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(fixture.Pool.Outstanding, Is.Zero);
            fixture.Stream.CancelAt = -1;
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadTcpInternalError));
            Assert.That(fixture.Stream.Position, Is.EqualTo(12));
        }

        /// <summary>
        /// A gathered chunk with a split header is written byte-for-byte, with no flow or record wrapper.
        /// </summary>
        [Test]
        public async Task GatheredAndContiguousSendsDoNotAddFraming()
        {
            byte[] chunk = Chunk(32, TcpMessageType.Hello);
            using var fixture = new Fixture([]);
            var gathered = new BufferCollection
            {
                new(chunk, 0, 3), new(chunk, 3, 4), new(chunk, 7, chunk.Length - 7)
            };
            await fixture.Transport.SendChunkAsync(gathered, CancellationToken.None).ConfigureAwait(false);
            await fixture.Transport.SendChunkAsync(chunk.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Stream.Written, Is.EqualTo(chunk.Concat(chunk).ToArray()));
            Assert.That(fixture.Pool.TotalRented, Is.Zero);
        }

        /// <summary>
        /// An encoded Service may gather several chunks, independently of the segment boundaries.
        /// </summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(19)]
        [TestCase(65536)]
        public async Task GatheredMultiChunkMessagesPreserveEveryBoundary(int segmentSize)
        {
            byte[] bytes =
            [
                .. Chunk(19, TcpMessageType.MessageIntermediate),
                .. Chunk(17, TcpMessageType.Message | TcpMessageType.Abort),
                .. Chunk(29, TcpMessageType.MessageFinal)
            ];
            using var fixture = new Fixture([]);
            var gathered = new BufferCollection();
            for (int offset = 0; offset < bytes.Length; offset += segmentSize)
            {
                gathered.Add(new ArraySegment<byte>(bytes, offset, Math.Min(segmentSize, bytes.Length - offset)));
            }

            await fixture.Transport.SendChunkAsync(gathered, CancellationToken.None).ConfigureAwait(false);

            Assert.That(fixture.Stream.Written, Is.EqualTo(bytes));
            Assert.That(fixture.Pool.TotalRented, Is.Zero);
        }

        /// <summary>
        /// A malformed later chunk is rejected before committing even a valid preceding chunk.
        /// </summary>
        [TestCase(1, false)]
        [TestCase(7, false)]
        [TestCase(8, false)]
        [TestCase(9, false)]
        [TestCase(15, false)]
        [TestCase(16, true)]
        public async Task GatheredInvalidTailIsRejectedBeforeWriting(int tailLength, bool invalidType)
        {
            byte[] valid = Chunk(16);
            byte[] tail = Chunk(16);
            if (invalidType)
            {
                tail[0] = 0xFF;
            }
            using var fixture = new Fixture([]);
            var gathered = new BufferCollection
            {
                new(valid), new(tail, 0, tailLength)
            };

            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(gathered, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(invalidType
                ? StatusCodes.BadTcpMessageTypeInvalid : StatusCodes.BadTcpMessageTooLarge));
            Assert.That(fixture.Stream.Written, Is.Empty);
            await fixture.Transport.SendChunkAsync(valid.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Stream.Written, Is.EqualTo(valid));
        }

        /// <summary>
        /// Invalid local chunks, empty batches and contiguous concatenations commit no bytes.
        /// </summary>
        [Test]
        public async Task InvalidSendsNeverWriteToTheStream()
        {
            using var fixture = new Fixture([]);
            byte[] chunk = Chunk(24);
            byte[] coalesced = [.. chunk, .. chunk];
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(coalesced.AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(new BufferCollection(), CancellationToken.None)
                    .ConfigureAwait(false))
                .ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(chunk.AsMemory(0, 23), CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(fixture.Stream.Written, Is.Empty);
        }

        /// <summary>
        /// A write failure may have committed bytes, so subsequent work cannot reuse that stream.
        /// </summary>
        [Test]
        public async Task FailedWriteClosesTheStreamInsteadOfRetryingAPartialChunk()
        {
            using var fixture = new Fixture([]);
            fixture.Stream.FailWrite = true;
            await Assert.ThrowsAsync<IOException>(async () =>
                await fixture.Transport.SendChunkAsync(Chunk(24).AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(fixture.Stream.Disposals, Is.EqualTo(1));
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Transport.SendChunkAsync(Chunk(24).AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
        }

        /// <summary>
        /// Closing pending I/O cancels the wait without disposing resources still in use.
        /// </summary>
        [Test]
        public async Task CloseCancelsPendingReceiveAndIsIdempotent()
        {
            using var fixture = new Fixture([]);
            fixture.Stream.WaitAtEnd = true;
            Task<ArraySegment<byte>> pending = fixture.Transport.ReceiveChunkAsync(CancellationToken.None).AsTask();
            Assert.That(pending.IsCompleted, Is.False);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            fixture.Transport.Close();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.ConfigureAwait(false)).ConfigureAwait(false);
            fixture.Transport.Close();
            Assert.That(fixture.Stream.Disposals, Is.EqualTo(1));
            Assert.That(fixture.Pool.TotalRented, Is.Zero);
        }

        /// <summary>
        /// Received buffers remain caller-owned after transport closure.
        /// </summary>
        [Test]
        public async Task ReceivedBufferOwnershipSurvivesTransportClose()
        {
            byte[] chunk = Chunk(24);
            using var fixture = new Fixture(chunk);
            ArraySegment<byte> received = await fixture.Transport.ReceiveChunkAsync(CancellationToken.None)
                .ConfigureAwait(false);
            fixture.Transport.Close();
            Assert.That(received.ToArray(), Is.EqualTo(chunk));
            Assert.That(fixture.Pool.Outstanding, Is.EqualTo(1));
            fixture.Buffers.ReturnBuffer(received.Array, nameof(ReceivedBufferOwnershipSurvivesTransportClose));
        }

        /// <summary>
        /// Construction failures leave stream ownership with the caller.
        /// </summary>
        [Test]
        public void InvalidConstructionDoesNotTakeStreamOwnership()
        {
            using var fixture = new Fixture([]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UascStreamByteTransport(fixture.Stream, fixture.Buffers, 8, "test"));
            Assert.Throws<ArgumentException>(() =>
                new UascStreamByteTransport(fixture.Stream, fixture.Buffers, 64, string.Empty));
            Assert.Throws<ArgumentNullException>(() =>
                new UascStreamByteTransport(fixture.Stream, null!, 64, "test"));
            Assert.That(fixture.Stream.Disposals, Is.Zero);
        }

        /// <summary>
        /// The ordinary UASC factory establishes a stream only on Connect and transfers its ownership once.
        /// </summary>
        [Test]
        public async Task ClientFactoryDefersConnectionAndWritesHelloWithoutAFlowEnvelope()
        {
            using var fixture = new Fixture([]);
            using var stream = new DuplexStream([], 3);
            var endpoint = new Uri("opc.quic://example.test:4840/");
            int connections = 0;
            var factory = new UascStreamByteTransportFactory((url, ct) =>
            {
                Assert.That(url, Is.EqualTo(endpoint));
                ct.ThrowIfCancellationRequested();
                connections++;
                return new ValueTask<Stream>(stream);
            }, "UA-QUIC");
            IUaSCByteTransport transport = factory.Create(fixture.Buffers, 64, fixture.Telemetry);
            try
            {
                Assert.That(connections, Is.Zero);
                await transport.ConnectAsync(endpoint, CancellationToken.None).ConfigureAwait(false);
                Assert.That(connections, Is.EqualTo(1));
                byte[] hello = Chunk(32, TcpMessageType.Hello);
                await transport.SendChunkAsync(hello.AsMemory(), CancellationToken.None).ConfigureAwait(false);
                Assert.That(stream.Written, Is.EqualTo(hello));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await transport.ConnectAsync(endpoint, CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(connections, Is.EqualTo(1));
            }
            finally
            {
                transport.Close();
            }
            Assert.That(stream.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// A connector completing after closure cannot resurrect the transport or leak its late stream.
        /// </summary>
        [Test]
        public async Task CloseDuringConnectionDisposesLateStream()
        {
            using var fixture = new Fixture([]);
            using var stream = new DuplexStream([], 3);
            var pending = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = new UascStreamByteTransportFactory((_, _) => new ValueTask<Stream>(pending.Task), "test");
            IUaSCByteTransport transport = factory.Create(fixture.Buffers, 64, fixture.Telemetry);
            Task connection = transport.ConnectAsync(
                new Uri("opc.quic://example.test:4840/"), CancellationToken.None).AsTask();
            transport.Close();
            pending.SetResult(stream);
            ServiceResultException? exception = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await connection.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            Assert.That(stream.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Work before connection fails explicitly and does not consume caller data.
        /// </summary>
        [Test]
        public async Task UnconnectedFactoryTransportRejectsIo()
        {
            using var fixture = new Fixture([]);
            var factory = new UascStreamByteTransportFactory((_, _) =>
                throw new InvalidOperationException("Connect must not run."), "test");
            IUaSCByteTransport transport = factory.Create(fixture.Buffers, 64, fixture.Telemetry);
            try
            {
                ServiceResultException? read = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await transport.ReceiveChunkAsync(CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                ServiceResultException? write = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await transport.SendChunkAsync(Chunk(24).AsMemory(), CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(read!.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
                Assert.That(write!.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
                Assert.That(fixture.Pool.TotalRented, Is.Zero);
            }
            finally
            {
                transport.Close();
            }
        }

        /// <summary>
        /// The stream transport forwards only an explicitly supplied session-scoped provider.
        /// </summary>
        [Test]
        public async Task DirectBindingProviderIsForwardedByStreamFactory()
        {
            using var fixture = new Fixture([]);
            var provider = new TestDirectBindingProvider();
            using var stream = new DuplexStream([], 1);
            var factory = new UascStreamByteTransportFactory(
                (_, _) => new ValueTask<Stream>(stream),
                "test",
                "urn:example:profile",
                provider);
            using var transport = (UascStreamByteTransport)factory.Create(
                fixture.Buffers, 64, fixture.Telemetry);

            Assert.That(transport.IsAvailable, Is.True);
            Assert.That(transport.TransportProfileUri, Is.EqualTo(provider.TransportProfileUri));
            Assert.That(transport.Capability, Is.EqualTo(provider.Capability));
            Assert.That(transport.Authority, Is.EqualTo(provider.Authority));
            Assert.That(transport.Path, Is.EqualTo(provider.Path));

            await transport.ConnectAsync(
                new Uri("opc.quic://example.test:4840/"), CancellationToken.None).ConfigureAwait(false);
            ByteString key = await transport.ExportKeyingMaterialAsync(
                new ByteString(new byte[32]), CancellationToken.None).ConfigureAwait(false);

            Assert.That(key.ToArray(), Is.EqualTo(provider.Key));
        }

        /// <summary>
        /// A stream without a provider refuses exporter requests instead of fabricating support.
        /// </summary>
        [Test]
        public async Task DirectBindingProviderIsUnsupportedByDefault()
        {
            using var fixture = new Fixture([]);
            UascStreamByteTransport provider = fixture.Transport;

            Assert.That(provider.IsAvailable, Is.False);
            DataChannelException? exception = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.ExportKeyingMaterialAsync(new ByteString(new byte[32])).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(exception!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            Assert.That(provider.AreDirectStreamsAvailable, Is.False);
            DataChannelException? open = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.OpenDirectStreamAsync(Guid.NewGuid()).ConfigureAwait(false)).ConfigureAwait(false);
            DataChannelException? accept = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.AcceptDirectStreamAsync(Guid.NewGuid()).ConfigureAwait(false)).ConfigureAwait(false);
            DataChannelException? datagram = await Assert.ThrowsAsync<DataChannelException>(async () =>
                await provider.ReceiveDirectDatagramAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(open!.Result, Is.EqualTo(DataChannelResult.Unsupported));
                Assert.That(accept!.Result, Is.EqualTo(DataChannelResult.Unsupported));
                Assert.That(provider.MaxDirectDatagramSize, Is.Zero);
                Assert.That(datagram!.Result, Is.EqualTo(DataChannelResult.Unsupported));
            });
        }

        /// <summary>
        /// Direct datagram capacity, complete payload and the exact commitment owner cross the byte adapter.
        /// </summary>
        [Test]
        public async Task DirectDatagramProviderForwardsCapacityPayloadAndCommitOwner()
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            Mock<IDataChannelDirectDatagramProvider> direct = provider.As<IDataChannelDirectDatagramProvider>();
            direct.SetupGet(p => p.MaxDirectDatagramSize).Returns(1200);
            ByteString payload = new(new byte[] { 1, 0, 2, 0xff });
            direct.Setup(p => p.ReceiveDirectDatagramAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ByteString>(payload));
            var write = new DataChannelWrite(DataChannelPath.Datagram, 1, 0, 0, payload, true, 0,
                static () => DataChannelResult.Good);
            direct.Setup(p => p.SendDirectDatagramAsync(write, It.IsAny<CancellationToken>()))
                .Returns((DataChannelWrite value, CancellationToken token) =>
                    new ValueTask<DataChannelResult>(value.TryCommit() ? DataChannelResult.Good : value.Result));
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Assert.That(await transport.SendDirectDatagramAsync(write).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            Assert.That(await transport.ReceiveDirectDatagramAsync().ConfigureAwait(false), Is.EqualTo(payload));
            Assert.Multiple(() =>
            {
                Assert.That(transport.MaxDirectDatagramSize, Is.EqualTo(1200));
                Assert.That(write.IsCommitted, Is.True);
            });
            direct.Verify(p => p.SendDirectDatagramAsync(write, It.IsAny<CancellationToken>()), Times.Once());
        }

        /// <summary>
        /// Pending direct datagram receive shares baseline cancellation and cannot survive closure.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task BaselineCloseCancelsOrRejectsLateDirectDatagram(bool providerHonorsCancellation)
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            Mock<IDataChannelDirectDatagramProvider> direct = provider.As<IDataChannelDirectDatagramProvider>();
            direct.SetupGet(p => p.MaxDirectDatagramSize).Returns(1200);
            var pending = new TaskCompletionSource<ByteString>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            direct.Setup(p => p.ReceiveDirectDatagramAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    observed = token;
                    return new ValueTask<ByteString>(providerHonorsCancellation
                        ? pending.Task.WaitAsync(token) : pending.Task);
                });
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Task<ByteString> receiving = transport.ReceiveDirectDatagramAsync().AsTask();
            transport.Close();
            pending.TrySetResult(new ByteString(new byte[] { 1 }));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await receiving.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed.IsCancellationRequested, Is.True);
                Assert.That(transport.MaxDirectDatagramSize, Is.Zero);
            });
            ServiceResultException? closed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await transport.ReceiveDirectDatagramAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(closed!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            direct.Verify(p => p.ReceiveDirectDatagramAsync(It.IsAny<CancellationToken>()), Times.Once());
        }

        /// <summary>
        /// The exact token and actual stream object cross the adapter without a numeric stream identity.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DirectStreamProviderForwardsTokenAndActualStream(bool open)
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            Mock<IDataChannelDirectStreamProvider> direct = provider.As<IDataChannelDirectStreamProvider>();
            direct.SetupGet(p => p.AreDirectStreamsAvailable).Returns(true);
            Guid token = Guid.NewGuid();
            using var expected = new DuplexStream([], 1);
            direct.Setup(p => p.OpenDirectStreamAsync(token, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(expected));
            direct.Setup(p => p.AcceptDirectStreamAsync(token, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(expected));
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Stream actual = open
                ? await transport.OpenDirectStreamAsync(token).ConfigureAwait(false)
                : await transport.AcceptDirectStreamAsync(token).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(transport.AreDirectStreamsAvailable, Is.True);
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(expected.Disposals, Is.Zero);
            });
            direct.Verify(p => p.OpenDirectStreamAsync(token, It.IsAny<CancellationToken>()),
                open ? Times.Once() : Times.Never());
            direct.Verify(p => p.AcceptDirectStreamAsync(token, It.IsAny<CancellationToken>()),
                open ? Times.Never() : Times.Once());
        }

        /// <summary>
        /// Baseline close cancels pending direct admission and refuses further provider calls.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task BaselineCloseCancelsDirectStreamAdmission(bool open)
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            provider.SetupGet(p => p.IsAvailable).Returns(true);
            Mock<IDataChannelDirectStreamProvider> direct = provider.As<IDataChannelDirectStreamProvider>();
            direct.SetupGet(p => p.AreDirectStreamsAvailable).Returns(true);
            var pending = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            ValueTask<Stream> Admit(Guid token, CancellationToken cancellationToken)
            {
                observed = cancellationToken;
                return new ValueTask<Stream>(pending.Task.WaitAsync(cancellationToken));
            }
            direct.Setup(p => p.OpenDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Admit);
            direct.Setup(p => p.AcceptDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Admit);
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Task<Stream> admitting = open
                ? transport.OpenDirectStreamAsync(Guid.NewGuid()).AsTask()
                : transport.AcceptDirectStreamAsync(Guid.NewGuid()).AsTask();
            Assert.That(admitting.IsCompleted, Is.False);
            transport.Close();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await admitting.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(observed.IsCancellationRequested, Is.True);
                Assert.That(transport.IsAvailable, Is.False);
                Assert.That(transport.AreDirectStreamsAvailable, Is.False);
            });
            ServiceResultException? closed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await transport.OpenDirectStreamAsync(Guid.NewGuid()).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(closed!.StatusCode, Is.EqualTo(StatusCodes.BadConnectionClosed));
            direct.Verify(p => p.OpenDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                open ? Times.Once() : Times.Never());
            direct.Verify(p => p.AcceptDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                open ? Times.Never() : Times.Once());
        }

        /// <summary>
        /// A provider completion racing baseline close cannot leak an abandoned actual stream.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task BaselineCloseDisposesLateDirectStream(bool open)
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            Mock<IDataChannelDirectStreamProvider> direct = provider.As<IDataChannelDirectStreamProvider>();
            var pending = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            direct.Setup(p => p.OpenDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(pending.Task));
            direct.Setup(p => p.AcceptDirectStreamAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Stream>(pending.Task));
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Task<Stream> admitting = open
                ? transport.OpenDirectStreamAsync(Guid.NewGuid()).AsTask()
                : transport.AcceptDirectStreamAsync(Guid.NewGuid()).AsTask();
            var late = new DuplexStream([], 1);
            transport.Close();
            pending.SetResult(late);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await admitting.ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(late.Disposals, Is.EqualTo(1));
        }

        /// <summary>
        /// Exporter requests share baseline cancellation instead of surviving its protected context.
        /// </summary>
        [Test]
        public async Task BaselineCloseCancelsPendingExporter()
        {
            using var fixture = new Fixture([]);
            var provider = new Mock<IDataChannelDirectBindingProvider>();
            var pending = new TaskCompletionSource<ByteString>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(p => p.ExportKeyingMaterialAsync(It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns((ByteString digest, CancellationToken cancellationToken) =>
                    new ValueTask<ByteString>(pending.Task.WaitAsync(cancellationToken)));
            using var transport = new UascStreamByteTransport(
                fixture.Stream, fixture.Buffers, 64, "test", directBindingProvider: provider.Object);
            Task<ByteString> exporting = transport.ExportKeyingMaterialAsync(new ByteString(new byte[32])).AsTask();
            Assert.That(exporting.IsCompleted, Is.False);
            transport.Close();
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await exporting.ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates framing bytes independently of the transport under test.
        /// </summary>
        private static byte[] Chunk(int length, uint type = TcpMessageType.MessageFinal)
        {
            byte[] chunk = new byte[length];
            BinaryPrimitives.WriteUInt32LittleEndian(chunk, type);
            BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)length);
            for (int index = 8; index < length; index++)
            {
                chunk[index] = (byte)index;
            }
            return chunk;
        }

        /// <summary>
        /// Owns each test's transport and verifies independent receive-pool accounting.
        /// </summary>
        private sealed class Fixture : IDisposable
        {
            internal Fixture(byte[] input, int segmentSize = int.MaxValue, int maximum = 65536)
            {
                m_telemetry = DefaultTelemetry.Create(_ => { });
                Pool = new TrackingPool();
                Buffers = new BufferManager("BaselineStreamTest", 65536, m_telemetry, Pool);
                Stream = new DuplexStream(input, segmentSize);
                Transport = new UascStreamByteTransport(Stream, Buffers, maximum, "test");
            }

            internal TrackingPool Pool { get; }

            internal BufferManager Buffers { get; }

            internal DuplexStream Stream { get; }

            internal UascStreamByteTransport Transport { get; }

            internal ITelemetryContext Telemetry => m_telemetry;

            public void Dispose()
            {
                Transport.Dispose();
                Assert.That(Pool.Outstanding, Is.Zero);
                (m_telemetry as IDisposable)?.Dispose();
            }

            private readonly ITelemetryContext m_telemetry;
        }

        /// <summary>
        /// Tracks pool ownership without allowing returned buffers to appear valid.
        /// </summary>
        private sealed class TrackingPool : ArrayPool<byte>
        {
            internal int Outstanding => m_outstanding.Count;

            internal int TotalRented { get; private set; }

            public override byte[] Rent(int minimumLength)
            {
                byte[] buffer = new byte[minimumLength];
                m_outstanding.Add(buffer);
                TotalRented++;
                return buffer;
            }

            public override void Return(byte[] array, bool clearArray = false)
            {
                Assert.That(m_outstanding.Remove(array), Is.True);
                array.AsSpan().Fill(0xCC);
            }

            private readonly HashSet<byte[]> m_outstanding = [];
        }

        /// <summary>
        /// Supplies independent segmented input and records writes without transport framing.
        /// </summary>
        private sealed class DuplexStream(byte[] input, int segmentSize) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => input.Length;

            public override long Position
            {
                get => m_position;
                set => throw new NotSupportedException();
            }

            internal byte[] Written => m_output.ToArray();

            internal int Disposals { get; private set; }

            internal int CancelAt { get; set; } = -1;

            internal bool FailWrite { get; set; }

            internal bool WaitAtEnd { get; set; }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ReadSegmentAsync(buffer.AsMemory(offset, count), cancellationToken);
            }

            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return WriteSegmentAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return new ValueTask<int>(ReadSegmentAsync(buffer, cancellationToken));
            }

            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return WriteSegmentAsync(buffer, cancellationToken);
            }
#endif

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Flush() => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                Disposals++;
                if (disposing)
                {
                    m_output.Dispose();
                }
                base.Dispose(disposing);
            }

            private async Task<int> ReadSegmentAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_position == CancelAt)
                {
                    throw new OperationCanceledException();
                }
                if (m_position == input.Length && WaitAtEnd)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                int copied = Math.Min(Math.Min(buffer.Length, segmentSize), input.Length - m_position);
                input.AsMemory(m_position, copied).CopyTo(buffer);
                m_position += copied;
                return copied;
            }

            private async ValueTask WriteSegmentAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (FailWrite)
                {
                    throw new IOException("Injected write failure.");
                }
                await m_output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            private readonly MemoryStream m_output = new();
            private int m_position;
        }

        private sealed class TestDirectBindingProvider : IDataChannelDirectBindingProvider
        {
            internal byte[] Key { get; } = Enumerable.Repeat((byte)0x35, 32).ToArray();

            public bool IsAvailable => true;

            public string TransportProfileUri => "urn:example:profile";

            public string Capability => "example-direct";

            public string Authority => "example.test:4840";

            public string Path => "/ua?x=1";

            public ValueTask<ByteString> ExportKeyingMaterialAsync(
                ByteString transcriptDigest,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ByteString>(new ByteString(Key.ToArray()));
            }
        }
    }
}
