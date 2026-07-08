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
        /// <exception cref="NotSupportedException"></exception>
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
                    CryptoUtils.ZeroMemory(nonce);
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

                ApplySequenceNumberMask(
                    record.AsSpan(0, HeaderLength),
                    record.AsSpan(HeaderLength, SequenceNumberSampleLength));
                return record;
            }
            finally
            {
                if (innerPlaintextBuffer is not null)
                {
                    CryptoUtils.ZeroMemory(innerPlaintextBuffer.AsSpan(0, innerPlaintextLength));
                    ArrayPool<byte>.Shared.Return(innerPlaintextBuffer);
                }
            }
        }

        /// <summary>
        /// Authenticates and unprotects one record, rejecting replayed sequence numbers, throwing a
        /// <see cref="CryptographicException"/> if the record is malformed, forged or replayed.
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        public byte[] Open(ReadOnlySpan<byte> record)
        {
            if (!TryOpen(record, out byte[]? applicationData))
            {
                throw new CryptographicException(
                    "DTLS record is malformed, failed authentication, or was replayed.");
            }

            return applicationData!;
        }

        /// <summary>
        /// Attempts to authenticate and unprotect one record. The record is fully authenticated
        /// (AEAD decrypt or integrity-only HMAC) BEFORE the anti-replay window is advanced so that
        /// malformed, forged or replayed datagrams cannot poison the replay window. RFC 9147 §4.5.2
        /// callers silently drop a record when this returns <see langword="false"/>.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public bool TryOpen(ReadOnlySpan<byte> record, out byte[]? applicationData)
        {
            ThrowIfDisposed();
            applicationData = null;
            if (record.Length < HeaderLength + 1 + m_tagLength ||
                record.Length < HeaderLength + SequenceNumberSampleLength)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[HeaderLength];
            record[..HeaderLength].CopyTo(header);
            try
            {
                ApplySequenceNumberMask(header, record.Slice(HeaderLength, SequenceNumberSampleLength));
                // Reconstruct the full 64-bit sequence number from the 16-bit on-wire
                // value (RFC 9147 §4.2.2): pick the value congruent to the truncated
                // bits that is closest to the highest accepted sequence number. Without
                // this the receiver's AEAD nonce and replay state desynchronize from the
                // sender's monotonic counter after 2^16 records in an epoch (SA-DTLS-CRYPTO-03).
                ushort truncatedSequence = BinaryPrimitives.ReadUInt16BigEndian(header[1..3]);
                ulong sequenceNumber = ReconstructSequenceNumber(truncatedSequence);
                if (ReadEpoch(header) != Epoch)
                {
                    return false;
                }

                int protectedLength = BinaryPrimitives.ReadUInt16BigEndian(header[3..5]);
                if (protectedLength != record.Length - HeaderLength || protectedLength <= m_tagLength)
                {
                    return false;
                }

                // Non-mutating replay peek before authentication: a still-needed early replay check
                // that must not advance the window. The window is only committed after the record is
                // proven authentic (CRYPTO-04 / HS-01).
                if (m_replayWindow.IsReplay(sequenceNumber))
                {
                    return false;
                }

                int contentLength = protectedLength - m_tagLength;
                byte[] plaintextBuffer = ArrayPool<byte>.Shared.Rent(contentLength);
                Span<byte> plaintext = plaintextBuffer.AsSpan(0, contentLength);
                try
                {
                    if (m_isAead)
                    {
#if NET8_0_OR_GREATER
                        Span<byte> nonce = stackalloc byte[NonceLength];
                        BuildNonce(sequenceNumber, nonce);
                        try
                        {
                            OpenAead(
                                nonce,
                                header,
                                record.Slice(HeaderLength, contentLength),
                                record.Slice(HeaderLength + contentLength, m_tagLength),
                                plaintext);
                        }
                        catch (CryptographicException)
                        {
                            CryptoUtils.ZeroMemory(nonce);
                            return false;
                        }

                        CryptoUtils.ZeroMemory(nonce);
#else
                        throw new NotSupportedException(
                            "AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
                    }
                    else
                    {
                        Span<byte> expectedTag = stackalloc byte[m_tagLength];
                        ComputeHmac(
                            header,
                            record.Slice(HeaderLength, contentLength),
                            expectedTag);
                        bool authenticated = CryptoUtils.FixedTimeEquals(
                            expectedTag,
                            record.Slice(HeaderLength + contentLength, m_tagLength));
                        CryptoUtils.ZeroMemory(expectedTag);
                        if (!authenticated)
                        {
                            return false;
                        }

                        record.Slice(HeaderLength, contentLength).CopyTo(plaintext);
                    }

                    if (plaintext.IsEmpty || plaintext[^1] != ApplicationDataContentType)
                    {
                        return false;
                    }

                    // Record is authenticated: now (and only now) advance the anti-replay window.
                    if (!m_replayWindow.TryAccept(sequenceNumber))
                    {
                        return false;
                    }

                    applicationData = plaintext[..^1].ToArray();
                    return true;
                }
                finally
                {
                    CryptoUtils.ZeroMemory(plaintext);
                    ArrayPool<byte>.Shared.Return(plaintextBuffer);
                }
            }
            finally
            {
                CryptoUtils.ZeroMemory(header);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptoUtils.ZeroMemory(m_key);
            CryptoUtils.ZeroMemory(m_iv);
            CryptoUtils.ZeroMemory(m_snKey);
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
                CryptoUtils.ZeroMemory(hash);
#else
                byte[] hash = hmac.ComputeHash(macInput, 0, input.Length);
                try
                {
                    hash.AsSpan(0, tag.Length).CopyTo(tag);
                }
                finally
                {
                    CryptoUtils.ZeroMemory(hash);
                }
#endif
            }
            finally
            {
                CryptoUtils.ZeroMemory(macInput.AsSpan(0, header.Length + plaintext.Length));
                ArrayPool<byte>.Shared.Return(macInput);
            }
        }

        private ulong ReconstructSequenceNumber(ushort truncatedSequence)
        {
            if (!m_replayWindow.HasHighest)
            {
                return truncatedSequence;
            }

            const ulong window = 1UL << 16;
            const ulong mask = window - 1;
            ulong expected = m_replayWindow.HighestSequenceNumber + 1;
            ulong candidate = (expected & ~mask) | truncatedSequence;
            if (candidate + (window / 2) < expected)
            {
                candidate += window;
            }
            else if (candidate >= window && candidate > expected + (window / 2))
            {
                candidate -= window;
            }

            return candidate;
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

            CryptoUtils.ZeroMemory(encoded);
        }

        /// <summary>
        /// Applies the RFC 9147 §4.2.3 record sequence-number mask to the encoded header. The mask
        /// is derived from a sample of the record ciphertext (not the near-constant header bytes):
        /// AES suites use AES-ECB over the ciphertext sample, ChaCha20 suites use the ChaCha20 block
        /// keystream (RFC 8446 §5.4), and integrity-only suites derive it from an HMAC over the
        /// ciphertext sample. XOR masking is symmetric, so the same routine seals and opens.
        /// </summary>
        private void ApplySequenceNumberMask(Span<byte> header, ReadOnlySpan<byte> ciphertextSample)
        {
            Span<byte> mask = stackalloc byte[2];
            ComputeSequenceNumberMask(ciphertextSample, mask);
            header[1] ^= mask[0];
            header[2] ^= mask[1];
            CryptoUtils.ZeroMemory(mask);
        }

        private void ComputeSequenceNumberMask(ReadOnlySpan<byte> ciphertextSample, Span<byte> mask)
        {
            ReadOnlySpan<byte> sample = ciphertextSample[..SequenceNumberSampleLength];
            switch (Profile.CipherSuite)
            {
                case DtlsCipherSuite.TlsAes128GcmSha256:
                case DtlsCipherSuite.TlsAes256GcmSha384:
#if NET8_0_OR_GREATER
                {
                    Span<byte> block = stackalloc byte[SequenceNumberSampleLength];
                    using (Aes aes = Aes.Create())
                    {
                        aes.Key = m_snKey;
                        aes.EncryptEcb(sample, block, PaddingMode.None);
                    }

                    block[..2].CopyTo(mask);
                    CryptoUtils.ZeroMemory(block);
                    break;
                }
#else
                    throw new NotSupportedException(
                        "AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
                case DtlsCipherSuite.TlsChaCha20Poly1305Sha256:
#if NET8_0_OR_GREATER
                    ChaCha20Mask(m_snKey, sample[..4], sample.Slice(4, 12), mask);
                    break;
#else
                    throw new NotSupportedException(
                        "AEAD DTLS record protection requires .NET 8 or later BCL primitives.");
#endif
                case DtlsCipherSuite.TlsSha256Sha256:
                case DtlsCipherSuite.TlsSha384Sha384:
                {
                    using HMAC hmac = new HMACSHA256(m_snKey);
#if NET8_0_OR_GREATER
                    Span<byte> hash = stackalloc byte[32];
                    if (!hmac.TryComputeHash(sample, hash, out int bytesWritten) || bytesWritten < 2)
                    {
                        throw new CryptographicException("Sequence-number mask HMAC did not produce a tag.");
                    }

                    mask[0] = hash[0];
                    mask[1] = hash[1];
                    CryptoUtils.ZeroMemory(hash);
#else
                    byte[] hash = hmac.ComputeHash(sample.ToArray());
                    try
                    {
                        mask[0] = hash[0];
                        mask[1] = hash[1];
                    }
                    finally
                    {
                        CryptoUtils.ZeroMemory(hash);
                    }
#endif
                    break;
                }
                default:
                    throw new NotSupportedException("Unsupported DTLS cipher suite for sequence-number masking.");
            }
        }

#if NET8_0_OR_GREATER
        private static void ChaCha20Mask(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> counter,
            ReadOnlySpan<byte> nonce,
            Span<byte> mask)
        {
            Span<uint> state = stackalloc uint[16];
            state[0] = 0x61707865;
            state[1] = 0x3320646e;
            state[2] = 0x79622d32;
            state[3] = 0x6b206574;
            for (int ii = 0; ii < 8; ii++)
            {
                state[4 + ii] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(ii * 4, 4));
            }

            state[12] = BinaryPrimitives.ReadUInt32LittleEndian(counter);
            state[13] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[..4]);
            state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(4, 4));
            state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(8, 4));
            Span<uint> working = stackalloc uint[16];
            state.CopyTo(working);
            for (int round = 0; round < 10; round++)
            {
                QuarterRound(working, 0, 4, 8, 12);
                QuarterRound(working, 1, 5, 9, 13);
                QuarterRound(working, 2, 6, 10, 14);
                QuarterRound(working, 3, 7, 11, 15);
                QuarterRound(working, 0, 5, 10, 15);
                QuarterRound(working, 1, 6, 11, 12);
                QuarterRound(working, 2, 7, 8, 13);
                QuarterRound(working, 3, 4, 9, 14);
            }

            uint firstWord = working[0] + state[0];
            Span<byte> keystream = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(keystream, firstWord);
            mask[0] = keystream[0];
            mask[1] = keystream[1];
            state.Clear();
            working.Clear();
            CryptoUtils.ZeroMemory(keystream);
        }

        private static void QuarterRound(Span<uint> state, int a, int b, int c, int d)
        {
            state[a] += state[b];
            state[d] = RotateLeft(state[d] ^ state[a], 16);
            state[c] += state[d];
            state[b] = RotateLeft(state[b] ^ state[c], 12);
            state[a] += state[b];
            state[d] = RotateLeft(state[d] ^ state[a], 8);
            state[c] += state[d];
            state[b] = RotateLeft(state[b] ^ state[c], 7);
        }

        private static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }
#endif

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
        private const int SequenceNumberSampleLength = 16;

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
