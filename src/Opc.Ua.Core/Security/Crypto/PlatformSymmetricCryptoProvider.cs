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
using System.Security.Cryptography;

namespace Opc.Ua
{
    /// <summary>
    /// The symmetric primitives the .NET platform supplies.
    /// </summary>
    /// <remarks>
    /// This performs exactly the operations the channel would otherwise perform
    /// inline, so registering it changes nothing. It exists so that the seam
    /// ships with an implementation rather than as an interface nothing satisfies,
    /// and so that a deployment running under
    /// <see cref="CryptoCompliancePolicy.FipsOnly"/> resolves the symmetric
    /// purposes to a provider that states its provenance like every other.
    /// </remarks>
    public sealed class PlatformSymmetricCryptoProvider : ISymmetricCryptoProvider
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static PlatformSymmetricCryptoProvider Instance { get; } = new();

        /// <inheritdoc/>
        public bool Supports(SymmetricEncryptionAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case SymmetricEncryptionAlgorithm.Aes128Cbc:
                case SymmetricEncryptionAlgorithm.Aes256Cbc:
                case SymmetricEncryptionAlgorithm.Aes128Ctr:
                case SymmetricEncryptionAlgorithm.Aes256Ctr:
                    return true;
                case SymmetricEncryptionAlgorithm.ChaCha20Poly1305:
                case SymmetricEncryptionAlgorithm.Aes128Gcm:
                case SymmetricEncryptionAlgorithm.Aes256Gcm:
#if NET8_0_OR_GREATER
                    return true;
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        /// <inheritdoc/>
        public bool Supports(SymmetricSignatureAlgorithm algorithm)
        {
            return algorithm is SymmetricSignatureAlgorithm.HmacSha1
                or SymmetricSignatureAlgorithm.HmacSha256
                or SymmetricSignatureAlgorithm.HmacSha384;
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a block cipher.
        /// </exception>
        public void Encrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext)
        {
            if (IsCounterMode(algorithm))
            {
                TransformCtr(key, iv, plaintext, ciphertext);
                return;
            }

            RequireCbc(algorithm);
            TransformCbc(key, iv, plaintext, ciphertext, encrypting: true);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a block cipher.
        /// </exception>
        public void Decrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext)
        {
            if (IsCounterMode(algorithm))
            {
                // Counter mode is its own inverse.
                TransformCtr(key, iv, ciphertext, plaintext);
                return;
            }

            RequireCbc(algorithm);
            TransformCbc(key, iv, ciphertext, plaintext, encrypting: false);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not an authenticated cipher, or the
        /// target framework does not supply it.
        /// </exception>
        public void EncryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData)
        {
#if NET8_0_OR_GREATER
            switch (algorithm)
            {
                case SymmetricEncryptionAlgorithm.Aes128Gcm:
                case SymmetricEncryptionAlgorithm.Aes256Gcm:
                {
                    using var aesGcm = new AesGcm(key, tag.Length);
                    aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    return;
                }
                case SymmetricEncryptionAlgorithm.ChaCha20Poly1305:
                {
                    using var chaCha = new ChaCha20Poly1305(key);
                    chaCha.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                    return;
                }
                default:
                    break;
            }
#endif
            throw NotAnAuthenticatedCipher(algorithm);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not an authenticated cipher, or the
        /// target framework does not supply it.
        /// </exception>
        public bool DecryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData)
        {
#if NET8_0_OR_GREATER
            switch (algorithm)
            {
                case SymmetricEncryptionAlgorithm.Aes128Gcm:
                case SymmetricEncryptionAlgorithm.Aes256Gcm:
                {
                    using var aesGcm = new AesGcm(key, tag.Length);
                    try
                    {
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                        return true;
                    }
                    catch (CryptographicException)
                    {
                        // The tag did not verify. Reported rather than thrown so
                        // the caller raises a protocol error.
                        return false;
                    }
                }
                case SymmetricEncryptionAlgorithm.ChaCha20Poly1305:
                {
                    using var chaCha = new ChaCha20Poly1305(key);
                    try
                    {
                        chaCha.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                        return true;
                    }
                    catch (CryptographicException)
                    {
                        return false;
                    }
                }
                default:
                    break;
            }
#endif
            throw NotAnAuthenticatedCipher(algorithm);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a message authentication code.
        /// </exception>
        public int GetSignatureLength(SymmetricSignatureAlgorithm algorithm)
        {
            return algorithm switch
            {
                SymmetricSignatureAlgorithm.HmacSha1 => 20,
                SymmetricSignatureAlgorithm.HmacSha256 => 32,
                SymmetricSignatureAlgorithm.HmacSha384 => 48,
                _ => throw NotAMac(algorithm)
            };
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a message authentication code.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// The platform produced an unexpected signature length.
        /// </exception>
        public void Sign(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> signature)
        {
            int length = GetSignatureLength(algorithm);

            if (signature.Length < length)
            {
                throw new ArgumentException(
                    $"A {algorithm} signature needs {length} bytes.",
                    nameof(signature));
            }

#if NET6_0_OR_GREATER
            int written = algorithm switch
            {
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
                SymmetricSignatureAlgorithm.HmacSha1
                    => HMACSHA1.HashData(key, data, signature),
#pragma warning restore CA5350
                SymmetricSignatureAlgorithm.HmacSha256
                    => HMACSHA256.HashData(key, data, signature),
                _ => HMACSHA384.HashData(key, data, signature)
            };

            if (written != length)
            {
                throw new CryptographicException(
                    $"The platform produced {written} bytes for a {algorithm} signature.");
            }
#else
            byte[] keyBytes = key.ToArray();

            try
            {
                using HMAC hmac = CreateHmac(algorithm, keyBytes);
                byte[] computed = hmac.ComputeHash(data.ToArray());
                computed.AsSpan(0, length).CopyTo(signature);
            }
            finally
            {
                Array.Clear(keyBytes, 0, keyBytes.Length);
            }
#endif
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// <paramref name="algorithm"/> is not a message authentication code.
        /// </exception>
        public bool Verify(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature)
        {
            int length = GetSignatureLength(algorithm);

            if (signature.Length != length)
            {
                return false;
            }

            Span<byte> computed = stackalloc byte[length];

            try
            {
                Sign(algorithm, key, data, computed);
                return CryptoUtils.FixedTimeEquals(computed, signature);
            }
            finally
            {
                computed.Clear();
            }
        }

        private PlatformSymmetricCryptoProvider()
        {
        }

        private const int kAesBlockSize = 16;
        private const int kCtrNonceLength = 12;

        private static void TransformCbc(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> input,
            Span<byte> output,
            bool encrypting)
        {
#pragma warning disable CA5401 // Symmetric encryption uses a non-default initialization vector
            using var aes = Aes.Create();

            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = key.ToArray();
            aes.IV = iv.ToArray();

            using ICryptoTransform transform = encrypting
                ? aes.CreateEncryptor()
                : aes.CreateDecryptor();
#pragma warning restore CA5401

            // TransformBlock has no span overload on any supported target, and
            // the channel already owns a contiguous array, so the copy here is
            // confined to callers that route through the provider seam.
            byte[] buffer = input.ToArray();

            try
            {
                transform.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
                buffer.AsSpan(0, input.Length).CopyTo(output);
            }
            finally
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }

#if !NET6_0_OR_GREATER
        private static HMAC CreateHmac(SymmetricSignatureAlgorithm algorithm, byte[] key)
        {
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            return algorithm switch
            {
                SymmetricSignatureAlgorithm.HmacSha1 => new HMACSHA1(key),
                SymmetricSignatureAlgorithm.HmacSha256 => new HMACSHA256(key),
                _ => new HMACSHA384(key)
            };
#pragma warning restore CA5350
        }
#endif

        private static bool IsCounterMode(SymmetricEncryptionAlgorithm algorithm)
        {
            return algorithm is SymmetricEncryptionAlgorithm.Aes128Ctr
                or SymmetricEncryptionAlgorithm.Aes256Ctr;
        }

        /// <summary>
        /// Applies AES counter mode, which is its own inverse.
        /// </summary>
        /// <remarks>
        /// The counter block is the twelve byte nonce followed by a big endian
        /// thirty two bit block counter starting at zero, which is the layout
        /// NIST SP 800-38A §6.5 defines and Part 14 §7.2.4.4.3.2 requires.
        /// </remarks>
        private static void TransformCtr(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> input,
            Span<byte> output)
        {
            if (nonce.Length != kCtrNonceLength)
            {
                throw new ArgumentException(
                    $"AES-CTR requires a {kCtrNonceLength} byte nonce.",
                    nameof(nonce));
            }

            if (output.Length < input.Length)
            {
                throw new ArgumentException(
                    "The destination is shorter than the input.",
                    nameof(output));
            }

            using var aes = Aes.Create();

            // CA5358: counter mode is built from the raw block cipher, so the
            // underlying mode is necessarily ECB. Confidentiality comes from
            // XOR-ing the key stream this produces, never from encrypting the
            // plaintext directly, and each counter block is used exactly once
            // under a given key and nonce. This is the construction NIST
            // SP 800-38A §6.5 defines.
#pragma warning disable CA5358
            aes.Mode = CipherMode.ECB;
#pragma warning restore CA5358
            aes.Padding = PaddingMode.None;
            aes.Key = key.ToArray();

            using ICryptoTransform encryptor = aes.CreateEncryptor();

            byte[] counter = new byte[kAesBlockSize];
            byte[] keyStream = new byte[kAesBlockSize];
            nonce.CopyTo(counter);

            try
            {
                for (int offset = 0; offset < input.Length; offset += kAesBlockSize)
                {
                    encryptor.TransformBlock(counter, 0, kAesBlockSize, keyStream, 0);

                    int count = Math.Min(kAesBlockSize, input.Length - offset);

                    for (int ii = 0; ii < count; ii++)
                    {
                        output[offset + ii] = (byte)(input[offset + ii] ^ keyStream[ii]);
                    }

                    IncrementCounter(counter);
                }
            }
            finally
            {
                Array.Clear(keyStream, 0, keyStream.Length);
                Array.Clear(counter, 0, counter.Length);
            }
        }

        private static void IncrementCounter(byte[] counter)
        {
            for (int ii = kAesBlockSize - 1; ii >= kCtrNonceLength; ii--)
            {
                if (++counter[ii] != 0)
                {
                    return;
                }
            }
        }

        private static void RequireCbc(SymmetricEncryptionAlgorithm algorithm)        {
            if (algorithm is not SymmetricEncryptionAlgorithm.Aes128Cbc and
                not SymmetricEncryptionAlgorithm.Aes256Cbc)
            {
                throw new NotSupportedException(
                    $"{algorithm} is not a block cipher handled by this provider.");
            }
        }

        private static NotSupportedException NotAnAuthenticatedCipher(
            SymmetricEncryptionAlgorithm algorithm)
        {
            return new NotSupportedException(
                $"{algorithm} is not an authenticated cipher available on this target framework.");
        }

        private static NotSupportedException NotAMac(SymmetricSignatureAlgorithm algorithm)
        {
            return new NotSupportedException(
                $"{algorithm} is not a message authentication code handled by this provider.");
        }
    }
}
