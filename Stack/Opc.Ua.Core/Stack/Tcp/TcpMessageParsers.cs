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
    /// <summary>
    /// Decoded OPC UA UA-TCP Hello (HEL) message body.
    /// Sent by the client immediately after the TCP connection is
    /// established to negotiate the protocol version and the per-direction
    /// buffer / message / chunk limits.
    /// </summary>
    /// <remarks>
    /// Specified by OPC 10000-6 (Mappings) §7.1.2.3 "Hello Message".
    /// Carried in a chunk with message type <c>HELF</c>
    /// (<see cref="TcpMessageType.Hello"/> | <see cref="TcpMessageType.Final"/>).
    /// </remarks>
    /// <param name="ProtocolVersion">
    /// The earliest version of the UA-TCP protocol the client supports.
    /// </param>
    /// <param name="ReceiveBufferSize">
    /// The largest chunk the client can receive, in bytes
    /// (must be at least <see cref="TcpMessageLimits.MinBufferSize"/>).
    /// </param>
    /// <param name="SendBufferSize">
    /// The largest chunk the client will send.
    /// </param>
    /// <param name="MaxMessageSize">
    /// The largest reassembled message the client can process; <c>0</c>
    /// means no limit other than the implementation default.
    /// </param>
    /// <param name="MaxChunkCount">
    /// The maximum number of chunks per message the client will accept;
    /// <c>0</c> means no limit.
    /// </param>
    /// <param name="EndpointUrl">
    /// The endpoint URL the client expects to be served by this socket;
    /// may be <c>null</c> when the field was encoded as a null string.
    /// Truncated to <see cref="TcpMessageLimits.MaxEndpointUrlLength"/>.
    /// </param>
    public readonly record struct HelloMessage(
        uint ProtocolVersion,
        uint ReceiveBufferSize,
        uint SendBufferSize,
        uint MaxMessageSize,
        uint MaxChunkCount,
        string? EndpointUrl);

    /// <summary>
    /// Decoded OPC UA UA-TCP Acknowledge (ACK) message body.
    /// Sent by the server in response to a <see cref="HelloMessage"/> to
    /// confirm the negotiated protocol version and buffer / chunk limits.
    /// </summary>
    /// <remarks>
    /// Specified by OPC 10000-6 (Mappings) §7.1.2.4 "Acknowledge Message".
    /// Carried in a chunk with message type <c>ACKF</c>
    /// (<see cref="TcpMessageType.Acknowledge"/> | <see cref="TcpMessageType.Final"/>).
    /// The server's response fields override the client's hello values; the
    /// effective per-direction limit is the minimum of the two for each side.
    /// </remarks>
    /// <param name="ProtocolVersion">
    /// The protocol version the server agreed to use; never higher than the
    /// client's <see cref="HelloMessage.ProtocolVersion"/>.
    /// </param>
    /// <param name="ReceiveBufferSize">
    /// The largest chunk the server can receive.
    /// </param>
    /// <param name="SendBufferSize">
    /// The largest chunk the server will send.
    /// </param>
    /// <param name="MaxMessageSize">
    /// The largest reassembled message the server can process; <c>0</c>
    /// means no limit other than the implementation default.
    /// </param>
    /// <param name="MaxChunkCount">
    /// The maximum number of chunks per message the server will accept;
    /// <c>0</c> means no limit.
    /// </param>
    public readonly record struct AcknowledgeMessage(
        uint ProtocolVersion,
        uint ReceiveBufferSize,
        uint SendBufferSize,
        uint MaxMessageSize,
        uint MaxChunkCount);

    /// <summary>
    /// Decoded OPC UA UA-TCP Error (ERR) message body.
    /// Sent by either peer to report a fatal transport-layer problem.
    /// The connection is closed immediately after the error chunk is sent.
    /// </summary>
    /// <remarks>
    /// Specified by OPC 10000-6 (Mappings) §7.1.2.5 "Error Message".
    /// Carried in a chunk with message type <c>ERRF</c>
    /// (<see cref="TcpMessageType.Error"/> | <see cref="TcpMessageType.Final"/>).
    /// </remarks>
    /// <param name="StatusCode">
    /// The OPC UA status code describing the failure; values follow
    /// <see cref="StatusCodes"/>.
    /// </param>
    /// <param name="Reason">
    /// Free-form, human-readable description of the failure (UTF-8 on the
    /// wire). Truncated to <see cref="TcpMessageLimits.MaxErrorReasonLength"/>;
    /// falls back to <see cref="ServiceResult.ToString"/> when the encoded
    /// field is empty.
    /// </param>
    public readonly record struct ErrorMessage(uint StatusCode, string Reason);

    /// <summary>
    /// Decoded OPC UA UA-TCP ReverseHello (RHE) message body.
    /// Sent by a server that is acting as a TCP <i>client</i> in a reverse
    /// connection scenario (the OPC UA client listens on a TCP port and
    /// the server initiates the underlying connection). The receiver of
    /// this message becomes the OPC UA client and replies with a
    /// <see cref="HelloMessage"/>.
    /// </summary>
    /// <remarks>
    /// Specified by OPC 10000-6 (Mappings) §7.1.2.6 "ReverseHello Message"
    /// and Part 4 §6.7.5 "ReverseConnect". Carried in a chunk with message
    /// type <c>RHEF</c>
    /// (<see cref="TcpMessageType.ReverseHello"/> | <see cref="TcpMessageType.Final"/>).
    /// </remarks>
    /// <param name="ServerUri">
    /// The ApplicationUri of the OPC UA server that initiated the reverse
    /// connection. May be <c>null</c> when the field was encoded as a null
    /// string.
    /// </param>
    /// <param name="EndpointUrl">
    /// The endpoint URL the client should use in the subsequent
    /// <see cref="HelloMessage.EndpointUrl"/>. May be <c>null</c> when the
    /// field was encoded as a null string.
    /// </param>
    public readonly record struct ReverseHelloMessage(string? ServerUri, string? EndpointUrl);

    /// <summary>
    /// Decoded OPC UA UA-SC asymmetric algorithm SecurityHeader, which
    /// prefixes every OpenSecureChannel request and response chunk
    /// (message type <c>OPN</c>) before the encrypted SequenceHeader and
    /// payload.
    /// </summary>
    /// <remarks>
    /// Specified by OPC 10000-6 (Mappings) §7.3.2 "Security header" and
    /// §6.7.2.2 "OpenSecureChannel" in Part 4. The fields it carries are
    /// the SecurityPolicy that the chunk is encrypted under, the sender's
    /// X.509 application certificate, and the SHA-1 thumbprint of the
    /// receiver's certificate (so the receiver can pick the right private
    /// key for asymmetric decryption).
    /// </remarks>
    /// <param name="SecureChannelId">
    /// The SecureChannelId from the common chunk header; <c>0</c> for the
    /// first OpenSecureChannel request, non-zero for renewals.
    /// </param>
    /// <param name="SecurityPolicyUri">
    /// The OPC UA SecurityPolicy URI under which the chunk's payload is
    /// signed and/or encrypted (e.g.
    /// <c>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</c>).
    /// Falls back to <see cref="SecurityPolicies.None"/> when the field was
    /// encoded as a null string. Truncated to
    /// <see cref="TcpMessageLimits.MaxSecurityPolicyUriSize"/>.
    /// </param>
    /// <param name="SenderCertificate">
    /// The DER-encoded sender (client or server) X.509 application
    /// certificate; truncated to <see cref="TcpMessageLimits.MaxCertificateSize"/>.
    /// Has <see cref="ByteString.IsNull"/> when the SecurityPolicy is
    /// <see cref="SecurityPolicies.None"/>.
    /// </param>
    /// <param name="ReceiverCertificateThumbprint">
    /// The SHA-1 thumbprint (<see cref="TcpMessageLimits.CertificateThumbprintSize"/>
    /// bytes) of the receiver's certificate; lets the receiver select the
    /// matching private key. Has <see cref="ByteString.IsNull"/> when the
    /// SecurityPolicy is <see cref="SecurityPolicies.None"/>.
    /// </param>
    public readonly record struct AsymmetricHeaderFields(
        uint SecureChannelId,
        string SecurityPolicyUri,
        ByteString SenderCertificate,
        ByteString ReceiverCertificateThumbprint);

    /// <summary>
    /// Stateless decoders for the pre-crypto, pre-authentication portion of
    /// the OPC UA UA-TCP / UA-SC chunk surface — the common chunk header
    /// and the bodies of the connection-protocol messages (Hello,
    /// Acknowledge, Error, ReverseHello) plus the asymmetric SecurityHeader
    /// that fronts every OpenSecureChannel chunk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specified by OPC 10000-6 (Mappings) §7.1 "UA Connection Protocol"
    /// and §7.3 "UA Secure Conversation". Inputs are always raw, attacker-
    /// controlled chunk bytes — every decoder is deliberately allocation-
    /// bounded (limits drawn from <see cref="TcpMessageLimits"/>) and
    /// surfaces decode failures via <see cref="ServiceResultException"/>
    /// with one of the standard transport status codes
    /// (<see cref="StatusCodes.BadTcpMessageTypeInvalid"/>,
    /// <see cref="StatusCodes.BadDecodingError"/>,
    /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/>).
    /// </para>
    /// <para>
    /// The decoders intentionally do not depend on any per-channel state
    /// (sequence numbers, security tokens, namespace tables) so they can be
    /// reused by offline tooling (packet capture decoders, fuzzing harness,
    /// CTT-style conformance tests) without spinning up a full channel.
    /// They share a single
    /// <see cref="ServiceMessageContext.CreateEmpty"/>-derived context for
    /// the embedded <see cref="BinaryDecoder"/>.
    /// </para>
    /// </remarks>
    public static class TcpMessageParsers
    {
        private static readonly IServiceMessageContext s_messageContext =
            ServiceMessageContext.CreateEmpty(null!);

        /// <summary>
        /// Parses the 8-byte common chunk header that prefixes every
        /// UA-TCP / UA-SC chunk and validates the message-type magic and
        /// the declared chunk size against
        /// <see cref="TcpMessageLimits.MessageTypeAndSize"/> and
        /// <see cref="TcpMessageLimits.MaxBufferSize"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Specified by OPC 10000-6 (Mappings) §7.1.2.2 "Message Header".
        /// The header layout is, little-endian on the wire:
        /// </para>
        /// <list type="bullet">
        ///   <item><description>bytes 0..2 — message-type magic
        ///   (3 ASCII bytes: <c>MSG</c>, <c>OPN</c>, <c>CLO</c>, <c>HEL</c>,
        ///   <c>ACK</c>, <c>ERR</c>, <c>RHE</c>);</description></item>
        ///   <item><description>byte 3 — chunk-type
        ///   (<c>F</c> final, <c>C</c> intermediate, <c>A</c> abort);</description></item>
        ///   <item><description>bytes 4..7 — message size, including the
        ///   8-byte header, as a <see cref="uint"/>.</description></item>
        /// </list>
        /// <para>
        /// Pure parsing; never throws. Returns <c>false</c> for any header
        /// that is too short, has an unknown message-type magic, or a
        /// declared size outside the protocol bounds — equivalent to the
        /// stack rejecting the chunk with
        /// <see cref="StatusCodes.BadTcpMessageTypeInvalid"/> /
        /// <see cref="StatusCodes.BadTcpMessageTooLarge"/>.
        /// </para>
        /// </remarks>
        /// <param name="bytes">
        /// The chunk bytes to inspect. Only the first 8 bytes are read;
        /// trailing data is ignored.
        /// </param>
        /// <param name="messageType">
        /// On success, the 4-byte combined message-type + chunk-type word
        /// as encoded little-endian on the wire (mask with
        /// <see cref="TcpMessageType.MessageTypeMask"/> /
        /// <see cref="TcpMessageType.ChunkTypeMask"/> to extract each).
        /// Zero on failure.
        /// </param>
        /// <param name="chunkType">
        /// On success, the chunk-type byte isolated via
        /// <see cref="TcpMessageType.ChunkTypeMask"/>
        /// (<see cref="TcpMessageType.Final"/>,
        /// <see cref="TcpMessageType.Intermediate"/>,
        /// or <see cref="TcpMessageType.Abort"/>). Zero on failure.
        /// </param>
        /// <param name="messageSize">
        /// On success, the declared chunk size in bytes (including the
        /// 8-byte header). Zero on failure.
        /// </param>
        /// <returns>
        /// <c>true</c> when the header is well-formed and the chunk size
        /// is within protocol bounds; otherwise <c>false</c>.
        /// </returns>
        public static bool TryParseChunkHeader(
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

        /// <summary>
        /// Decodes the body of a Hello (HEL) message (the 8 header bytes
        /// already stripped) into a <see cref="HelloMessage"/>.
        /// </summary>
        /// <remarks>
        /// Specified by OPC 10000-6 (Mappings) §7.1.2.3 "Hello Message".
        /// </remarks>
        /// <param name="body">
        /// The chunk payload after the 8-byte common header.
        /// </param>
        /// <returns>
        /// The decoded <see cref="HelloMessage"/>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The body is malformed
        /// (<see cref="StatusCodes.BadDecodingError"/>) or the encoded
        /// EndpointUrl exceeds <see cref="TcpMessageLimits.MaxEndpointUrlLength"/>
        /// (<see cref="StatusCodes.BadEncodingLimitsExceeded"/>).
        /// </exception>
        public static HelloMessage ReadHelloMessage(ArraySegment<byte> body)
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

        /// <summary>
        /// Decodes the body of an Acknowledge (ACK) message (the 8 header
        /// bytes already stripped) into an <see cref="AcknowledgeMessage"/>.
        /// </summary>
        /// <remarks>
        /// Specified by OPC 10000-6 (Mappings) §7.1.2.4 "Acknowledge
        /// Message".
        /// </remarks>
        /// <param name="body">
        /// The chunk payload after the 8-byte common header.
        /// </param>
        /// <returns>
        /// The decoded <see cref="AcknowledgeMessage"/>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The body is malformed
        /// (<see cref="StatusCodes.BadDecodingError"/>).
        /// </exception>
        public static AcknowledgeMessage ReadAcknowledgeMessage(ArraySegment<byte> body)
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

        /// <summary>
        /// Decodes the body of an Error (ERR) message (the 8 header bytes
        /// already stripped) into an <see cref="ErrorMessage"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Specified by OPC 10000-6 (Mappings) §7.1.2.5 "Error Message".
        /// </para>
        /// <para>
        /// When the encoded Reason field is empty or absent the returned
        /// <see cref="ErrorMessage.Reason"/> is populated from the
        /// <see cref="ServiceResult.ToString"/> of the decoded status code,
        /// so callers always receive a non-empty, human-readable string.
        /// </para>
        /// </remarks>
        /// <param name="body">
        /// The chunk payload after the 8-byte common header.
        /// </param>
        /// <returns>
        /// The decoded <see cref="ErrorMessage"/>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The body is malformed
        /// (<see cref="StatusCodes.BadDecodingError"/>).
        /// </exception>
        public static ErrorMessage ReadErrorMessage(ArraySegment<byte> body)
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

        /// <summary>
        /// Decodes the body of a ReverseHello (RHE) message (the 8 header
        /// bytes already stripped) into a <see cref="ReverseHelloMessage"/>.
        /// </summary>
        /// <remarks>
        /// Specified by OPC 10000-6 (Mappings) §7.1.2.6 "ReverseHello
        /// Message" and Part 4 §6.7.5 "ReverseConnect".
        /// </remarks>
        /// <param name="body">
        /// The chunk payload after the 8-byte common header.
        /// </param>
        /// <returns>
        /// The decoded <see cref="ReverseHelloMessage"/>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The body is malformed
        /// (<see cref="StatusCodes.BadDecodingError"/>) or either of the
        /// length-prefixed string fields exceeds
        /// <see cref="TcpMessageLimits.MaxEndpointUrlLength"/>
        /// (<see cref="StatusCodes.BadEncodingLimitsExceeded"/>).
        /// </exception>
        public static ReverseHelloMessage ReadReverseHelloMessage(ArraySegment<byte> body)
        {
            using var decoder = CreateDecoder(body);
            string? serverUri = decoder.ReadString(null, TcpMessageLimits.MaxEndpointUrlLength);
            string? endpointUrl = decoder.ReadString(null, TcpMessageLimits.MaxEndpointUrlLength);

            return new ReverseHelloMessage(serverUri, endpointUrl);
        }

        /// <summary>
        /// Decodes the asymmetric algorithm SecurityHeader that prefixes
        /// every OpenSecureChannel (OPN) chunk into an
        /// <see cref="AsymmetricHeaderFields"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Specified by OPC 10000-6 (Mappings) §7.3.2 "Security header"
        /// (asymmetric variant) and §6.7.2.2 "OpenSecureChannel" in Part 4.
        /// </para>
        /// <para>
        /// The body passed in starts at the SecureChannelId UInt32; the
        /// 4-byte chunk magic + 4-byte message size that precede it on the
        /// wire have already been consumed. The
        /// <see cref="AsymmetricHeaderFields.SenderCertificate"/> and
        /// <see cref="AsymmetricHeaderFields.ReceiverCertificateThumbprint"/>
        /// fields are returned as <see cref="ByteString"/> so callers avoid
        /// the per-chunk allocation of a managed <c>byte[]</c>.
        /// </para>
        /// </remarks>
        /// <param name="body">
        /// The chunk payload after the 8-byte common header.
        /// </param>
        /// <returns>
        /// The decoded <see cref="AsymmetricHeaderFields"/>.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// The body is malformed
        /// (<see cref="StatusCodes.BadDecodingError"/>) or any of the
        /// length-prefixed fields exceeds its
        /// <see cref="TcpMessageLimits"/> bound
        /// (<see cref="StatusCodes.BadEncodingLimitsExceeded"/>).
        /// </exception>
        public static AsymmetricHeaderFields ReadAsymmetricMessageHeader(ArraySegment<byte> body)
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
                senderCertificate,
                receiverCertificateThumbprint);
        }

        private static BinaryDecoder CreateDecoder(ArraySegment<byte> body)
        {
            return new BinaryDecoder(body, s_messageContext);
        }
    }
}
