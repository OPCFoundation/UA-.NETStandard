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
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// DTLS 1.3 connection-id-less unified record protection for Part 14 §7.3.2.4.
    /// </summary>
    public sealed class DtlsRecordProtection : IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsRecordProtection"/>.
        /// </summary>
        public DtlsRecordProtection(DtlsProfile profile, ReadOnlySpan<byte> trafficSecret, ushort epoch)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Epoch = epoch;
            m_hashAlgorithmName = GetHashAlgorithm(profile.CipherSuite);
            m_isAead = IsAead(profile.CipherSuite);
            m_tagLength = GetTagLength(profile.CipherSuite);
            int keyLength = GetKeyLength(profile.CipherSuite);
            m_key = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "key", ReadOnlySpan<byte>.Empty, keyLength);
            m_iv = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "iv", ReadOnlySpan<byte>.Empty, NonceLength);
            m_snKey = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "sn", ReadOnlySpan<byte>.Empty, keyLength);
        }

        /// <summary>
        /// Length of the emitted unified record header.
        /// </summary>
        public const int HeaderLength = 5;

        /// <summary>
        /// DTLS profile used for record protection.
        /// </summary>
        public DtlsProfile Profile { get; }

        /// <summary>
        /// DTLS epoch encoded into protected records.
        /// </summary>
        public ushort Epoch { get; }

        /// <summary>
        /// Protects one plaintext record and increments the write sequence number.
        /// </summary>
        public byte[] Seal(ReadOnlySpan<byte> plaintext)
        {
            ThrowIfDisposed();
            ulong sequenceNumber = m_writeSequenceNumber++;
            byte[] innerPlaintext = new byte[plaintext.Length + 1];
            plaintext.CopyTo(innerPlaintext);
            innerPlaintext[^1] = ApplicationDataContentType;
            int protectedLength = innerPlaintext.Length + m_tagLength;
            byte[] record = new byte[HeaderLength + protectedLength];
            WriteHeader(record.AsSpan(0, HeaderLength), Epoch, sequenceNumber, protectedLength);
            try
            {
                if (m_isAead)
                {
                    Span<byte> nonce = stackalloc byte[NonceLength];
                    BuildNonce(sequenceNumber, nonce);
                    SealAead(
                        nonce,
                        record.AsSpan(0, HeaderLength),
                        innerPlaintext,
                        record.AsSpan(HeaderLength, innerPlaintext.Length),
                        record.AsSpan(HeaderLength + innerPlaintext.Length, m_tagLength));
                    CryptographicOperations.ZeroMemory(nonce);
                }
                else
                {
                    innerPlaintext.CopyTo(record.AsSpan(HeaderLength));
                    ComputeHmac(
                        record.AsSpan(0, HeaderLength),
                        record.AsSpan(HeaderLength, innerPlaintext.Length),
                        record.AsSpan(HeaderLength + innerPlaintext.Length, m_tagLength));
                }

                MaskSequenceNumber(record.AsSpan(0, HeaderLength));
                return record;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(innerPlaintext);
            }
        }

        /// <summary>
        /// Authenticates and unprotects one record, rejecting replayed sequence numbers.
        /// </summary>
        public byte[] Open(ReadOnlySpan<byte> record)
        {
            ThrowIfDisposed();
            if (record.Length < HeaderLength + 1 + m_tagLength)
            {
                throw new CryptographicException("DTLS record is too short.");
            }

            byte[] working = record.ToArray();
            MaskSequenceNumber(working.AsSpan(0, HeaderLength));
            ulong sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(working.AsSpan(1, 2));
            if (ReadEpoch(working.AsSpan(0, HeaderLength)) != Epoch)
            {
                throw new CryptographicException("DTLS record epoch does not match the active read keys.");
            }

            int protectedLength = BinaryPrimitives.ReadUInt16BigEndian(working.AsSpan(3, 2));
            if (protectedLength != working.Length - HeaderLength || protectedLength <= m_tagLength)
            {
                throw new CryptographicException("DTLS record length is invalid.");
            }

            if (!m_replayWindow.TryAccept(sequenceNumber))
            {
                throw new CryptographicException("DTLS record replay detected.");
            }

            int contentLength = protectedLength - m_tagLength;
            byte[] plaintext = new byte[contentLength];
            try
            {
                if (m_isAead)
                {
                    Span<byte> nonce = stackalloc byte[NonceLength];
                    BuildNonce(sequenceNumber, nonce);
                    OpenAead(
                        nonce,
                        working.AsSpan(0, HeaderLength),
                        working.AsSpan(HeaderLength, contentLength),
                        working.AsSpan(HeaderLength + contentLength, m_tagLength),
                        plaintext);
                    CryptographicOperations.ZeroMemory(nonce);
                }
                else
                {
                    Span<byte> expectedTag = stackalloc byte[m_tagLength];
                    ComputeHmac(
                        working.AsSpan(0, HeaderLength),
                        working.AsSpan(HeaderLength, contentLength),
                        expectedTag);
                    if (!CryptographicOperations.FixedTimeEquals(
                        expectedTag,
                        working.AsSpan(HeaderLength + contentLength, m_tagLength)))
                    {
                        throw new CryptographicException("DTLS integrity-only record tag validation failed.");
                    }

                    working.AsSpan(HeaderLength, contentLength).CopyTo(plaintext);
                    CryptographicOperations.ZeroMemory(expectedTag);
                }

                if (plaintext.Length == 0 || plaintext[^1] != ApplicationDataContentType)
                {
                    throw new CryptographicException("DTLS record inner content type is invalid.");
                }

                byte[] applicationData = new byte[plaintext.Length - 1];
                Buffer.BlockCopy(plaintext, 0, applicationData, 0, applicationData.Length);
                return applicationData;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(working);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(m_key);
            CryptographicOperations.ZeroMemory(m_iv);
            CryptographicOperations.ZeroMemory(m_snKey);
            m_disposed = true;
        }

        private static void WriteHeader(Span<byte> destination, ushort epoch, ulong sequenceNumber, int protectedLength)
        {
            destination[0] = (byte)(UnifiedHeaderFixedBits | SequenceNumberLengthBits | ((epoch & 0x03) << 2));
            BinaryPrimitives.WriteUInt16BigEndian(destination[1..3], (ushort)sequenceNumber);
            BinaryPrimitives.WriteUInt16BigEndian(destination[3..5], checked((ushort)protectedLength));
        }

        private static ushort ReadEpoch(ReadOnlySpan<byte> header)
        {
            return (ushort)((header[0] >> 2) & 0x03);
        }

        private static HashAlgorithmName GetHashAlgorithm(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes256GcmSha384 or DtlsCipherSuite.TlsSha384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;
        }

        private static bool IsAead(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsAes128GcmSha256
                or DtlsCipherSuite.TlsAes256GcmSha384
                or DtlsCipherSuite.TlsChaCha20Poly1305Sha256;
        }
        private static int GetKeyLength(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite switch
            {
                DtlsCipherSuite.TlsAes128GcmSha256 => 16,
                DtlsCipherSuite.TlsAes256GcmSha384 => 32,
                DtlsCipherSuite.TlsChaCha20Poly1305Sha256 => 32,
                DtlsCipherSuite.TlsSha256Sha256 => 32,
                DtlsCipherSuite.TlsSha384Sha384 => 48,
                _ => throw new NotSupportedException("Unsupported DTLS cipher suite.")
            };
        }

        private static int GetTagLength(DtlsCipherSuite cipherSuite)
        {
            return cipherSuite is DtlsCipherSuite.TlsSha384Sha384 ? 48 : 16;
        }

        private void SealAead(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag)
        {
#if NET8_0_OR_GREATER
            switch (Profile.CipherSuite)
            {
                case DtlsCipherSuite.TlsAes128GcmSha256:
                case DtlsCipherSuite.TlsAes256GcmSha384:
                    using (var aesGcm = new AesGcm(m_key, AeadTagLength))
                    {
                        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    }
                    break;
                case DtlsCipherSuite.TlsChaCha20Poly1305Sha256:
                    if (!ChaCha20Poly1305.IsSupported)
                    {
                        throw new NotSupportedException("ChaCha20-Poly1305 is not supported by this platform.");
                    }

                    using (var chacha = new ChaCha20Poly1305(m_key))
                    {
                        chacha.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    }
                    break;
                default:
                    throw new NotSupportedException("Cipher suite is not AEAD-protected.");
            }
#else
            _ = nonce;
            _ = associatedData;
            _ = plaintext;
            _ = ciphertext;
            _ = tag;
            throw new NotSupportedException("AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
        }

        private void OpenAead(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext)
        {
#if NET8_0_OR_GREATER
            switch (Profile.CipherSuite)
            {
                case DtlsCipherSuite.TlsAes128GcmSha256:
                case DtlsCipherSuite.TlsAes256GcmSha384:
                    using (var aesGcm = new AesGcm(m_key, AeadTagLength))
                    {
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                    }
                    break;
                case DtlsCipherSuite.TlsChaCha20Poly1305Sha256:
                    if (!ChaCha20Poly1305.IsSupported)
                    {
                        throw new NotSupportedException("ChaCha20-Poly1305 is not supported by this platform.");
                    }

                    using (var chacha = new ChaCha20Poly1305(m_key))
                    {
                        chacha.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                    }
                    break;
                default:
                    throw new NotSupportedException("Cipher suite is not AEAD-protected.");
            }
#else
            _ = nonce;
            _ = associatedData;
            _ = ciphertext;
            _ = tag;
            _ = plaintext;
            throw new NotSupportedException("AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
        }

        private void ComputeHmac(ReadOnlySpan<byte> header, ReadOnlySpan<byte> plaintext, Span<byte> tag)
        {
            byte[] key = (byte[])m_key.Clone();
            try
            {
                using HMAC hmac = DtlsHkdf.CreateHmac(m_hashAlgorithmName, key);
                byte[] headerBytes = header.ToArray();
                byte[] plaintextBytes = plaintext.ToArray();
                try
                {
                    _ = hmac.TransformBlock(headerBytes, 0, headerBytes.Length, headerBytes, 0);
                    _ = hmac.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
                    ReadOnlySpan<byte> hash = hmac.Hash ?? throw new CryptographicException("HMAC did not produce a tag.");
                    hash[..tag.Length].CopyTo(tag);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(headerBytes);
                    CryptographicOperations.ZeroMemory(plaintextBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private void BuildNonce(ulong sequenceNumber, Span<byte> nonce)
        {
            m_iv.CopyTo(nonce);
            Span<byte> encoded = stackalloc byte[NonceLength];
            BinaryPrimitives.WriteUInt64BigEndian(encoded[4..], sequenceNumber);
            for (int ii = 0; ii < nonce.Length; ii++)
            {
                nonce[ii] ^= encoded[ii];
            }

            CryptographicOperations.ZeroMemory(encoded);
        }

        private void MaskSequenceNumber(Span<byte> header)
        {
            Span<byte> input = stackalloc byte[3];
            input[0] = header[0];
            input[1] = header[3];
            input[2] = header[4];
            byte[] key = (byte[])m_snKey.Clone();
            try
            {
                using HMAC hmac = new HMACSHA256(key);
                byte[] hash = hmac.ComputeHash(input.ToArray());
                try
                {
                    header[1] ^= hash[0];
                    header[2] ^= hash[1];
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hash);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(input);
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(DtlsRecordProtection));
            }
        }

        private const byte UnifiedHeaderFixedBits = 0x20;
        private const byte SequenceNumberLengthBits = 0x01;
        private const byte ApplicationDataContentType = 0x17;
        private const int NonceLength = 12;
        private const int AeadTagLength = 16;

        private readonly HashAlgorithmName m_hashAlgorithmName;
        private readonly byte[] m_key;
        private readonly byte[] m_iv;
        private readonly byte[] m_snKey;
        private readonly DtlsAntiReplayWindow m_replayWindow = new();
        private readonly int m_tagLength;
        private readonly bool m_isAead;
        private ulong m_writeSequenceNumber;
        private bool m_disposed;
    }
}

