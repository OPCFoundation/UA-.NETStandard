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
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Dtls
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
            m_key = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "key", [], keyLength);
            m_iv = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "iv", [], NonceLength);
            m_snKey = DtlsHkdf.ExpandLabel(m_hashAlgorithmName, trafficSecret, "sn", [], keyLength);
#if NET8_0_OR_GREATER
            if (profile.CipherSuite is DtlsCipherSuite.TlsAes128GcmSha256 or DtlsCipherSuite.TlsAes256GcmSha384)
            {
                m_aesGcm = new AesGcm(m_key, 16);
            }
            else if (profile.CipherSuite == DtlsCipherSuite.TlsChaCha20Poly1305Sha256)
            {
                if (!ChaCha20Poly1305.IsSupported)
                {
                    throw new NotSupportedException("ChaCha20-Poly1305 is not supported by this platform.");
                }

                m_chacha20Poly1305 = new ChaCha20Poly1305(m_key);
            }
#endif
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
            int innerPlaintextLength = plaintext.Length + 1;
            int protectedLength = innerPlaintextLength + m_tagLength;
            byte[] record = new byte[HeaderLength + protectedLength];
            WriteHeader(record.AsSpan(0, HeaderLength), Epoch, sequenceNumber, protectedLength);
            byte[]? innerPlaintextBuffer = null;
            try
            {
                if (m_isAead)
                {
                    innerPlaintextBuffer = ArrayPool<byte>.Shared.Rent(innerPlaintextLength);
                    Span<byte> innerPlaintext = innerPlaintextBuffer.AsSpan(0, innerPlaintextLength);
                    plaintext.CopyTo(innerPlaintext);
                    innerPlaintext[^1] = ApplicationDataContentType;
                    Span<byte> nonce = stackalloc byte[NonceLength];
                    BuildNonce(sequenceNumber, nonce);
#if NET8_0_OR_GREATER
                    SealAead(
                        nonce,
                        record.AsSpan(0, HeaderLength),
                        innerPlaintext,
                        record.AsSpan(HeaderLength, innerPlaintext.Length),
                        record.AsSpan(HeaderLength + innerPlaintext.Length, m_tagLength));
                    DtlsCryptographicOperations.ZeroMemory(nonce);
#else
                    throw new NotSupportedException("AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
                }
                else
                {
                    plaintext.CopyTo(record.AsSpan(HeaderLength));
                    record[HeaderLength + plaintext.Length] = ApplicationDataContentType;
                    ComputeHmac(
                        record.AsSpan(0, HeaderLength),
                        record.AsSpan(HeaderLength, innerPlaintextLength),
                        record.AsSpan(HeaderLength + innerPlaintextLength, m_tagLength));
                }

                MaskSequenceNumber(record.AsSpan(0, HeaderLength));
                return record;
            }
            finally
            {
                if (innerPlaintextBuffer is not null)
                {
                    DtlsCryptographicOperations.ZeroMemory(innerPlaintextBuffer.AsSpan(0, innerPlaintextLength));
                    ArrayPool<byte>.Shared.Return(innerPlaintextBuffer);
                }
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

            Span<byte> header = stackalloc byte[HeaderLength];
            record[..HeaderLength].CopyTo(header);
            MaskSequenceNumber(header);
            ulong sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(header[1..3]);
            if (ReadEpoch(header) != Epoch)
            {
                throw new CryptographicException("DTLS record epoch does not match the active read keys.");
            }

            int protectedLength = BinaryPrimitives.ReadUInt16BigEndian(header[3..5]);
            if (protectedLength != record.Length - HeaderLength || protectedLength <= m_tagLength)
            {
                throw new CryptographicException("DTLS record length is invalid.");
            }

            if (!m_replayWindow.TryAccept(sequenceNumber))
            {
                throw new CryptographicException("DTLS record replay detected.");
            }

            int contentLength = protectedLength - m_tagLength;
            byte[] plaintextBuffer = ArrayPool<byte>.Shared.Rent(contentLength);
            Span<byte> plaintext = plaintextBuffer.AsSpan(0, contentLength);
            try
            {
                if (m_isAead)
                {
                    Span<byte> nonce = stackalloc byte[NonceLength];
                    BuildNonce(sequenceNumber, nonce);
#if NET8_0_OR_GREATER
                    OpenAead(
                        nonce,
                        header,
                        record.Slice(HeaderLength, contentLength),
                        record.Slice(HeaderLength + contentLength, m_tagLength),
                        plaintext);
                    DtlsCryptographicOperations.ZeroMemory(nonce);
#else
                    throw new NotSupportedException("AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
                }
                else
                {
                    Span<byte> expectedTag = stackalloc byte[m_tagLength];
                    ComputeHmac(
                        header,
                        record.Slice(HeaderLength, contentLength),
                        expectedTag);
                    if (!DtlsCryptographicOperations.FixedTimeEquals(
                        expectedTag,
                        record.Slice(HeaderLength + contentLength, m_tagLength)))
                    {
                        throw new CryptographicException("DTLS integrity-only record tag validation failed.");
                    }

                    record.Slice(HeaderLength, contentLength).CopyTo(plaintext);
                    DtlsCryptographicOperations.ZeroMemory(expectedTag);
                }

                if (plaintext.IsEmpty || plaintext[^1] != ApplicationDataContentType)
                {
                    throw new CryptographicException("DTLS record inner content type is invalid.");
                }

                byte[] applicationData = plaintext[..^1].ToArray();
                return applicationData;
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(header);
                DtlsCryptographicOperations.ZeroMemory(plaintext);
                ArrayPool<byte>.Shared.Return(plaintextBuffer);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            DtlsCryptographicOperations.ZeroMemory(m_key);
            DtlsCryptographicOperations.ZeroMemory(m_iv);
            DtlsCryptographicOperations.ZeroMemory(m_snKey);
#if NET8_0_OR_GREATER
            m_aesGcm?.Dispose();
            m_chacha20Poly1305?.Dispose();
#endif
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

#if NET8_0_OR_GREATER
        private void SealAead(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag)
        {
            switch (Profile.CipherSuite)
            {
                case DtlsCipherSuite.TlsAes128GcmSha256:
                case DtlsCipherSuite.TlsAes256GcmSha384:
                    AesGcm aesGcm = m_aesGcm ?? throw new ObjectDisposedException(nameof(DtlsRecordProtection));
                    aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    break;
                case DtlsCipherSuite.TlsChaCha20Poly1305Sha256:
                    ChaCha20Poly1305 chacha = m_chacha20Poly1305
                        ?? throw new ObjectDisposedException(nameof(DtlsRecordProtection));
                    chacha.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    break;
                default:
                    throw new NotSupportedException("Cipher suite is not AEAD-protected.");
            }
        }
#endif

#if NET8_0_OR_GREATER
        private void OpenAead(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> associatedData,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext)
        {
            switch (Profile.CipherSuite)
            {
                case DtlsCipherSuite.TlsAes128GcmSha256:
                case DtlsCipherSuite.TlsAes256GcmSha384:
                    AesGcm aesGcm = m_aesGcm ?? throw new ObjectDisposedException(nameof(DtlsRecordProtection));
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                    break;
                case DtlsCipherSuite.TlsChaCha20Poly1305Sha256:
                    ChaCha20Poly1305 chacha = m_chacha20Poly1305
                        ?? throw new ObjectDisposedException(nameof(DtlsRecordProtection));
                    chacha.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                    break;
                default:
                    throw new NotSupportedException("Cipher suite is not AEAD-protected.");
            }
        }
#endif

        private void ComputeHmac(ReadOnlySpan<byte> header, ReadOnlySpan<byte> plaintext, Span<byte> tag)
        {
            using HMAC hmac = DtlsHkdf.CreateHmac(m_hashAlgorithmName, m_key);
            byte[] macInput = ArrayPool<byte>.Shared.Rent(header.Length + plaintext.Length);
            try
            {
                Span<byte> input = macInput.AsSpan(0, header.Length + plaintext.Length);
                header.CopyTo(input);
                plaintext.CopyTo(input[header.Length..]);
#if NET8_0_OR_GREATER
                Span<byte> hash = stackalloc byte[DtlsHkdf.GetHashLength(m_hashAlgorithmName)];
                if (!hmac.TryComputeHash(input, hash, out int bytesWritten) || bytesWritten < tag.Length)
                {
                    throw new CryptographicException("HMAC did not produce a tag.");
                }

                hash[..tag.Length].CopyTo(tag);
                DtlsCryptographicOperations.ZeroMemory(hash);
#else
                byte[] hash = hmac.ComputeHash(macInput, 0, input.Length);
                try
                {
                    hash.AsSpan(0, tag.Length).CopyTo(tag);
                }
                finally
                {
                    DtlsCryptographicOperations.ZeroMemory(hash);
                }
#endif
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(macInput.AsSpan(0, header.Length + plaintext.Length));
                ArrayPool<byte>.Shared.Return(macInput);
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

            DtlsCryptographicOperations.ZeroMemory(encoded);
        }

        private void MaskSequenceNumber(Span<byte> header)
        {
            Span<byte> input = [header[0], header[3], header[4]];
            using HMAC hmac = new HMACSHA256(m_snKey);
#if NET8_0_OR_GREATER
            Span<byte> hash = stackalloc byte[32];
            if (!hmac.TryComputeHash(input, hash, out int bytesWritten) || bytesWritten < 2)
            {
                throw new CryptographicException("Sequence-number mask HMAC did not produce a tag.");
            }

            header[1] ^= hash[0];
            header[2] ^= hash[1];
            DtlsCryptographicOperations.ZeroMemory(hash);
#else
            byte[] hash = hmac.ComputeHash(input.ToArray());
            try
            {
                header[1] ^= hash[0];
                header[2] ^= hash[1];
            }
            finally
            {
                DtlsCryptographicOperations.ZeroMemory(hash);
            }
#endif
            DtlsCryptographicOperations.ZeroMemory(input);
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

        private readonly HashAlgorithmName m_hashAlgorithmName;
        private readonly byte[] m_key;
        private readonly byte[] m_iv;
        private readonly byte[] m_snKey;
#if NET8_0_OR_GREATER
        private readonly AesGcm? m_aesGcm;
        private readonly ChaCha20Poly1305? m_chacha20Poly1305;
#endif
        private readonly DtlsAntiReplayWindow m_replayWindow = new();
        private readonly int m_tagLength;
        private readonly bool m_isAead;
        private ulong m_writeSequenceNumber;
        private bool m_disposed;
    }
}
