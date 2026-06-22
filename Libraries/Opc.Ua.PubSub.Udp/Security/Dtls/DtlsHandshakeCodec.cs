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
using System.Linq;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// DTLS 1.3 handshake frame and TLS 1.3 hello message codecs.
    /// </summary>
    internal static class DtlsHandshakeCodec
    {
        public const ushort Dtls13Version = 0xfefd;
        public const ushort LegacyDtls12Version = 0xfefd;
        public const int HandshakeHeaderLength = 12;

        public static byte[] EncodeFrame(DtlsHandshakeType messageType, ushort messageSequence, ReadOnlySpan<byte> body)
        {
            byte[] output = new byte[HandshakeHeaderLength + body.Length];
            output[0] = (byte)messageType;
            WriteUInt24(output.AsSpan(1, 3), body.Length);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4, 2), messageSequence);
            WriteUInt24(output.AsSpan(6, 3), 0);
            WriteUInt24(output.AsSpan(9, 3), body.Length);
            body.CopyTo(output.AsSpan(HandshakeHeaderLength));
            return output;
        }

        public static DtlsHandshakeFrame DecodeFrame(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < HandshakeHeaderLength)
            {
                throw new DtlsHandshakeException("DTLS handshake frame is shorter than RFC 9147 §5 header.");
            }

            int length = ReadUInt24(frame.Slice(1, 3));
            int fragmentOffset = ReadUInt24(frame.Slice(6, 3));
            int fragmentLength = ReadUInt24(frame.Slice(9, 3));
            if (frame.Length != HandshakeHeaderLength + fragmentLength || fragmentOffset + fragmentLength > length)
            {
                throw new DtlsHandshakeException("DTLS handshake fragment range is invalid.");
            }

            return new DtlsHandshakeFrame(
                (DtlsHandshakeType)frame[0],
                length,
                BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2)),
                fragmentOffset,
                frame.Slice(HandshakeHeaderLength, fragmentLength).ToArray());
        }

        public static byte[] EncodeClientHello(DtlsClientHello hello)
        {
            if (hello is null)
            {
                throw new ArgumentNullException(nameof(hello));
            }

            var writer = new DtlsHandshakeWriter();
            writer.WriteUInt16(LegacyDtls12Version);
            writer.WriteBytes(EnsureLength(hello.Random, 32, nameof(hello.Random)));
            writer.WriteOpaque8(hello.SessionId);
            writer.WriteUInt16((ushort)(hello.CipherSuites.Count * 2));
            foreach (DtlsCipherSuite cipherSuite in hello.CipherSuites)
            {
                writer.WriteUInt16(ToWireCipherSuite(cipherSuite));
            }

            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteOpaque16(EncodeExtensions(hello.Extensions));
            return writer.ToArray();
        }
        public static DtlsClientHello DecodeClientHello(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            if (reader.ReadUInt16() != LegacyDtls12Version)
            {
                throw new DtlsHandshakeException("ClientHello legacy_version must be DTLS 1.2 for DTLS 1.3.");
            }

            byte[] random = reader.ReadBytes(32);
            byte[] sessionId = reader.ReadOpaque8();
            ReadOnlySpan<byte> cipherSuiteBytes = reader.ReadOpaque16();
            if ((cipherSuiteBytes.Length & 1) != 0)
            {
                throw new DtlsHandshakeException("ClientHello cipher_suites vector has an odd length.");
            }

            var cipherSuites = new List<DtlsCipherSuite>();
            for (int ii = 0; ii < cipherSuiteBytes.Length; ii += 2)
            {
                cipherSuites.Add(FromWireCipherSuite(BinaryPrimitives.ReadUInt16BigEndian(cipherSuiteBytes.Slice(ii, 2))));
            }

            ReadOnlySpan<byte> compressionMethods = reader.ReadOpaque8();
            if (compressionMethods.Length != 1 || compressionMethods[0] != 0)
            {
                throw new DtlsHandshakeException("DTLS 1.3 ClientHello must offer only null compression.");
            }

            DtlsHelloExtensions extensions = DecodeExtensions(reader.ReadOpaque16());
            reader.EnsureComplete();
            ValidateSupportedVersions(extensions);
            return new DtlsClientHello(random, sessionId, cipherSuites, extensions);
        }

        public static byte[] EncodeServerHello(DtlsServerHello hello)
        {
            if (hello is null)
            {
                throw new ArgumentNullException(nameof(hello));
            }

            var writer = new DtlsHandshakeWriter();
            writer.WriteUInt16(LegacyDtls12Version);
            writer.WriteBytes(EnsureLength(hello.Random, 32, nameof(hello.Random)));
            writer.WriteOpaque8(hello.SessionId);
            writer.WriteUInt16(ToWireCipherSuite(hello.CipherSuite));
            writer.WriteByte(0);
            writer.WriteOpaque16(EncodeExtensions(hello.Extensions));
            return writer.ToArray();
        }

        public static DtlsServerHello DecodeServerHello(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            if (reader.ReadUInt16() != LegacyDtls12Version)
            {
                throw new DtlsHandshakeException("ServerHello legacy_version must be DTLS 1.2 for DTLS 1.3.");
            }

            byte[] random = reader.ReadBytes(32);
            byte[] sessionId = reader.ReadOpaque8();
            DtlsCipherSuite cipherSuite = FromWireCipherSuite(reader.ReadUInt16());
            if (reader.ReadByte() != 0)
            {
                throw new DtlsHandshakeException("DTLS 1.3 ServerHello must select null compression.");
            }

            DtlsHelloExtensions extensions = DecodeExtensions(reader.ReadOpaque16());
            reader.EnsureComplete();
            ValidateSupportedVersions(extensions);
            return new DtlsServerHello(random, sessionId, cipherSuite, extensions);
        }

        public static byte[] EncodeEncryptedExtensions()
        {
            return [0, 0];
        }

        public static void DecodeEncryptedExtensions(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            if (reader.ReadOpaque16().Length != 0)
            {
                throw new DtlsHandshakeException("EncryptedExtensions must not contain unsupported extensions.");
            }

            reader.EnsureComplete();
        }

        public static byte[] EncodeFinished(ReadOnlySpan<byte> verifyData)
        {
            return verifyData.ToArray();
        }

        public static byte[] DecodeFinished(ReadOnlySpan<byte> body)
        {
            return body.ToArray();
        }
        public static ushort ToWireNamedGroup(DtlsNamedCurve curve)
        {
            return curve switch
            {
                DtlsNamedCurve.NistP256 => 0x0017,
                DtlsNamedCurve.NistP384 => 0x0018,
                DtlsNamedCurve.BrainpoolP256r1 => 0x001a,
                DtlsNamedCurve.BrainpoolP384r1 => 0x001b,
                DtlsNamedCurve.Curve25519 => throw new DtlsHandshakeException(
                    "Curve25519 is not available through portable .NET BCL ECDH; no downgrade is allowed."),
                DtlsNamedCurve.Curve448 => throw new DtlsHandshakeException(
                    "Curve448 is not available through portable .NET BCL ECDH; no downgrade is allowed."),
                _ => throw new DtlsHandshakeException("Unsupported DTLS named group.")
            };
        }

        public static DtlsNamedCurve FromWireNamedGroup(ushort wireGroup)
        {
            return wireGroup switch
            {
                0x0017 => DtlsNamedCurve.NistP256,
                0x0018 => DtlsNamedCurve.NistP384,
                0x001a => DtlsNamedCurve.BrainpoolP256r1,
                0x001b => DtlsNamedCurve.BrainpoolP384r1,
                0x001d => throw new DtlsHandshakeException(
                    "Curve25519 key_share is unsupported by the .NET BCL and is rejected fail-closed."),
                0x001e => throw new DtlsHandshakeException(
                    "Curve448 key_share is unsupported by the .NET BCL and is rejected fail-closed."),
                _ => throw new DtlsHandshakeException("Unsupported DTLS named group.")
            };
        }

        public static ushort ToWireCipherSuite(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite switch
            {
                DtlsCipherSuite.TlsAes128GcmSha256 => 0x1301,
                DtlsCipherSuite.TlsAes256GcmSha384 => 0x1302,
                DtlsCipherSuite.TlsChaCha20Poly1305Sha256 => 0x1303,
                DtlsCipherSuite.TlsSha256Sha256 => 0xc0b4,
                DtlsCipherSuite.TlsSha384Sha384 => 0xc0b5,
                _ => throw new DtlsHandshakeException("Unsupported DTLS cipher suite.")
            };
        }

        public static DtlsCipherSuite FromWireCipherSuite(ushort cipherSuite)
        {
            return cipherSuite switch
            {
                0x1301 => DtlsCipherSuite.TlsAes128GcmSha256,
                0x1302 => DtlsCipherSuite.TlsAes256GcmSha384,
                0x1303 => DtlsCipherSuite.TlsChaCha20Poly1305Sha256,
                0xc0b4 => DtlsCipherSuite.TlsSha256Sha256,
                0xc0b5 => DtlsCipherSuite.TlsSha384Sha384,
                _ => throw new DtlsHandshakeException("Unsupported DTLS cipher suite.")
            };
        }

        internal static int ReadUInt24(ReadOnlySpan<byte> source)
        {
            return (source[0] << 16) | (source[1] << 8) | source[2];
        }

        internal static void WriteUInt24(Span<byte> destination, int value)
        {
            if (value is < 0 or > 0xffffff)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            destination[0] = (byte)(value >> 16);
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)value;
        }
        private static byte[] EncodeExtensions(DtlsHelloExtensions extensions)
        {
            var extensionsWriter = new DtlsHandshakeWriter();
            WriteExtension(extensionsWriter, 43, EncodeSupportedVersions(extensions.SupportedVersions));
            WriteExtension(extensionsWriter, 10, EncodeSupportedGroups(extensions.SupportedGroups));
            WriteExtension(extensionsWriter, 51, EncodeKeyShares(extensions.KeyShares));
            WriteExtension(extensionsWriter, 13, EncodeSignatureAlgorithms(extensions.SignatureAlgorithms));
            if (extensions.Cookie.Length > 0)
            {
                var cookieWriter = new DtlsHandshakeWriter();
                cookieWriter.WriteOpaque16(extensions.Cookie);
                WriteExtension(extensionsWriter, 44, cookieWriter.ToArray());
            }

            return extensionsWriter.ToArray();
        }

        private static DtlsHelloExtensions DecodeExtensions(ReadOnlySpan<byte> extensionsBytes)
        {
            var reader = new DtlsHandshakeReader(extensionsBytes);
            var versions = new List<ushort>();
            var groups = new List<DtlsNamedCurve>();
            var keyShares = new List<DtlsKeyShareEntry>();
            var signatures = new List<DtlsSignatureScheme>();
            byte[] cookie = [];
            while (!reader.EndOfData)
            {
                ushort extensionType = reader.ReadUInt16();
                ReadOnlySpan<byte> extensionData = reader.ReadOpaque16();
                switch (extensionType)
                {
                    case 43:
                        versions.AddRange(DecodeSupportedVersions(extensionData));
                        break;
                    case 10:
                        groups.AddRange(DecodeSupportedGroups(extensionData));
                        break;
                    case 51:
                        keyShares.AddRange(DecodeKeyShares(extensionData));
                        break;
                    case 13:
                        signatures.AddRange(DecodeSignatureAlgorithms(extensionData));
                        break;
                    case 44:
                        cookie = new DtlsHandshakeReader(extensionData).ReadOpaque16();
                        break;
                    default:
                        throw new DtlsHandshakeException("Unsupported DTLS 1.3 extension was received.");
                }
            }

            return new DtlsHelloExtensions(versions, groups, keyShares, signatures, cookie);
        }

        private static void WriteExtension(DtlsHandshakeWriter writer, ushort extensionType, ReadOnlySpan<byte> body)
        {
            writer.WriteUInt16(extensionType);
            writer.WriteOpaque16(body);
        }

        private static byte[] EncodeSupportedVersions(IReadOnlyList<ushort> versions)
        {
            var writer = new DtlsHandshakeWriter();
            writer.WriteByte((byte)(versions.Count * 2));
            foreach (ushort version in versions)
            {
                writer.WriteUInt16(version);
            }

            return writer.ToArray();
        }

        private static List<ushort> DecodeSupportedVersions(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            ReadOnlySpan<byte> versions = reader.ReadOpaque8();
            if ((versions.Length & 1) != 0)
            {
                throw new DtlsHandshakeException("supported_versions has an odd length.");
            }

            var result = new List<ushort>();
            for (int ii = 0; ii < versions.Length; ii += 2)
            {
                result.Add(BinaryPrimitives.ReadUInt16BigEndian(versions.Slice(ii, 2)));
            }

            reader.EnsureComplete();
            return result;
        }

        private static byte[] EncodeSupportedGroups(IReadOnlyList<DtlsNamedCurve> groups)
        {
            var writer = new DtlsHandshakeWriter();
            writer.WriteUInt16((ushort)(groups.Count * 2));
            foreach (DtlsNamedCurve group in groups)
            {
                writer.WriteUInt16(ToWireNamedGroup(group));
            }

            return writer.ToArray();
        }

        private static List<DtlsNamedCurve> DecodeSupportedGroups(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            ReadOnlySpan<byte> groups = reader.ReadOpaque16();
            var result = new List<DtlsNamedCurve>();
            for (int ii = 0; ii < groups.Length; ii += 2)
            {
                result.Add(FromWireNamedGroup(BinaryPrimitives.ReadUInt16BigEndian(groups.Slice(ii, 2))));
            }

            reader.EnsureComplete();
            return result;
        }
        private static byte[] EncodeKeyShares(IReadOnlyList<DtlsKeyShareEntry> keyShares)
        {
            var body = new DtlsHandshakeWriter();
            foreach (DtlsKeyShareEntry keyShare in keyShares)
            {
                body.WriteUInt16(ToWireNamedGroup(keyShare.Group));
                body.WriteOpaque16(keyShare.KeyExchange);
            }

            var writer = new DtlsHandshakeWriter();
            writer.WriteOpaque16(body.ToArray());
            return writer.ToArray();
        }

        private static List<DtlsKeyShareEntry> DecodeKeyShares(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            ReadOnlySpan<byte> entries = reader.ReadOpaque16();
            var entryReader = new DtlsHandshakeReader(entries);
            var result = new List<DtlsKeyShareEntry>();
            while (!entryReader.EndOfData)
            {
                DtlsNamedCurve group = FromWireNamedGroup(entryReader.ReadUInt16());
                result.Add(new DtlsKeyShareEntry(group, entryReader.ReadOpaque16()));
            }

            reader.EnsureComplete();
            return result;
        }

        private static byte[] EncodeSignatureAlgorithms(IReadOnlyList<DtlsSignatureScheme> schemes)
        {
            var writer = new DtlsHandshakeWriter();
            writer.WriteUInt16((ushort)(schemes.Count * 2));
            foreach (DtlsSignatureScheme scheme in schemes)
            {
                writer.WriteUInt16((ushort)scheme);
            }

            return writer.ToArray();
        }

        private static List<DtlsSignatureScheme> DecodeSignatureAlgorithms(ReadOnlySpan<byte> body)
        {
            var reader = new DtlsHandshakeReader(body);
            ReadOnlySpan<byte> schemes = reader.ReadOpaque16();
            var result = new List<DtlsSignatureScheme>();
            for (int ii = 0; ii < schemes.Length; ii += 2)
            {
                ushort scheme = BinaryPrimitives.ReadUInt16BigEndian(schemes.Slice(ii, 2));
                if (scheme is not ((ushort)DtlsSignatureScheme.EcdsaSecp256r1Sha256)
                    and not ((ushort)DtlsSignatureScheme.EcdsaSecp384r1Sha384))
                {
                    throw new DtlsHandshakeException("Only ECDSA SHA-2 signature algorithms are allowed for DTLS PubSub.");
                }

                result.Add((DtlsSignatureScheme)scheme);
            }

            reader.EnsureComplete();
            return result;
        }

        private static void ValidateSupportedVersions(DtlsHelloExtensions extensions)
        {
            if (!extensions.SupportedVersions.Contains(Dtls13Version))
            {
                throw new DtlsHandshakeException("DTLS supported_versions must include DTLS 1.3; downgrade is rejected.");
            }
        }

        private static byte[] EnsureLength(byte[] value, int length, string parameterName)
        {
            if (value.Length != length)
            {
                throw new ArgumentException("Unexpected TLS vector length.", parameterName);
            }

            return value;
        }
    }

}
