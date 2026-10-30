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
    /// Verifies the response gate and capsule framing over an HTTP/3 request stream.
    /// </summary>
    [TestFixture]
    public sealed class Http3CapsuleSessionTests
    {
        /// <summary>
        /// Writes the exact WebTransport Extended CONNECT pseudo-headers in an HTTP/3 HEADERS frame.
        /// </summary>
        [Test]
        public async Task WriteConnectRequestAsync_EmitsValidatedHeadersFrame()
        {
            await using var stream = new MemoryStream();

            await Http3CapsuleSession.WriteConnectRequestAsync(
                stream, "server.example", "/opcua/wt", "https://client.example", 256, CancellationToken.None);
            stream.Position = 0;
            Http3Frame? frame = await Http3StreamCodec.ReadFrameAsync(stream, 256, CancellationToken.None);

            Assert.That(frame.HasValue, Is.True);
            Assert.That(frame.Value.Type, Is.EqualTo(0x1));
            QpackDecodeStatus decodeStatus = QpackFieldSectionDecoder.TryDecode(
                frame.Value.Payload, 256, 1024, 16, out QpackHeaderField[] fields);
            Assert.That(decodeStatus, Is.EqualTo(QpackDecodeStatus.Success));
            Assert.That(
                Http3ExtendedConnectRequestParser.TryParse(fields, out Http3ExtendedConnectRequest request),
                Is.EqualTo(Http3ExtendedConnectRequestStatus.Success));
            Assert.That(request.Authority, Is.EqualTo("server.example"));
            Assert.That(request.Path, Is.EqualTo("/opcua/wt"));
            Assert.That(request.Origin, Is.EqualTo("https://client.example"));
        }

        /// <summary>
        /// Rejects an over-limit request field section before writing any frame bytes.
        /// </summary>
        [Test]
        public async Task WriteConnectRequestAsync_OverLimitDoesNotWrite()
        {
            await using var stream = new MemoryStream();

            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await Http3CapsuleSession.WriteConnectRequestAsync(
                    stream, "server.example", "/opcua/wt", null, 1, CancellationToken.None));

            Assert.That(stream.Length, Is.Zero);
        }

        /// <summary>
        /// Accepts only a successful response that signals Capsule Protocol.
        /// </summary>
        [Test]
        public async Task AcceptConnectResponse_RequiresSuccessAndCapsuleProtocol()
        {
            await using var success = new MemoryStream();
            Assert.That(
                QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(64, out byte[] response),
                Is.True);
            await Http3StreamCodec.WriteFrameAsync(
                success, 0x1, response, CancellationToken.None);
            success.Position = 0;

            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                success, 128, 128, 128, CancellationToken.None);

            Assert.That(session, Is.Not.Null);

            await using var missingCapsuleProtocol = new MemoryStream();
            byte[] statusOnly = [0x00, 0x00, 0xd9];
            await Http3StreamCodec.WriteFrameAsync(
                missingCapsuleProtocol, 0x1, statusOnly, CancellationToken.None);
            missingCapsuleProtocol.Position = 0;

            _ = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await Http3CapsuleSession.AcceptConnectResponseAsync(
                    missingCapsuleProtocol, 128, 128, 128, CancellationToken.None));

            await using var nonSuccess = new MemoryStream();
            response[2] = 0xda;
            await Http3StreamCodec.WriteFrameAsync(nonSuccess, 0x1, response, CancellationToken.None);
            nonSuccess.Position = 0;
            _ = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await Http3CapsuleSession.AcceptConnectResponseAsync(
                    nonSuccess, 128, 128, 128, CancellationToken.None));
        }

        /// <summary>
        /// Ignores a bounded interim response and accepts the successful final response.
        /// </summary>
        [Test]
        public async Task AcceptConnectResponse_InterimThenSuccessEstablishesSession()
        {
            await using var stream = new MemoryStream();
            byte[] interim = [0x00, 0x00, 0xd8];
            Assert.That(
                QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(64, out byte[] response),
                Is.True);
            await Http3StreamCodec.WriteFrameAsync(stream, 0x1, interim, CancellationToken.None);
            await Http3StreamCodec.WriteFrameAsync(stream, 0x1, response, CancellationToken.None);
            stream.Position = 0;

            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                stream, 128, 128, 128, CancellationToken.None);

            Assert.That(session, Is.Not.Null);
        }

        /// <summary>
        /// Reads a capsule whose bytes span multiple HTTP/3 DATA frames.
        /// </summary>
        [Test]
        public async Task ReadCapsuleAsync_CrossesDataFramesAndSkipsUnknownFrame()
        {
            await using var stream = await CreateSuccessfulResponseStreamAsync();
            await using var encodedCapsule = new MemoryStream();
            await Http3StreamCodec.WriteCapsuleAsync(
                encodedCapsule, 0x32, new byte[] { 0x11, 0x22, 0x33 }, CancellationToken.None);
            byte[] capsuleBytes = encodedCapsule.ToArray();
            await Http3StreamCodec.WriteFrameAsync(
                stream, 0x40, new byte[] { 0xaa }, CancellationToken.None);
            for (int offset = 0; offset < capsuleBytes.Length; offset += 2)
            {
                await Http3StreamCodec.WriteFrameAsync(
                    stream, 0x0, capsuleBytes.AsMemory(offset, Math.Min(2, capsuleBytes.Length - offset)),
                    CancellationToken.None);
            }
            stream.Position = 0;

            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                stream, 128, 2, 16, CancellationToken.None);
            WebTransportCapsule? capsule = await session.ReadCapsuleAsync(CancellationToken.None);

            Assert.That(capsule.HasValue, Is.True);
            Assert.That(capsule.Value.Type, Is.EqualTo(0x32));
            Assert.That(capsule.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
            Assert.That(await session.ReadCapsuleAsync(CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Splits an encoded capsule across bounded HTTP/3 DATA frames without changing its bytes.
        /// </summary>
        [Test]
        public async Task WriteCapsuleAsync_SplitsCapsuleAcrossBoundedDataFrames()
        {
            await using var stream = await CreateSuccessfulResponseStreamAsync();
            stream.Position = 0;
            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                stream, 128, 2, 16, CancellationToken.None);
            long responseEnd = stream.Position;

            await session.WriteCapsuleAsync(
                0x32, new byte[] { 0x11, 0x22, 0x33 }, CancellationToken.None);

            stream.Position = responseEnd;
            using var capsuleBytes = new MemoryStream();
            Http3Frame? frame;
            while ((frame = await Http3StreamCodec.ReadFrameAsync(
                stream, 2, CancellationToken.None)) is Http3Frame dataFrame)
            {
                Assert.That(dataFrame.Type, Is.Zero);
                Assert.That(dataFrame.Payload.Length, Is.LessThanOrEqualTo(2));
                byte[] payload = dataFrame.Payload.ToArray();
                await capsuleBytes.WriteAsync(payload);
            }

            capsuleBytes.Position = 0;
            WebTransportCapsule? capsule = await Http3StreamCodec.ReadCapsuleAsync(
                capsuleBytes, 16, CancellationToken.None);

            Assert.That(capsule.HasValue, Is.True);
            Assert.That(capsule.Value.Type, Is.EqualTo(0x32));
            Assert.That(capsule.Value.Payload.ToArray(), Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
            Assert.That(await Http3StreamCodec.ReadCapsuleAsync(
                capsuleBytes, 16, CancellationToken.None), Is.Null);
        }

        /// <summary>
        /// Rejects an oversized capsule before writing any DATA frame.
        /// </summary>
        [Test]
        public async Task WriteCapsuleAsync_OverLimitDoesNotWrite()
        {
            await using var stream = await CreateSuccessfulResponseStreamAsync();
            stream.Position = 0;
            await using Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                stream, 128, 128, 2, CancellationToken.None);
            long responseEnd = stream.Position;

            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await session.WriteCapsuleAsync(0x32, new byte[] { 1, 2, 3 }, CancellationToken.None));

            Assert.That(stream.Length, Is.EqualTo(responseEnd));
        }

        /// <summary>
        /// Allows the owning transport and its caller to dispose the session safely.
        /// </summary>
        [Test]
        public async Task DisposeAsync_CanBeCalledMoreThanOnce()
        {
            await using var stream = await CreateSuccessfulResponseStreamAsync();
            stream.Position = 0;
            Http3CapsuleSession session = await Http3CapsuleSession.AcceptConnectResponseAsync(
                stream, 128, 128, 128, CancellationToken.None);

            await session.DisposeAsync();
            await session.DisposeAsync();
        }

        private static async Task<MemoryStream> CreateSuccessfulResponseStreamAsync()
        {
            var stream = new MemoryStream();
            Assert.That(
                QpackConnectRequestEncoder.TryEncodeCapsuleConnectResponse(64, out byte[] response),
                Is.True);
            await Http3StreamCodec.WriteFrameAsync(stream, 0x1, response, CancellationToken.None);
            return stream;
        }
    }
}
