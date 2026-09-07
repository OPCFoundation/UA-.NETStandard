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

namespace Opc.Ua
{
    /// <summary>
    /// Derives the key material a secure channel and a session need from a shared
    /// secret.
    /// </summary>
    /// <remarks>
    /// This is a facet of <see cref="ICryptoProvider"/> rather than a member of
    /// it, so that a provider declares it only when it can serve
    /// <see cref="CryptoPurpose.KeyDerivation"/>, and so that adding it does not
    /// break providers written against the shipped interface.
    /// <para>
    /// Both families the stack uses reduce to the same shape: a secret and a seed
    /// in, a caller-sized block of key material out. P_SHA keys its HMAC with the
    /// secret and iterates over the seed; HKDF extracts with the seed as salt and
    /// expands. Expressing them alike keeps the channel free of algorithm
    /// specific branching.
    /// </para>
    /// </remarks>
    public interface IKeyDerivationProvider
    {
        /// <summary>
        /// Whether this provider can derive keys with an algorithm.
        /// </summary>
        /// <param name="algorithm">The algorithm being requested.</param>
        /// <returns>
        /// <c>true</c> when the algorithm is supported. A provider that returns
        /// <c>false</c> is bypassed in favour of the platform, so a partial
        /// implementation is legitimate.
        /// </returns>
        bool Supports(KeyDerivationAlgorithm algorithm);

        /// <summary>
        /// Derives key material.
        /// </summary>
        /// <param name="algorithm">The derivation algorithm to apply.</param>
        /// <param name="secret">The shared secret.</param>
        /// <param name="seed">
        /// The seed, which is the salt for the HKDF algorithms.
        /// </param>
        /// <param name="output">
        /// The buffer to fill completely with derived material. Its length is the
        /// number of bytes to derive.
        /// </param>
        void DeriveKey(
            KeyDerivationAlgorithm algorithm,
            ReadOnlySpan<byte> secret,
            ReadOnlySpan<byte> seed,
            Span<byte> output);
    }
}
