// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Security.Cryptography;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// Hashing helper shared by the non exportable key test doubles.
    /// </summary>
    /// <remarks>
    /// The base implementations of <c>RSA.HashData</c> and <c>ECDsa.HashData</c>
    /// are not guaranteed to be usable on every target framework the stack
    /// supports, so the test doubles override them and route here instead.
    /// </remarks>
    internal static class NonExportableKeyHash
    {
        /// <summary>
        /// Hashes a region of a buffer with the requested algorithm.
        /// </summary>
        public static byte[] Compute(
            byte[] data,
            int offset,
            int count,
            HashAlgorithmName hashAlgorithm)
        {
            using HashAlgorithm algorithm = Create(hashAlgorithm);
            return algorithm.ComputeHash(data, offset, count);
        }

        /// <summary>
        /// Hashes a stream with the requested algorithm.
        /// </summary>
        public static byte[] Compute(Stream data, HashAlgorithmName hashAlgorithm)
        {
            using HashAlgorithm algorithm = Create(hashAlgorithm);
            return algorithm.ComputeHash(data);
        }

        private static HashAlgorithm Create(HashAlgorithmName hashAlgorithm)
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

            if (hashAlgorithm == HashAlgorithmName.SHA1)
            {
                // CA5350: SHA-1 is required here because the OPC UA specification's
                // deprecated Basic128Rsa15 and Basic256 security policies use it. This
                // is a test double that must be able to mirror every algorithm the
                // stack supports; it never selects SHA-1 on its own.
#pragma warning disable CA5350
                return SHA1.Create();
#pragma warning restore CA5350
            }

            throw new CryptographicException(
                $"The hash algorithm '{hashAlgorithm.Name}' is not supported by the test double.");
        }
    }
}
