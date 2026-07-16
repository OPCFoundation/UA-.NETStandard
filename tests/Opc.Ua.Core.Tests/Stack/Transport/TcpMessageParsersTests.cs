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
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests for the stateless UA-TCP / UA-SC chunk parsers in
    /// <see cref="TcpMessageParsers"/>.
    /// </summary>
    [TestFixture]
    [Category("TcpMessageParsersTests")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TcpMessageParsersTests
    {
        [Test]
        public void TryParseChunkHeaderAcceptsValidHeaderAndRejectsInvalidInputs()
        {
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), TcpMessageType.Hello | TcpMessageType.Final);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 32);

            bool parsed = TcpMessageParsers.TryParseChunkHeader(
                header,
                out uint messageType,
                out uint chunkType,
                out uint messageSize);

            Assert.That(parsed, Is.True);
            Assert.That(messageType, Is.EqualTo(TcpMessageType.Hello | TcpMessageType.Final));
            Assert.That(chunkType, Is.EqualTo(TcpMessageType.Final));
            Assert.That(messageSize, Is.EqualTo(32));
            Assert.That(TcpMessageParsers.TryParseChunkHeader(header.AsSpan(0, 7), out _, out _, out _), Is.False);

            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), 0x00FFFFFFu);
            Assert.That(TcpMessageParsers.TryParseChunkHeader(header, out _, out _, out _), Is.False);

            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), TcpMessageType.Hello | TcpMessageType.Final);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 4);
            Assert.That(TcpMessageParsers.TryParseChunkHeader(header, out _, out _, out _), Is.False);
        }

        [Test]
        public void ReadHelloMessageDecodesAllFields()
        {
            byte[] body = Encode(encoder =>
            {
                encoder.WriteUInt32(null, 1);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 16384);
                encoder.WriteUInt32(null, 100000);
                encoder.WriteUInt32(null, 10);
                encoder.WriteString(null, "opc.tcp://localhost:4840");
            });

            HelloMessage message = TcpMessageParsers.ReadHelloMessage(new ArraySegment<byte>(body));

            Assert.That(message.ProtocolVersion, Is.EqualTo(1));
            Assert.That(message.ReceiveBufferSize, Is.EqualTo(8192));
            Assert.That(message.SendBufferSize, Is.EqualTo(16384));
            Assert.That(message.MaxMessageSize, Is.EqualTo(100000));
            Assert.That(message.MaxChunkCount, Is.EqualTo(10));
            Assert.That(message.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
        }

        [Test]
        public void ReadAcknowledgeMessageDecodesAllFields()
        {
            byte[] body = Encode(encoder =>
            {
                encoder.WriteUInt32(null, 2);
                encoder.WriteUInt32(null, 32768);
                encoder.WriteUInt32(null, 65536);
                encoder.WriteUInt32(null, 200000);
                encoder.WriteUInt32(null, 20);
            });

            AcknowledgeMessage message = TcpMessageParsers.ReadAcknowledgeMessage(new ArraySegment<byte>(body));

            Assert.That(message.ProtocolVersion, Is.EqualTo(2));
            Assert.That(message.ReceiveBufferSize, Is.EqualTo(32768));
            Assert.That(message.SendBufferSize, Is.EqualTo(65536));
            Assert.That(message.MaxMessageSize, Is.EqualTo(200000));
            Assert.That(message.MaxChunkCount, Is.EqualTo(20));
        }

        [Test]
        public void ReadErrorMessageUsesWireReasonOrStatusFallback()
        {
            byte[] withReason = Encode(encoder =>
            {
                encoder.WriteUInt32(null, (uint)StatusCodes.BadTcpMessageTooLarge);
                encoder.WriteString(null, "too large");
            });
            byte[] withoutReason = Encode(encoder =>
            {
                encoder.WriteUInt32(null, (uint)StatusCodes.BadTcpEndpointUrlInvalid);
                encoder.WriteInt32(null, 0);
            });

            ErrorMessage explicitReason = TcpMessageParsers.ReadErrorMessage(new ArraySegment<byte>(withReason));
            ErrorMessage fallbackReason = TcpMessageParsers.ReadErrorMessage(new ArraySegment<byte>(withoutReason));

            Assert.That(explicitReason.StatusCode, Is.EqualTo(StatusCodes.BadTcpMessageTooLarge));
            Assert.That(explicitReason.Reason, Is.EqualTo("too large"));
            Assert.That(fallbackReason.StatusCode, Is.EqualTo(StatusCodes.BadTcpEndpointUrlInvalid));
            Assert.That(fallbackReason.Reason, Does.Contain("BadTcpEndpointUrlInvalid"));
        }

        [Test]
        public void ReadAsymmetricMessageHeaderDecodesFieldsAndDefaultsSecurityPolicy()
        {
            byte[] sender = [1, 2, 3];
            byte[] thumbprint = [4, 5, 6];
            byte[] body = Encode(encoder =>
            {
                encoder.WriteUInt32(null, 1234);
                encoder.WriteString(null, null);
                encoder.WriteByteString(null, sender);
                encoder.WriteByteString(null, thumbprint);
            });

            AsymmetricHeaderFields fields = TcpMessageParsers.ReadAsymmetricMessageHeader(new ArraySegment<byte>(body));

            Assert.That(fields.SecureChannelId, Is.EqualTo(1234));
            Assert.That(fields.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None));
            Assert.That(fields.SenderCertificate.ToArray(), Is.EqualTo(sender));
            Assert.That(fields.ReceiverCertificateThumbprint.ToArray(), Is.EqualTo(thumbprint));
        }

        [Test]
        public void ReadMalformedMessageThrowsServiceResultException()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TcpMessageParsers.ReadHelloMessage(new ArraySegment<byte>(new byte[2])));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public void ReadReverseHelloMessageOversizedServerUriThrowsBadEncodingLimitsExceeded()
        {
            byte[] body = BuildReverseHelloBody(
                serverUri: new string('A', TcpMessageLimits.MaxEndpointUrlLength + 1),
                endpointUrl: "opc.tcp://localhost:4840");

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TcpMessageParsers.ReadReverseHelloMessage(new ArraySegment<byte>(body)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadReverseHelloMessageOversizedEndpointUrlThrowsBadEncodingLimitsExceeded()
        {
            byte[] body = BuildReverseHelloBody(
                serverUri: "urn:test:server",
                endpointUrl: new string('B', TcpMessageLimits.MaxEndpointUrlLength + 1));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => TcpMessageParsers.ReadReverseHelloMessage(new ArraySegment<byte>(body)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void ReadReverseHelloMessageValidStringsAreAccepted()
        {
            const string serverUri = "urn:reverse:server";
            const string endpointUrl = "opc.tcp://gateway.example.com:4840/UA/Server";

            byte[] body = BuildReverseHelloBody(serverUri, endpointUrl);

            ReverseHelloMessage decoded = TcpMessageParsers.ReadReverseHelloMessage(
                new ArraySegment<byte>(body));

            Assert.That(decoded.ServerUri, Is.EqualTo(serverUri));
            Assert.That(decoded.EndpointUrl, Is.EqualTo(endpointUrl));
        }

        private static byte[] BuildReverseHelloBody(string serverUri, string endpointUrl)
        {
            return Encode(encoder =>
            {
                encoder.WriteString(null, serverUri);
                encoder.WriteString(null, endpointUrl);
            });
        }

        private static byte[] Encode(Action<BinaryEncoder> write)
        {
            using var encoder = new BinaryEncoder(ServiceMessageContext.CreateEmpty(null));
            write(encoder);
            return encoder.CloseAndReturnBuffer();
        }
    }
}

