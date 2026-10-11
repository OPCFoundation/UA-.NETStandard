/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// Opc.Ua.Core.Tests also compiles this file on .NET 8+ to cross-check it
// against the BCL ciphers.
#if NETFRAMEWORK || AEAD_POLYFILL_CROSSCHECK
using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// AES-GCM for .NET Framework, whose BCL has no
    /// <c>System.Security.Cryptography.AesGcm</c>.
    /// </summary>
    /// <remarks>
    /// The members have the shape of the .NET 8 type so that callers compile
    /// unchanged on every target. The cipher runs in the managed BouncyCastle
    /// implementation, which is not a validated (FIPS) module.
    /// </remarks>
    internal sealed class AesGcm : IDisposable
    {
        /// <summary>
        /// Always <c>true</c>; the managed implementation runs everywhere.
        /// </summary>
        public static bool IsSupported => true;

        /// <summary>
        /// Creates the cipher for a key.
        /// </summary>
        /// <param name="key">A 128, 192 or 256 bit key.</param>
        /// <param name="tagSizeInBytes">The tag size, 12 to 16 bytes.</param>
        /// <exception cref="CryptographicException">The key has an invalid size.</exception>
        /// <exception cref="ArgumentException">The tag size is invalid.</exception>
        public AesGcm(ReadOnlySpan<byte> key, int tagSizeInBytes)
        {
            if (key.Length is not (16 or 24 or 32))
            {
                throw new CryptographicException("Specified key is not a valid size for this algorithm.");
            }

            if (tagSizeInBytes is < 12 or > 16)
            {
                throw new ArgumentException(
                    "The specified tag is not a valid size for this algorithm.",
                    nameof(tagSizeInBytes));
            }

            m_key = key.ToArray();
            TagSizeInBytes = tagSizeInBytes;
        }

        /// <summary>
        /// The tag size the cipher produces and expects.
        /// </summary>
        public int TagSizeInBytes { get; }

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> and computes its tag.
        /// </summary>
        /// <exception cref="ArgumentException">A buffer has an invalid size.</exception>
        /// <exception cref="ObjectDisposedException">The cipher was disposed.</exception>
        public void Encrypt(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData = default)
        {
            CheckParameters(nonce, plaintext.Length, ciphertext.Length, tag.Length);
            AeadCipher.Encrypt(
                new GcmBlockCipher(new AesEngine()),
                GetKey(),
                nonce,
                plaintext,
                ciphertext,
                tag,
                associatedData);
        }

        /// <summary>
        /// Verifies the tag and decrypts <paramref name="ciphertext"/>.
        /// </summary>
        /// <exception cref="ArgumentException">A buffer has an invalid size.</exception>
        /// <exception cref="ObjectDisposedException">The cipher was disposed.</exception>
        /// <exception cref="CryptographicException">
        /// The tag did not verify; <paramref name="plaintext"/> is cleared.
        /// </exception>
        public void Decrypt(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData = default)
        {
            CheckParameters(nonce, plaintext.Length, ciphertext.Length, tag.Length);
            AeadCipher.Decrypt(
                new GcmBlockCipher(new AesEngine()),
                GetKey(),
                nonce,
                ciphertext,
                tag,
                plaintext,
                associatedData);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_key != null)
            {
                Array.Clear(m_key, 0, m_key.Length);
                m_key = null;
            }
        }

        private byte[] GetKey()
        {
            return m_key ?? throw new ObjectDisposedException(nameof(AesGcm));
        }

        private void CheckParameters(
            ReadOnlySpan<byte> nonce,
            int plaintextLength,
            int ciphertextLength,
            int tagLength)
        {
            if (nonce.Length != AeadCipher.NonceSize)
            {
                throw new ArgumentException(
                    "The specified nonce is not a valid size for this algorithm.",
                    nameof(nonce));
            }

            if (plaintextLength != ciphertextLength)
            {
                throw new ArgumentException("Plaintext and ciphertext must have the same length.");
            }

            if (tagLength != TagSizeInBytes)
            {
                throw new ArgumentException(
                    "The specified tag is not a valid size for this algorithm.",
                    nameof(tagLength));
            }
        }

        private byte[]? m_key;
    }

    /// <summary>
    /// ChaCha20-Poly1305 (RFC 8439) for .NET Framework, whose BCL has no
    /// <c>System.Security.Cryptography.ChaCha20Poly1305</c>.
    /// </summary>
    /// <remarks>
    /// The members have the shape of the .NET 8 type so that callers compile
    /// unchanged on every target. The cipher runs in the managed BouncyCastle
    /// implementation, which is not a validated (FIPS) module.
    /// </remarks>
    internal sealed class ChaCha20Poly1305 : IDisposable
    {
        /// <summary>
        /// Always <c>true</c>; the managed implementation runs everywhere.
        /// </summary>
        public static bool IsSupported => true;

        /// <summary>
        /// Creates the cipher for a key.
        /// </summary>
        /// <param name="key">A 256 bit key.</param>
        /// <exception cref="CryptographicException">The key has an invalid size.</exception>
        public ChaCha20Poly1305(ReadOnlySpan<byte> key)
        {
            if (key.Length != 32)
            {
                throw new CryptographicException("Specified key is not a valid size for this algorithm.");
            }

            m_key = key.ToArray();
        }

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> and computes its tag.
        /// </summary>
        /// <exception cref="ArgumentException">A buffer has an invalid size.</exception>
        /// <exception cref="ObjectDisposedException">The cipher was disposed.</exception>
        public void Encrypt(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData = default)
        {
            CheckParameters(nonce, plaintext.Length, ciphertext.Length, tag.Length);
            AeadCipher.Encrypt(
                new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305(),
                GetKey(),
                nonce,
                plaintext,
                ciphertext,
                tag,
                associatedData);
        }

        /// <summary>
        /// Verifies the tag and decrypts <paramref name="ciphertext"/>.
        /// </summary>
        /// <exception cref="ArgumentException">A buffer has an invalid size.</exception>
        /// <exception cref="ObjectDisposedException">The cipher was disposed.</exception>
        /// <exception cref="CryptographicException">
        /// The tag did not verify; <paramref name="plaintext"/> is cleared.
        /// </exception>
        public void Decrypt(
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData = default)
        {
            CheckParameters(nonce, plaintext.Length, ciphertext.Length, tag.Length);
            AeadCipher.Decrypt(
                new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305(),
                GetKey(),
                nonce,
                ciphertext,
                tag,
                plaintext,
                associatedData);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_key != null)
            {
                Array.Clear(m_key, 0, m_key.Length);
                m_key = null;
            }
        }

        private byte[] GetKey()
        {
            return m_key ?? throw new ObjectDisposedException(nameof(ChaCha20Poly1305));
        }

        private static void CheckParameters(
            ReadOnlySpan<byte> nonce,
            int plaintextLength,
            int ciphertextLength,
            int tagLength)
        {
            if (nonce.Length != AeadCipher.NonceSize)
            {
                throw new ArgumentException(
                    "The specified nonce is not a valid size for this algorithm.",
                    nameof(nonce));
            }

            if (plaintextLength != ciphertextLength)
            {
                throw new ArgumentException("Plaintext and ciphertext must have the same length.");
            }

            if (tagLength != AeadCipher.TagSize)
            {
                throw new ArgumentException(
                    "The specified tag is not a valid size for this algorithm.",
                    nameof(tagLength));
            }
        }

        private byte[]? m_key;
    }

    /// <summary>
    /// Runs a BouncyCastle AEAD cipher over spans with the semantics of the
    /// .NET 8 one shot APIs.
    /// </summary>
    internal static class AeadCipher
    {
        /// <summary>
        /// The 96 bit nonce both OPC UA AEAD ciphers use.
        /// </summary>
        public const int NonceSize = 12;

        /// <summary>
        /// The full 128 bit tag.
        /// </summary>
        public const int TagSize = 16;

        /// <summary>
        /// Encrypts into <paramref name="ciphertext"/> and <paramref name="tag"/>.
        /// </summary>
        public static void Encrypt(
            IAeadCipher cipher,
            byte[] key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData)
        {
            Init(cipher, forEncryption: true, key, nonce, tag.Length, associatedData);

            byte[] input = plaintext.ToArray();
            byte[] output = new byte[input.Length + tag.Length];
            try
            {
                int length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
                length += cipher.DoFinal(output, length);

                if (length != output.Length)
                {
                    throw new CryptographicException("The authenticated encryption produced an unexpected length.");
                }

                output.AsSpan(0, input.Length).CopyTo(ciphertext);
                output.AsSpan(input.Length).CopyTo(tag);
            }
            finally
            {
                Array.Clear(input, 0, input.Length);
                Array.Clear(output, 0, output.Length);
            }
        }

        /// <summary>
        /// Verifies <paramref name="tag"/> and decrypts into
        /// <paramref name="plaintext"/>, which is written only when the tag
        /// verifies and is cleared otherwise.
        /// </summary>
        /// <exception cref="CryptographicException">The tag did not verify.</exception>
        public static void Decrypt(
            IAeadCipher cipher,
            byte[] key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData)
        {
            Init(cipher, forEncryption: false, key, nonce, tag.Length, associatedData);

            // BouncyCastle takes the tag appended to the ciphertext.
            byte[] input = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(input);
            tag.CopyTo(input.AsSpan(ciphertext.Length));

            // GCM releases plaintext blocks before the tag is checked, so the
            // result stays in a scratch buffer until it verifies.
            byte[] output = new byte[ciphertext.Length];
            try
            {
                int length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
                length += cipher.DoFinal(output, length);

                if (length != output.Length)
                {
                    throw new CryptographicException("The authenticated decryption produced an unexpected length.");
                }

                output.CopyTo(plaintext);
            }
            catch (InvalidCipherTextException e)
            {
                plaintext.Clear();
                throw new CryptographicException(
                    "The computed authentication tag did not match the input authentication tag.",
                    e);
            }
            finally
            {
                Array.Clear(input, 0, input.Length);
                Array.Clear(output, 0, output.Length);
            }
        }

        private static void Init(
            IAeadCipher cipher,
            bool forEncryption,
            byte[] key,
            ReadOnlySpan<byte> nonce,
            int tagLength,
            ReadOnlySpan<byte> associatedData)
        {
            // A fresh cipher per operation: BouncyCastle refuses to encrypt twice
            // with one key and nonce on the same instance, and OPC UA derives a
            // new nonce per message anyway.
            cipher.Init(
                forEncryption,
                new AeadParameters(
                    new KeyParameter(key),
                    tagLength * 8,
                    nonce.ToArray()));

            if (!associatedData.IsEmpty)
            {
                byte[] aad = associatedData.ToArray();
                cipher.ProcessAadBytes(aad, 0, aad.Length);
            }
        }
    }
}
#endif
