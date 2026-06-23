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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Parsed DTLS handshake message fragment header and payload from RFC 9147 §5.2.
    /// </summary>
    internal sealed record DtlsHandshakeFrame(
        DtlsHandshakeType MessageType,
        int MessageLength,
        ushort MessageSequence,
        int FragmentOffset,
        byte[] Fragment);

    /// <summary>
    /// Decoded DTLS 1.3 ClientHello fields used by the handshake driver.
    /// </summary>
    internal sealed record DtlsClientHello(
        byte[] Random,
        byte[] SessionId,
        IReadOnlyList<DtlsCipherSuite> CipherSuites,
        DtlsHelloExtensions Extensions);

    /// <summary>
    /// Decoded DTLS 1.3 ServerHello fields used by the handshake driver.
    /// </summary>
    internal sealed record DtlsServerHello(
        byte[] Random,
        byte[] SessionId,
        DtlsCipherSuite CipherSuite,
        DtlsHelloExtensions Extensions);

    /// <summary>
    /// TLS 1.3 hello extensions carried in the DTLS ClientHello and ServerHello.
    /// </summary>
    internal sealed record DtlsHelloExtensions(
        IReadOnlyList<ushort> SupportedVersions,
        IReadOnlyList<DtlsNamedCurve> SupportedGroups,
        IReadOnlyList<DtlsKeyShareEntry> KeyShares,
        IReadOnlyList<DtlsSignatureScheme> SignatureAlgorithms,
        byte[] Cookie)
    {
        /// <summary>
        /// Creates the default extension set advertising DTLS 1.3, the supplied groups
        /// and key shares, and the supported ECDSA signature schemes.
        /// </summary>
        public static DtlsHelloExtensions CreateDefault(
            IReadOnlyList<DtlsNamedCurve> groups,
            IReadOnlyList<DtlsKeyShareEntry> keyShares,
            byte[]? cookie = null)
        {
            return new DtlsHelloExtensions(
                [DtlsHandshakeCodec.Dtls13Version],
                groups,
                keyShares,
                [DtlsSignatureScheme.EcdsaSecp256r1Sha256, DtlsSignatureScheme.EcdsaSecp384r1Sha384],
                cookie ?? []);
        }
    }

    /// <summary>
    /// Single TLS 1.3 key_share entry pairing a named group with its key exchange data.
    /// </summary>
    internal sealed record DtlsKeyShareEntry(DtlsNamedCurve Group, byte[] KeyExchange);

    /// <summary>
    /// DTLS 1.3 handshake message type codes from RFC 8446 §4.
    /// </summary>
    internal enum DtlsHandshakeType : byte
    {
        ClientHello = 1,
        ServerHello = 2,
        EncryptedExtensions = 8,
        Certificate = 11,
        CertificateRequest = 13,
        CertificateVerify = 15,
        Finished = 20,
        MessageHash = 254
    }

    /// <summary>
    /// TLS 1.3 signature scheme code points used for certificate authentication.
    /// </summary>
    internal enum DtlsSignatureScheme : ushort
    {
        EcdsaSecp256r1Sha256 = 0x0403,
        EcdsaSecp384r1Sha384 = 0x0503
    }

    /// <summary>
    /// Exception thrown when a DTLS handshake message is malformed or fails verification.
    /// </summary>
    public sealed class DtlsHandshakeException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DtlsHandshakeException"/> class.
        /// </summary>
        public DtlsHandshakeException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DtlsHandshakeException"/> class
        /// with the specified error message.
        /// </summary>
        public DtlsHandshakeException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DtlsHandshakeException"/> class
        /// with the specified error message and inner exception.
        /// </summary>
        public DtlsHandshakeException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Minimal big-endian buffer writer for DTLS handshake message bodies.
    /// </summary>
    internal sealed class DtlsHandshakeWriter
    {
        /// <summary>
        /// Appends a single byte to the buffer.
        /// </summary>
        public void WriteByte(byte value)
        {
            m_bytes.Add(value);
        }

        /// <summary>
        /// Appends a big-endian 16-bit value to the buffer.
        /// </summary>
        public void WriteUInt16(ushort value)
        {
            m_bytes.Add((byte)(value >> 8));
            m_bytes.Add((byte)value);
        }

        /// <summary>
        /// Appends a raw byte sequence to the buffer.
        /// </summary>
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            for (int ii = 0; ii < value.Length; ii++)
            {
                m_bytes.Add(value[ii]);
            }
        }

        /// <summary>
        /// Appends a byte sequence prefixed with an 8-bit length.
        /// </summary>
        public void WriteOpaque8(ReadOnlySpan<byte> value)
        {
            if (value.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            WriteByte((byte)value.Length);
            WriteBytes(value);
        }

        /// <summary>
        /// Appends a byte sequence prefixed with a big-endian 16-bit length.
        /// </summary>
        public void WriteOpaque16(ReadOnlySpan<byte> value)
        {
            if (value.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            WriteUInt16((ushort)value.Length);
            WriteBytes(value);
        }

        /// <summary>
        /// Returns the accumulated bytes as a new array.
        /// </summary>
        public byte[] ToArray()
        {
            return [.. m_bytes];
        }

        private readonly List<byte> m_bytes = [];
    }
}
