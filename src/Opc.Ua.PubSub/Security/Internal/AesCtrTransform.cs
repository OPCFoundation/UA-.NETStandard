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

namespace Opc.Ua.PubSub.Security.Internal
{
    /// <summary>
    /// Manual AES-CTR keystream generator. The .NET BCL does not expose
    /// an explicit <c>CipherMode.CTR</c>; this helper implements counter
    /// mode by encrypting 16-byte counter blocks with AES-ECB and XOR-ing
    /// the resulting keystream against the caller-supplied plaintext or
    /// ciphertext.
    /// </summary>
    /// <remarks>
    /// Implements the AES-CTR primitive referenced by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see> and the nonce
    /// layout from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156)</see>. The 16-byte counter block
    /// is composed of the 12-byte <c>MessageNonce</c> followed by a
    /// big-endian 32-bit block counter starting at zero, as specified by
    /// NIST SP 800-38A §6.5.
    /// </remarks>
    internal static class AesCtrTransform
    {
        /// <summary>
        /// Length of the AES block in bytes.
        /// </summary>
        public const int BlockSize = 16;

        /// <summary>
        /// Length of the spec-mandated AES-CTR nonce in bytes.
        /// </summary>
        public const int NonceLength = 12;

        /// <summary>
        /// Encrypts or decrypts <paramref name="input"/> using AES-CTR
        /// where the initial counter is composed of the spec layout
        /// <c>nonce(12) || blockCounter(4 BE)</c> with the block counter
        /// starting at zero.
        /// </summary>
        /// <param name="key">AES key (16, 24 or 32 bytes).</param>
        /// <param name="nonce">12-byte message nonce.</param>
        /// <param name="input">Plaintext or ciphertext.</param>
        /// <param name="output">
        /// Destination buffer; must be at least <c>input.Length</c>
        /// bytes long.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        public static void EncryptOrDecrypt(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> input,
            Span<byte> output)
        {
            ValidateKey(key);
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"AES-CTR nonce must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            if (output.Length < input.Length)
            {
                throw new ArgumentException(
                    "Output buffer is shorter than input.",
                    nameof(output));
            }

            Span<byte> counter = stackalloc byte[BlockSize];
            nonce.CopyTo(counter);
            counter[12] = 0;
            counter[13] = 0;
            counter[14] = 0;
            counter[15] = 0;

            TransformWithCounter(key, counter, input, output);
        }

        /// <summary>
        /// Encrypts or decrypts <paramref name="input"/> using AES-CTR
        /// with a caller-supplied 16-byte initial counter block. Used by
        /// known-answer tests that follow the NIST SP 800-38A vector
        /// format (where the 16-byte counter is given directly rather
        /// than split into <c>nonce || blockCounter</c>).
        /// </summary>
        /// <param name="key">AES key (16, 24 or 32 bytes).</param>
        /// <param name="initialCounter16">16-byte initial counter.</param>
        /// <param name="input">Plaintext or ciphertext.</param>
        /// <param name="output">
        /// Destination buffer; must be at least <c>input.Length</c>
        /// bytes long.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        public static void EncryptOrDecryptWithCounter(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> initialCounter16,
            ReadOnlySpan<byte> input,
            Span<byte> output)
        {
            ValidateKey(key);
            if (initialCounter16.Length != BlockSize)
            {
                throw new ArgumentException(
                    $"Initial counter must be exactly {BlockSize} bytes.",
                    nameof(initialCounter16));
            }
            if (output.Length < input.Length)
            {
                throw new ArgumentException(
                    "Output buffer is shorter than input.",
                    nameof(output));
            }

            Span<byte> counter = stackalloc byte[BlockSize];
            initialCounter16.CopyTo(counter);

            TransformWithCounter(key, counter, input, output);
        }

        private static void TransformWithCounter(
            ReadOnlySpan<byte> key,
            Span<byte> counter,
            ReadOnlySpan<byte> input,
            Span<byte> output)
        {
            // AES-CTR is constructed by encrypting deterministic counter
            // blocks with the raw AES block cipher and XOR-ing the keystream
            // with the message. The block cipher is only ever applied to
            // unique counter blocks, never to message data directly, so the
            // standard ECB risks (block-level pattern leakage, replay) do not
            // apply to the message. Newer targets use the allocation-free
            // one-shot EncryptEcb API; older targets fall back to an
            // ECB ICryptoTransform that is fed one unique counter block at a
            // time.
            using var aes = Aes.Create();
            aes.Padding = PaddingMode.None;
            byte[] aesKey = key.ToArray();
            byte[] counterBuffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            byte[] keystreamBuffer = ArrayPool<byte>.Shared.Rent(BlockSize);
            try
            {
                aes.Key = aesKey;
#if !NET6_0_OR_GREATER
#pragma warning disable CA5358
                aes.Mode = CipherMode.ECB;
#pragma warning restore CA5358
                using ICryptoTransform encryptor = aes.CreateEncryptor();
#endif
                int processed = 0;
                while (processed < input.Length)
                {
                    counter.CopyTo(counterBuffer);
#if NET6_0_OR_GREATER
                    int produced = aes.EncryptEcb(
                        counterBuffer.AsSpan(0, BlockSize),
                        keystreamBuffer.AsSpan(0, BlockSize),
                        PaddingMode.None);
#else
                    int produced = encryptor.TransformBlock(
                        counterBuffer,
                        0,
                        BlockSize,
                        keystreamBuffer,
                        0);
#endif
                    if (produced != BlockSize)
                    {
                        throw new CryptographicException(
                            "AES-CTR keystream block had an unexpected length.");
                    }

                    int remaining = input.Length - processed;
                    int chunk = remaining < BlockSize ? remaining : BlockSize;
                    ReadOnlySpan<byte> keystream = keystreamBuffer.AsSpan(0, chunk);
                    ReadOnlySpan<byte> inSlice = input.Slice(processed, chunk);
                    Span<byte> outSlice = output.Slice(processed, chunk);
                    for (int i = 0; i < chunk; i++)
                    {
                        outSlice[i] = (byte)(inSlice[i] ^ keystream[i]);
                    }
                    processed += chunk;

                    IncrementBlockCounter(counter);
                }
            }
            finally
            {
                ClearSensitiveBuffer(aesKey);
                Array.Clear(counterBuffer, 0, BlockSize);
                Array.Clear(keystreamBuffer, 0, BlockSize);
                ArrayPool<byte>.Shared.Return(counterBuffer);
                ArrayPool<byte>.Shared.Return(keystreamBuffer);
            }
        }

        private static void ValidateKey(ReadOnlySpan<byte> key)
        {
            if (key.Length is not 16 and not 24 and not 32)
            {
                throw new ArgumentException(
                    "AES key must be 16, 24, or 32 bytes long.",
                    nameof(key));
            }
        }

        private static void IncrementBlockCounter(Span<byte> counter)
        {
            // NIST SP 800-38A increments the entire 16-byte block as a
            // big-endian integer; for PubSub the high 12 bytes are the
            // fixed nonce and the low 4 bytes are the per-block counter,
            // so a 32-bit increment is sufficient for any practical
            // single-message length (max 2^32 * 16 = 64 GiB per message).
            // Carry is propagated into the upper 12 bytes for parity with
            // the NIST KAT vectors used by the unit tests.
            for (int i = BlockSize - 1; i >= 0; i--)
            {
                if (++counter[i] != 0)
                {
                    return;
                }
            }
        }

        private static void ClearSensitiveBuffer(byte[] buffer)
        {
            CryptoUtils.ZeroMemory(buffer);
        }

        /// <summary>
        /// Helper used by tests; equivalent to
        /// <see cref="EncryptOrDecrypt"/> but advances the per-block
        /// counter by 1 starting from the supplied integer rather than
        /// zero. Not part of the public contract.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void EncryptOrDecryptWithStartingBlock(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            uint startingBlock,
            ReadOnlySpan<byte> input,
            Span<byte> output)
        {
            ValidateKey(key);
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"AES-CTR nonce must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            Span<byte> counter = stackalloc byte[BlockSize];
            nonce.CopyTo(counter);
            BinaryPrimitives.WriteUInt32BigEndian(counter[12..], startingBlock);
            TransformWithCounter(key, counter, input, output);
        }
    }
}
