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
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests for <see cref="RequestHandleReader"/> and the ServiceFault it feeds
    /// (OPC 10000-4 §7.33: the requestHandle should be echoed even for a request
    /// that is not valid).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RequestHandleReaderTests
    {
        private const uint kRequestHandle = 4711;
        private const int kDecodeMaxStringLength = 100;

        private ITelemetryContext m_telemetry = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FromBinaryReadsHandleOfRequestAboveMaxStringLength(bool stringAuthenticationToken)
        {
            ReadRequest request = CreateOversizedRequest(stringAuthenticationToken);
            byte[] message = BinaryEncoder.EncodeMessage(request, CreateContext(0));
            ServiceMessageContext decodeContext = CreateContext(kDecodeMaxStringLength);

            // the request itself cannot be decoded
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BinaryDecoder.DecodeMessage<IServiceRequest>(message, decodeContext))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));

            Assert.That(RequestHandleReader.FromBinary(message), Is.EqualTo(kRequestHandle));
            using var stream = new MemoryStream(message);
            Assert.That(RequestHandleReader.FromBinary(stream), Is.EqualTo(kRequestHandle));
        }

        /// <summary>
        /// The AuthenticationToken itself can be the field the decoder rejects;
        /// the reader skips it by its encoded length.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FromBinaryReadsHandleWhenAuthenticationTokenIsAboveLimits(bool byteStringToken)
        {
            ReadRequest request = CreateOversizedRequest(false);
            request.RequestHeader.AuthenticationToken = byteStringToken
                ? new NodeId(ByteString.From(new byte[10 * kDecodeMaxStringLength]), 2)
                : new NodeId(new string('t', 10 * kDecodeMaxStringLength), 2);
            byte[] message = BinaryEncoder.EncodeMessage(request, CreateContext(0));
            ServiceMessageContext decodeContext = CreateContext(kDecodeMaxStringLength);
            decodeContext.MaxByteStringLength = kDecodeMaxStringLength;

            Assert.That(
                () => BinaryDecoder.DecodeMessage<IServiceRequest>(message, decodeContext),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(RequestHandleReader.FromBinary(message), Is.EqualTo(kRequestHandle));
        }

        [Test]
        public void FromBinaryReadsHandleFromArraySegmentAndNonSeekableStream()
        {
            byte[] message = BinaryEncoder.EncodeMessage(CreateOversizedRequest(true), CreateContext(0));
            byte[] framed = new byte[message.Length + 16];
            message.CopyTo(framed, 8);

            Assert.That(
                RequestHandleReader.FromBinary(new ArraySegment<byte>(framed, 8, message.Length)),
                Is.EqualTo(kRequestHandle));
            Assert.That(RequestHandleReader.FromBinary(default(ArraySegment<byte>)), Is.Zero);
            using var nonSeekable = new NonSeekableStream(message);
            Assert.That(RequestHandleReader.FromBinary(nonSeekable), Is.EqualTo(kRequestHandle));
        }

        [TestCase(new byte[] { 0x40, 0x00 })]
        [TestCase(new byte[] { 0x80, 0x00 })]
        [TestCase(new byte[] { 0x06, 0x00 })]
        [TestCase(new byte[] { 0x03, 0x00, 0x00, 0xFE, 0xFF, 0xFF, 0xFF })]
        [TestCase(new byte[] { 0x03, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x41 })]
        public void FromBinaryReturnsZeroForInvalidNodeIdEncoding(byte[] message)
        {
            Assert.That(RequestHandleReader.FromBinary(message), Is.Zero);
        }

        [Test]
        public void FromBinaryReturnsZeroForFailingOrTruncatedNonSeekableStream()
        {
            byte[] message = BinaryEncoder.EncodeMessage(CreateOversizedRequest(true), CreateContext(0));

            using var throwing = new ThrowingStream();
            Assert.That(RequestHandleReader.FromBinary(throwing), Is.Zero);
            using var truncated = new NonSeekableStream(message.AsSpan(0, 10).ToArray());
            Assert.That(RequestHandleReader.FromBinary(truncated), Is.Zero);
            using var truncatedHandle = new NonSeekableStream(message.AsSpan(0, message.Length - 50).ToArray());
            Assert.That(RequestHandleReader.FromBinary(truncatedHandle), Is.EqualTo(kRequestHandle));
            // encoding id (four byte, 4) + token "session-token" (1 + 2 + 4 + 13)
            // + timestamp (8): the handle starts at offset 32.
            Assert.That(BitConverter.ToUInt32(message, 32), Is.EqualTo(kRequestHandle));
            using var shortHandle = new MemoryStream(message.AsSpan(0, 34).ToArray());
            Assert.That(RequestHandleReader.FromBinary(shortHandle), Is.Zero);
        }


        [TestCase(0)]
        [TestCase(3)]
        [TestCase(12)]
        public void FromBinaryReturnsZeroForTruncatedMessage(int length)
        {
            byte[] message = BinaryEncoder.EncodeMessage(CreateOversizedRequest(false), CreateContext(0));

            Assert.That(
                RequestHandleReader.FromBinary(message.AsSpan(0, length).ToArray()),
                Is.Zero);
        }

        [Test]
        public void FromBinaryReturnsZeroForNull()
        {
            Assert.That(RequestHandleReader.FromBinary((byte[]?)null), Is.Zero);
            Assert.That(RequestHandleReader.FromBinary((Stream?)null), Is.Zero);
        }

        [Test]
        public void FromJsonReadsHandleOfEncodedRequest()
        {
            ReadRequest request = CreateOversizedRequest(stringAuthenticationToken: true);

            foreach (JsonEncoderOptions options in new[] { JsonEncoderOptions.Compact, JsonEncoderOptions.Verbose })
            {
                using var memory = new MemoryStream();
                using (var encoder = new JsonEncoder(memory, CreateContext(0), options))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                }

                Assert.That(RequestHandleReader.FromJson(memory.ToArray()), Is.EqualTo(kRequestHandle));
            }
        }

        [TestCase("{\"UaTypeId\":\"i=629\",\"UaBody\":{\"RequestHeader\":{\"RequestHandle\":42}}}", 42u)]
        [TestCase("{\"UaBody\":{\"NodesToRead\":[],\"RequestHeader\":{\"Timestamp\":\"x\",\"RequestHandle\":42}},\"UaTypeId\":\"i=629\"}", 42u)]
        [TestCase("{\"UaBody\":{\"Other\":{\"RequestHandle\":1},\"RequestHeader\":{\"RequestHandle\":42}}}", 42u)]
        [TestCase("{\"UaBody\":{\"List\":[{\"RequestHeader\":{\"RequestHandle\":1}}],\"RequestHeader\":{\"RequestHandle\":42}}}", 42u)]
        [TestCase("{\"UaBody\":{\"RequestHeader\":{\"RequestHandle\":42}, broken", 42u)]
        [TestCase("{\"RequestHeader\":{\"RequestHandle\":42}}", 0u)]
        [TestCase("{\"Other\":{\"UaBody\":{\"RequestHeader\":{\"RequestHandle\":42}}}}", 0u)]
        [TestCase("{\"UaBody\":{\"RequestHandle\":42}}", 0u)]
        [TestCase("{\"UaBody\":{\"RequestHeader\":{\"RequestHandle\":\"42\"}}}", 0u)]
        [TestCase("{\"UaBody\":{\"RequestHeader\":{\"RequestHandle\":-1}}}", 0u)]
        [TestCase("{\"UaBody\": broken {\"RequestHeader\":{\"RequestHandle\":42}}}", 0u)]
        [TestCase("", 0u)]
        [TestCase("junk!", 0u)]
        public void FromJsonFindsOnlyTheRequestHeaderHandle(string json, uint expected)
        {
            Assert.That(RequestHandleReader.FromJson(Encoding.UTF8.GetBytes(json)), Is.EqualTo(expected));
        }

        [Test]
        public void CreateFaultEchoesRequestHandleOfUndecodedRequest()
        {
            ILogger logger = m_telemetry.CreateLogger<RequestHandleReaderTests>();
            DateTime before = DateTime.UtcNow.AddSeconds(-1);

            ServiceFault fault = EndpointBase.CreateFault(
                logger,
                null,
                new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded),
                kRequestHandle);

            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(kRequestHandle));
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That((DateTime)fault.ResponseHeader.Timestamp, Is.GreaterThan(before));

            // the handle of a decoded request takes precedence
            fault = EndpointBase.CreateFault(
                logger,
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 7 } },
                new ServiceResultException(StatusCodes.BadSecurityPolicyRejected),
                kRequestHandle);
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(7u));
        }

        private sealed class ThrowingStream : MemoryStream
        {
            public override int ReadByte()
            {
                throw new IOException("read failed");
            }
        }

        private sealed class NonSeekableStream : MemoryStream
        {
            public NonSeekableStream(byte[] buffer)
                : base(buffer, writable: false)
            {
            }

            public override bool CanSeek => false;
        }

        private ServiceMessageContext CreateContext(int maxStringLength)
        {
            ServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            context.MaxStringLength = maxStringLength;
            context.MaxMessageSize = 0;
            return context;
        }

        private static ReadRequest CreateOversizedRequest(bool stringAuthenticationToken)
        {
            return new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    AuthenticationToken = stringAuthenticationToken
                        ? new NodeId("session-token", 0)
                        : new NodeId(Guid.NewGuid(), 1),
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = kRequestHandle,
                    TimeoutHint = 10000
                },
                NodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(new string('n', 10 * kDecodeMaxStringLength), 1),
                        AttributeId = Attributes.Value
                    }
                ]
            };
        }
    }
}
