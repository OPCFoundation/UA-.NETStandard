/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if !NET6_0_OR_GREATER
using System.IO;

namespace System.Security.Cryptography
{
    /// <summary>
    /// Allocating random-byte generation on targets without the framework static method.
    /// </summary>
    public static class RandomNumberGeneratorPolyfills
    {
        extension(RandomNumberGenerator)
        {
            /// <summary>
            /// Allocates and fills a buffer with cryptographically strong random bytes.
            /// </summary>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
            public static byte[] GetBytes(int count)
            {
                if (count < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(count));
                }
                byte[] bytes = new byte[count];
                if (count > 0)
                {
                    using var generator = RandomNumberGenerator.Create();
                    generator.GetBytes(bytes);
                }
                return bytes;
            }
        }
    }

    /// <summary>
    /// SHA-256 one-shot hashing on targets without the framework static methods.
    /// </summary>
    public static class Polyfills
    {
        extension(SHA256)
        {
            /// <summary>
            /// Computes the SHA-256 digest of the supplied bytes.
            /// </summary>
            public static byte[] HashData(byte[] source)
            {
                if (source is null)
                {
                    throw new ArgumentNullException(nameof(source));
                }
                using var algorithm = SHA256.Create();
                return algorithm.ComputeHash(source);
            }

            /// <summary>
            /// Computes the SHA-256 digest from the current stream position
            /// without taking ownership of the stream.
            /// </summary>
            public static byte[] HashData(Stream source)
            {
                if (source is null)
                {
                    throw new ArgumentNullException(nameof(source));
                }
                using var algorithm = SHA256.Create();
                return algorithm.ComputeHash(source);
            }
        }
    }
}
#endif
