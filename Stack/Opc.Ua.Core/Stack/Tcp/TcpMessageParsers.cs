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
using System.Text;

namespace Opc.Ua.Bindings
{
    internal readonly record struct HelloMessage(
        uint ProtocolVersion,
        uint ReceiveBufferSize,
        uint SendBufferSize,
        uint MaxMessageSize,
        uint MaxChunkCount,
        string? EndpointUrl);

    internal readonly record struct AcknowledgeMessage(
        uint ProtocolVersion,
        uint ReceiveBufferSize,
        uint SendBufferSize,
        uint MaxMessageSize,
        uint MaxChunkCount);

    internal readonly record struct ErrorMessage(uint StatusCode, string Reason);

    internal readonly record struct ReverseHelloMessage(string? ServerUri, string? EndpointUrl);

    internal readonly record struct AsymmetricHeaderFields(
        uint SecureChannelId,
        string SecurityPolicyUri,
        byte[] SenderCertificate,
        byte[] ReceiverCertificateThumbprint);

    internal static class TcpMessageParsers
    {
        private static readonly IServiceMessageContext s_messageContext =
            ServiceMessageContext.CreateEmpty(null!);

        internal static bool TryParseChunkHeader(
            ReadOnlySpan<byte> bytes,
            out uint messageType,
            out uint chunkType,
            out uint messageSize)
        {
            messageType = 0;
            chunkType = 0;
            messageSize = 0;

            if (bytes.Length < TcpMessageLimits.MessageTypeAndSize)
            {
                return false;
            }

            messageType = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            chunkType = messageType & TcpMessageType.ChunkTypeMask;
            messageSize = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes[TcpMessageLimits.StringLengthSize..]);

            return TcpMessageType.IsValid(messageType) &&
                messageSize is >= TcpMessageLimits.MessageTypeAndSize and <= TcpMessageLimits.MaxBufferSize;
        }

        internal static HelloMessage ReadHelloMessage(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            uint protocolVersion = decoder.ReadUInt32(null);
            uint receiveBufferSize = decoder.ReadUInt32(null);
            uint sendBufferSize = decoder.ReadUInt32(null);
            uint maxMessageSize = decoder.ReadUInt32(null);
            uint maxChunkCount = decoder.ReadUInt32(null);
            string? endpointUrl = decoder.ReadString(null, TcpMessageLimits.MaxEndpointUrlLength);

            return new HelloMessage(
                protocolVersion,
                receiveBufferSize,
                sendBufferSize,
                maxMessageSize,
                maxChunkCount,
                endpointUrl);
        }

        internal static AcknowledgeMessage ReadAcknowledgeMessage(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            uint protocolVersion = decoder.ReadUInt32(null);
            uint receiveBufferSize = decoder.ReadUInt32(null);
            uint sendBufferSize = decoder.ReadUInt32(null);
            uint maxMessageSize = decoder.ReadUInt32(null);
            uint maxChunkCount = decoder.ReadUInt32(null);

            return new AcknowledgeMessage(
                protocolVersion,
                receiveBufferSize,
                sendBufferSize,
                maxMessageSize,
                maxChunkCount);
        }

        internal static ErrorMessage ReadErrorMessage(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            uint statusCode = decoder.ReadUInt32(null);
            string? reason = null;
            int reasonLength = decoder.ReadInt32(null);

            if (reasonLength is > 0 and < TcpMessageLimits.MaxErrorReasonLength)
            {
                byte[] reasonBytes = new byte[reasonLength];

                for (int ii = 0; ii < reasonBytes.Length; ii++)
                {
                    reasonBytes[ii] = decoder.ReadByte(null);
                }

                reason = Encoding.UTF8.GetString(reasonBytes, 0, reasonBytes.Length);
            }

            reason ??= new ServiceResult(statusCode).ToString();

            return new ErrorMessage(statusCode, reason);
        }

        internal static ReverseHelloMessage ReadReverseHelloMessage(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            string? serverUri = decoder.ReadString(null);
            string? endpointUrl = decoder.ReadString(null);

            return new ReverseHelloMessage(serverUri, endpointUrl);
        }

        internal static AsymmetricHeaderFields ReadAsymmetricMessageHeader(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            uint secureChannelId = decoder.ReadUInt32(null);
            string securityPolicyUri = decoder.ReadString(
                null,
                TcpMessageLimits.MaxSecurityPolicyUriSize) ??
                SecurityPolicies.None;
            ByteString senderCertificate = decoder.ReadByteString(TcpMessageLimits.MaxCertificateSize);
            ByteString receiverCertificateThumbprint = decoder.ReadByteString(
                TcpMessageLimits.CertificateThumbprintSize);

            return new AsymmetricHeaderFields(
                secureChannelId,
                securityPolicyUri,
                senderCertificate.ToArray(),
                receiverCertificateThumbprint.ToArray());
        }

        private static BinaryDecoder CreateDecoder(ArraySegment<byte> body)
        {
            return new BinaryDecoder(body, s_messageContext);
        }
    }
}
