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
    /// Checks that a provider actually produced the key material or randomness
    /// it was asked for.
    /// </summary>
    /// <remarks>
    /// <see cref="IKeyDerivationProvider.DeriveKey"/> and
    /// <see cref="ISecureRandomSource.GetBytes"/> return <see langword="void"/>,
    /// so a provider that no-ops, fills part of the buffer, or swallows an
    /// internal failure is indistinguishable from one that succeeded. The buffer
    /// it was handed then becomes channel signing keys, encryption keys,
    /// initialization vectors and nonces.
    /// <para>
    /// The platform paths these replace cannot fail this way: <c>Utils.PSHA</c>
    /// returns the array it built and
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/> throws.
    /// Only the provider path is exposed, and because both ends of a channel
    /// usually run the same image, both would derive the same dead keys and the
    /// handshake would complete - traffic flowing with no confidentiality and
    /// forgeable integrity, invisible to the operator and to the peer.
    /// </para>
    /// <para>
    /// So the buffer is stamped before the call and checked after it. This
    /// cannot prove the output is good, which no caller-side check can; it fails
    /// closed on output that is provably unusable.
    /// </para>
    /// </remarks>
    internal static class CryptoProviderOutput
    {
        /// <summary>
        /// Stamps a buffer so that a provider leaving it untouched is detectable.
        /// </summary>
        /// <param name="output">The buffer handed to the provider.</param>
        public static void Stamp(Span<byte> output)
        {
            output.Fill(kStamp);
        }

        /// <summary>
        /// Rejects output a provider cannot have produced.
        /// </summary>
        /// <param name="output">The buffer the provider filled.</param>
        /// <param name="operation">What was being produced, for the error.</param>
        /// <param name="provider">The facet that produced it.</param>
        /// <exception cref="ServiceResultException">
        /// The provider left the buffer stamped or zeroed.
        /// </exception>
        public static void Verify(
            ReadOnlySpan<byte> output,
            string operation,
            object provider)
        {
            if (output.IsEmpty)
            {
                return;
            }

            bool allStamp = true;
            bool allZero = true;

            for (int ii = 0; ii < output.Length; ii++)
            {
                byte value = output[ii];

                if (value != kStamp)
                {
                    allStamp = false;
                }

                if (value != 0)
                {
                    allZero = false;
                }

                if (!allStamp && !allZero)
                {
                    return;
                }
            }

            // Real key material is neither of these. A whole buffer of the stamp
            // means the provider never wrote, and a whole buffer of zero means it
            // cleared without filling - both yield keys an attacker can derive.
            throw ServiceResultException.Create(
                StatusCodes.BadSecurityChecksFailed,
                "The crypto provider '{0}' returned no usable output for {1}. " +
                "The buffer was left {2}, so the key material would be predictable.",
                (provider as ICryptoProvider)?.Name ?? provider?.GetType().Name ?? "unknown",
                operation,
                allStamp ? "untouched" : "zeroed");
        }

        private const byte kStamp = 0xA5;
    }
}
