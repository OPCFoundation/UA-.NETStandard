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

using System;
using System.IO;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Hash helpers shared by the PKCS#11 key implementations.
    /// </summary>
    /// <remarks>
    /// PKCS#11 mechanisms name their hash algorithms with their own constants,
    /// and the PKCS#1 v1.5 signature mechanism expects a DER encoded DigestInfo
    /// rather than a bare hash. Both translations live here so the key types
    /// stay about key operations.
    /// </remarks>
    internal static class Pkcs11Digest
    {
        /// <summary>
        /// Maps a .NET hash algorithm to its PKCS#11 mechanism.
        /// </summary>
        /// <param name="hashAlgorithm">The hash algorithm.</param>
        /// <returns>The matching mechanism.</returns>
        /// <exception cref="CryptographicException">
        /// The hash algorithm has no supported mechanism. SHA-1 is deliberately
        /// absent: the stack does not sign with it.
        /// </exception>
        public static Net.Pkcs11Interop.Common.CKM ToMechanism(HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return Net.Pkcs11Interop.Common.CKM.CKM_SHA256;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return Net.Pkcs11Interop.Common.CKM.CKM_SHA384;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return Net.Pkcs11Interop.Common.CKM.CKM_SHA512;
            }

            throw new CryptographicException(
                $"Hash algorithm '{hashAlgorithm.Name}' is not supported by the PKCS#11 provider.");
        }

        /// <summary>
        /// Maps a .NET hash algorithm to the matching MGF1 variant.
        /// </summary>
        /// <param name="hashAlgorithm">The hash algorithm.</param>
        /// <returns>The matching mask generation function.</returns>
        /// <exception cref="CryptographicException">
        /// The hash algorithm has no supported mask generation function.
        /// </exception>
        public static Net.Pkcs11Interop.Common.CKG ToMaskGenerationFunction(
            HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return Net.Pkcs11Interop.Common.CKG.CKG_MGF1_SHA256;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return Net.Pkcs11Interop.Common.CKG.CKG_MGF1_SHA384;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return Net.Pkcs11Interop.Common.CKG.CKG_MGF1_SHA512;
            }

            throw new CryptographicException(
                $"Hash algorithm '{hashAlgorithm.Name}' is not supported by the PKCS#11 provider.");
        }

        /// <summary>
        /// Wraps a hash in the DER encoded DigestInfo that PKCS#1 v1.5 signing
        /// expects.
        /// </summary>
        /// <param name="hash">The hash to wrap.</param>
        /// <param name="hashAlgorithm">The algorithm that produced the hash.</param>
        /// <returns>The DigestInfo.</returns>
        /// <exception cref="CryptographicException">
        /// The hash algorithm is not supported, or the hash is the wrong length.
        /// </exception>
        public static byte[] WrapInDigestInfo(byte[] hash, HashAlgorithmName hashAlgorithm)
        {
            byte[] prefix = GetDigestInfoPrefix(hashAlgorithm, out int expectedLength);

            if (hash.Length != expectedLength)
            {
                throw new CryptographicException(
                    $"A {hashAlgorithm.Name} hash must be {expectedLength} bytes, " +
                    $"but {hash.Length} were supplied.");
            }

            var digestInfo = new byte[prefix.Length + hash.Length];

            Buffer.BlockCopy(prefix, 0, digestInfo, 0, prefix.Length);
            Buffer.BlockCopy(hash, 0, digestInfo, prefix.Length, hash.Length);

            return digestInfo;
        }

        /// <summary>
        /// Computes a hash over part of a buffer.
        /// </summary>
        /// <param name="data">The buffer to hash.</param>
        /// <param name="offset">Where to start.</param>
        /// <param name="count">How many bytes to hash.</param>
        /// <param name="hashAlgorithm">The hash algorithm.</param>
        /// <returns>The hash.</returns>
        public static byte[] Compute(
            byte[] data,
            int offset,
            int count,
            HashAlgorithmName hashAlgorithm)
        {
            using HashAlgorithm hash = CreateHashAlgorithm(hashAlgorithm);
            return hash.ComputeHash(data, offset, count);
        }

        /// <summary>
        /// Computes a hash over a stream.
        /// </summary>
        /// <param name="data">The stream to hash.</param>
        /// <param name="hashAlgorithm">The hash algorithm.</param>
        /// <returns>The hash.</returns>
        public static byte[] Compute(Stream data, HashAlgorithmName hashAlgorithm)
        {
            using HashAlgorithm hash = CreateHashAlgorithm(hashAlgorithm);
            return hash.ComputeHash(data);
        }

        private static HashAlgorithm CreateHashAlgorithm(HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return SHA256.Create();
            }

            if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return SHA384.Create();
            }

            if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return SHA512.Create();
            }

            throw new CryptographicException(
                $"Hash algorithm '{hashAlgorithm.Name}' is not supported by the PKCS#11 provider.");
        }

        private static byte[] GetDigestInfoPrefix(
            HashAlgorithmName hashAlgorithm,
            out int hashLength)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                hashLength = 32;
                return s_sha256DigestInfoPrefix;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                hashLength = 48;
                return s_sha384DigestInfoPrefix;
            }

            if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                hashLength = 64;
                return s_sha512DigestInfoPrefix;
            }

            throw new CryptographicException(
                $"Hash algorithm '{hashAlgorithm.Name}' is not supported by the PKCS#11 provider.");
        }

        /// <summary>
        /// DER prefix of DigestInfo for SHA-256, from RFC 8017 section 9.2 note 1.
        /// </summary>
        private static readonly byte[] s_sha256DigestInfoPrefix =
        [
            0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
            0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20
        ];

        /// <summary>
        /// DER prefix of DigestInfo for SHA-384.
        /// </summary>
        private static readonly byte[] s_sha384DigestInfoPrefix =
        [
            0x30, 0x41, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
            0x03, 0x04, 0x02, 0x02, 0x05, 0x00, 0x04, 0x30
        ];

        /// <summary>
        /// DER prefix of DigestInfo for SHA-512.
        /// </summary>
        private static readonly byte[] s_sha512DigestInfoPrefix =
        [
            0x30, 0x51, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
            0x03, 0x04, 0x02, 0x03, 0x05, 0x00, 0x04, 0x40
        ];
    }
}
