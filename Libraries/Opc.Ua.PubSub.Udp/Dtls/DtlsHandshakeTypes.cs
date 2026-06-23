/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    internal sealed record DtlsHandshakeFrame(
        DtlsHandshakeType MessageType,
        int MessageLength,
        ushort MessageSequence,
        int FragmentOffset,
        byte[] Fragment);

    internal sealed record DtlsClientHello(
        byte[] Random,
        byte[] SessionId,
        IReadOnlyList<DtlsCipherSuite> CipherSuites,
        DtlsHelloExtensions Extensions);

    internal sealed record DtlsServerHello(
        byte[] Random,
        byte[] SessionId,
        DtlsCipherSuite CipherSuite,
        DtlsHelloExtensions Extensions);

    internal sealed record DtlsHelloExtensions(
        IReadOnlyList<ushort> SupportedVersions,
        IReadOnlyList<DtlsNamedCurve> SupportedGroups,
        IReadOnlyList<DtlsKeyShareEntry> KeyShares,
        IReadOnlyList<DtlsSignatureScheme> SignatureAlgorithms,
        byte[] Cookie)
    {
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

    internal sealed record DtlsKeyShareEntry(DtlsNamedCurve Group, byte[] KeyExchange);

    internal enum DtlsHandshakeType : byte
    {
        ClientHello = 1,
        ServerHello = 2,
        EncryptedExtensions = 8,
        Certificate = 11,
        CertificateVerify = 15,
        Finished = 20,
        MessageHash = 254
    }

    internal enum DtlsSignatureScheme : ushort
    {
        EcdsaSecp256r1Sha256 = 0x0403,
        EcdsaSecp384r1Sha384 = 0x0503
    }

    public sealed class DtlsHandshakeException : Exception
    {
        public DtlsHandshakeException()
        {
        }

        public DtlsHandshakeException(string message)
            : base(message)
        {
        }

        public DtlsHandshakeException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class DtlsHandshakeWriter
    {
        public void WriteByte(byte value)
        {
            m_bytes.Add(value);
        }

        public void WriteUInt16(ushort value)
        {
            m_bytes.Add((byte)(value >> 8));
            m_bytes.Add((byte)value);
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            for (int ii = 0; ii < value.Length; ii++)
            {
                m_bytes.Add(value[ii]);
            }
        }

        public void WriteOpaque8(ReadOnlySpan<byte> value)
        {
            if (value.Length > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            WriteByte((byte)value.Length);
            WriteBytes(value);
        }

        public void WriteOpaque16(ReadOnlySpan<byte> value)
        {
            if (value.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            WriteUInt16((ushort)value.Length);
            WriteBytes(value);
        }

        public byte[] ToArray()
        {
            return m_bytes.ToArray();
        }

        private readonly List<byte> m_bytes = [];
    }
}