// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies the bounded byte-stream mapping over WebTransport capsules.
    /// </summary>
    [TestFixture]
    public sealed class WebTransportCapsuleByteStreamTests
    {
        /// <summary>
        /// Delivers stream-zero data, handles FIN, and replenishes consumed receive credit.
        /// </summary>
        [Test]
        public async Task ReadAsync_DeliversDataAndFinAndReplenishesCredit()
        {
            using var input = new MemoryStream();
            await WriteSuccessfulResponseAsync(input);
            await WriteDataFrameCapsuleAsync(input, MaxDataCapsuleType, [16]);
            await WriteDataFrameCapsuleAsync(input, MaxStreamDataCapsuleType, [0, 16]);
            await WriteDataFrameCapsuleAsync(input, StreamDataCapsuleType, [0, 1, 2, 3]);
            await WriteDataFrameCapsuleAsync(input, StreamFinCapsuleType, [0]);
            input.Position = 0;
            using var output = new MemoryStream();
            await using var duplex = new DuplexTestStream(input, output);
            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                duplex, 128, 128, 128, CancellationToken.None);
            await using var byteStream = await WebTransportCapsuleByteStream.CreateAsync(
                session, 128, 16, CancellationToken.None);

            byte[] received = new byte[3];
            await byteStream.ReadExactlyAsync(received);
            Assert.That(received, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(await byteStream.ReadAsync(received), Is.Zero);
            Assert.That(await byteStream.ReadAsync(received), Is.Zero);

            output.Position = 0;
            using var capsulePayloads = new MemoryStream();
            Http3Frame? frame;
            while ((frame = await Http3StreamCodec.ReadFrameAsync(
                output, 128, CancellationToken.None)) is Http3Frame dataFrame)
            {
                Assert.That(dataFrame.Type, Is.Zero);
                await capsulePayloads.WriteAsync(dataFrame.Payload.ToArray());
            }

            capsulePayloads.Position = 0;
            WebTransportCapsule? maxData = await Http3StreamCodec.ReadCapsuleAsync(
                capsulePayloads, 128, CancellationToken.None);
            WebTransportCapsule? maxStreamData = await Http3StreamCodec.ReadCapsuleAsync(
                capsulePayloads, 128, CancellationToken.None);
            WebTransportCapsule? replenishedData = await Http3StreamCodec.ReadCapsuleAsync(
                capsulePayloads, 128, CancellationToken.None);
            WebTransportCapsule? replenishedStreamData = await Http3StreamCodec.ReadCapsuleAsync(
                capsulePayloads, 128, CancellationToken.None);

            Assert.That(maxData?.Type, Is.EqualTo(MaxDataCapsuleType));
            Assert.That(ReadVarInt(maxData.Value.Payload.ToArray()), Is.EqualTo(16));
            Assert.That(maxStreamData?.Type, Is.EqualTo(MaxStreamDataCapsuleType));
            Assert.That(ReadStreamCredit(maxStreamData.Value.Payload.ToArray()), Is.EqualTo(16));
            Assert.That(replenishedData?.Type, Is.EqualTo(MaxDataCapsuleType));
            Assert.That(ReadVarInt(replenishedData.Value.Payload.ToArray()), Is.EqualTo(19));
            Assert.That(replenishedStreamData?.Type, Is.EqualTo(MaxStreamDataCapsuleType));
            Assert.That(ReadStreamCredit(replenishedStreamData.Value.Payload.ToArray()), Is.EqualTo(19));
            Assert.That(await Http3StreamCodec.ReadCapsuleAsync(
                capsulePayloads, 128, CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Rejects stream data that exceeds the advertised stream-zero receive credit.
        /// </summary>
        [Test]
        public async Task ReadAsync_RejectsDataBeyondReceiveCredit()
        {
            using var input = new MemoryStream();
            await WriteSuccessfulResponseAsync(input);
            await WriteDataFrameCapsuleAsync(input, MaxDataCapsuleType, [8]);
            await WriteDataFrameCapsuleAsync(input, MaxStreamDataCapsuleType, [0, 8]);
            await WriteDataFrameCapsuleAsync(input, StreamDataCapsuleType, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
            input.Position = 0;
            using var output = new MemoryStream();
            await using var duplex = new DuplexTestStream(input, output);
            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                duplex, 128, 128, 128, CancellationToken.None);
            await using var byteStream = await WebTransportCapsuleByteStream.CreateAsync(
                session, 128, 8, CancellationToken.None);

            ChannelClosedException exception = await Assert.ThrowsAsync<ChannelClosedException>(async () =>
                await byteStream.ReadExactlyAsync(new byte[1]));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidDataException>());
        }

        private static async Task WriteSuccessfulResponseAsync(Stream stream)
        {
            Assert.That(
                QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(64, out byte[] response),
                Is.True);
            await Http3StreamCodec.WriteFrameAsync(stream, 0x1, response, CancellationToken.None);
        }

        private static async Task WriteDataFrameCapsuleAsync(
            Stream stream,
            ulong type,
            byte[] payload)
        {
            using var capsule = new MemoryStream();
            await Http3StreamCodec.WriteCapsuleAsync(capsule, type, payload, CancellationToken.None);
            await Http3StreamCodec.WriteFrameAsync(stream, 0x0, capsule.ToArray(), CancellationToken.None);
        }

        private static ulong ReadVarInt(byte[] payload)
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(payload));
            Assert.That(Http3VarInt.TryRead(ref reader, out ulong value), Is.True);
            Assert.That(reader.Remaining, Is.Zero);
            return value;
        }

        private static ulong ReadStreamCredit(byte[] payload)
        {
            SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(payload));
            Assert.That(Http3VarInt.TryRead(ref reader, out ulong streamId), Is.True);
            Assert.That(streamId, Is.Zero);
            Assert.That(Http3VarInt.TryRead(ref reader, out ulong maximum), Is.True);
            Assert.That(reader.Remaining, Is.Zero);
            return maximum;
        }

        private const ulong StreamDataCapsuleType = 0x190B4D3C;
        private const ulong StreamFinCapsuleType = 0x190B4D3B;
        private const ulong MaxDataCapsuleType = 0x190B4D3D;
        private const ulong MaxStreamDataCapsuleType = 0x190B4D3E;

        private sealed class DuplexTestStream : Stream
        {
            public DuplexTestStream(Stream input, Stream output)
            {
                m_input = input;
                m_output = output;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) =>
                m_input.Read(buffer, offset, count);
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                m_input.ReadAsync(buffer, cancellationToken);
            public override void Write(byte[] buffer, int offset, int count) =>
                m_output.Write(buffer, offset, count);
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                m_output.WriteAsync(buffer, cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_input.Dispose();
                    m_output.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly Stream m_input;
            private readonly Stream m_output;
        }
    }
}
