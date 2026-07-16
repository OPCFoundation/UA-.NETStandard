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

namespace Opc.Ua.PubSub.Security.Policies
{
    /// <summary>
    /// Pass-through implementation of <see cref="IPubSubSecurityPolicy"/>
    /// representing the absence of message-level security. Used when a
    /// SecurityGroup is configured with <c>SecurityMode = None</c> or
    /// when running interop tests against unsecured publishers.
    /// </summary>
    /// <remarks>
    /// Implements the <c>None</c> entry of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.1">
    /// Part 14 §7.2.4.4.3.1 PubSub security policies</see>. All key,
    /// nonce and signature lengths are zero — the wrapper layer must
    /// not allocate a SecurityHeader when this policy is selected.
    /// </remarks>
    public sealed class PubSubNonePolicy : IPubSubSecurityPolicy
    {
        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static readonly PubSubNonePolicy Instance = new();

        private PubSubNonePolicy()
        {
        }

        /// <inheritdoc/>
        public string PolicyUri => PubSubSecurityPolicyUri.None;

        /// <inheritdoc/>
        public int SigningKeyLength => 0;

        /// <inheritdoc/>
        public int EncryptingKeyLength => 0;

        /// <inheritdoc/>
        public int NonceLength => 0;

        /// <inheritdoc/>
        public int SignatureLength => 0;

        /// <inheritdoc/>
        public void Sign(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signingKey,
            Span<byte> signature)
        {
            if (signature.Length != 0)
            {
                throw new ArgumentException(
                    "None policy does not produce a signature; pass an empty span.",
                    nameof(signature));
            }
        }

        /// <inheritdoc/>
        public bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> signingKey)
        {
            return signature.Length == 0;
        }

        /// <inheritdoc/>
        public void Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> ciphertext)
        {
            if (ciphertext.Length < plaintext.Length)
            {
                throw new ArgumentException(
                    "Ciphertext buffer is shorter than plaintext.",
                    nameof(ciphertext));
            }
            plaintext.CopyTo(ciphertext);
        }

        /// <inheritdoc/>
        public void Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> plaintext)
        {
            if (plaintext.Length < ciphertext.Length)
            {
                throw new ArgumentException(
                    "Plaintext buffer is shorter than ciphertext.",
                    nameof(plaintext));
            }
            ciphertext.CopyTo(plaintext);
        }
    }
}
