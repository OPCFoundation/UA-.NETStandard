/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
 *
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

namespace Opc.Ua.Aas
{
    /// <summary>
    /// Computes and formats the content digests the AAS registry publishes.
    /// </summary>
    /// <remarks>
    /// Clause 6.5.4 states the digest of a resource version and of a package
    /// as a case-sensitive algorithm name and a lower-case hexadecimal value.
    /// Both the Server that publishes a digest and the Client that verifies
    /// one have to agree on it exactly, so the rule is stated once here rather
    /// than in each of them.
    /// </remarks>
    public static class AasDigest
    {
        /// <summary>
        /// The SHA-256 algorithm name.
        /// </summary>
        public const string Sha256Name = "Sha256";

        /// <summary>
        /// The SHA-384 algorithm name.
        /// </summary>
        public const string Sha384Name = "Sha384";

        /// <summary>
        /// The SHA-512 algorithm name.
        /// </summary>
        public const string Sha512Name = "Sha512";

        /// <summary>
        /// Determines whether the name is one of the three algorithms the
        /// specification allows, compared case-sensitively.
        /// </summary>
        public static bool IsSupportedAlgorithm(string? digestAlg)
        {
            return string.Equals(digestAlg, Sha256Name, StringComparison.Ordinal) ||
                string.Equals(digestAlg, Sha384Name, StringComparison.Ordinal) ||
                string.Equals(digestAlg, Sha512Name, StringComparison.Ordinal);
        }

        /// <summary>
        /// Throws unless the name is one of the three algorithms the
        /// specification allows.
        /// </summary>
        public static void ValidateAlgorithm(string digestAlg)
        {
            if (digestAlg is null)
            {
                throw new ArgumentNullException(nameof(digestAlg));
            }
            if (!IsSupportedAlgorithm(digestAlg))
            {
                throw new ArgumentException(
                    "DigestAlg must be exactly Sha256, Sha384 or Sha512.",
                    nameof(digestAlg));
            }
        }

        /// <summary>
        /// Computes the digest of the content under the named algorithm.
        /// </summary>
        public static ByteString Compute(ReadOnlySpan<byte> content, string digestAlg)
        {
            ValidateAlgorithm(digestAlg);
            return ByteString.From(ComputeHash(content, digestAlg));
        }

        /// <summary>
        /// Computes the digest of the content under the named algorithm.
        /// </summary>
        public static ByteString Compute(ByteString content, string digestAlg)
        {
            return Compute(content.IsNull ? default : content.Span, digestAlg);
        }

        /// <summary>
        /// Computes the digest of the content as lower-case hexadecimal
        /// without an algorithm prefix.
        /// </summary>
        public static string ComputeHex(ReadOnlySpan<byte> content, string digestAlg)
        {
            ValidateAlgorithm(digestAlg);
            return ToHex(ComputeHash(content, digestAlg));
        }

        /// <summary>
        /// Computes the digest of the content as lower-case hexadecimal
        /// without an algorithm prefix.
        /// </summary>
        public static string ComputeHex(ByteString content, string digestAlg)
        {
            return ComputeHex(content.IsNull ? default : content.Span, digestAlg);
        }

        /// <summary>
        /// Formats bytes as lower-case hexadecimal.
        /// </summary>
        public static string ToHex(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }
            char[] chars = new char[bytes.Length * 2];
            for (int ii = 0; ii < bytes.Length; ii++)
            {
                byte value = bytes[ii];
                chars[ii * 2] = Nibble(value >> 4);
                chars[(ii * 2) + 1] = Nibble(value & 0xF);
            }
            return new string(chars);
        }

        /// <summary>
        /// Formats a digest as lower-case hexadecimal.
        /// </summary>
        public static string ToHex(ByteString digest)
        {
            return digest.IsNull ? string.Empty : ToHex(digest.Span);
        }

        /// <summary>
        /// Determines whether the value is a non-empty lower-case hexadecimal
        /// string of whole octets and carries no algorithm prefix.
        /// </summary>
        public static bool IsHex(string? value)
        {
            if (string.IsNullOrEmpty(value) || value!.Length % 2 != 0)
            {
                return false;
            }
            foreach (char c in value)
            {
                if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether the content matches the published digest.
        /// </summary>
        public static bool Matches(ByteString content, string digestAlg, string? digest)
        {
            if (!IsSupportedAlgorithm(digestAlg) || !IsHex(digest))
            {
                return false;
            }
            return string.Equals(ComputeHex(content, digestAlg), digest, StringComparison.Ordinal);
        }

        private static byte[] ComputeHash(ReadOnlySpan<byte> content, string digestAlg)
        {
            if (string.Equals(digestAlg, Sha256Name, StringComparison.Ordinal))
            {
#if NET5_0_OR_GREATER
                return SHA256.HashData(content);
#else
                using SHA256 sha = SHA256.Create();
                return sha.ComputeHash(content.ToArray());
#endif
            }
            if (string.Equals(digestAlg, Sha384Name, StringComparison.Ordinal))
            {
#if NET5_0_OR_GREATER
                return SHA384.HashData(content);
#else
                using SHA384 sha = SHA384.Create();
                return sha.ComputeHash(content.ToArray());
#endif
            }
#if NET5_0_OR_GREATER
            return SHA512.HashData(content);
#else
            using SHA512 sha512 = SHA512.Create();
            return sha512.ComputeHash(content.ToArray());
#endif
        }

        private static char Nibble(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }
}
