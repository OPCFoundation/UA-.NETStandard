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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Algorithm bundle for one PubSub security policy. Encapsulates
    /// the signing and encryption primitives and the key / nonce /
    /// signature sizes derived from the underlying cryptographic
    /// suite so callers never need to encode policy-specific
    /// constants directly.
    /// </summary>
    /// <remarks>
    /// Implements the algorithm-policy contract defined in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3">
    /// Part 14 §7.2.4.4.3 PubSub security headers</see>. The default
    /// AES-CTR implementation will be added in the Phase 7 security
    /// subsystem; Phase 1 only commits the contract.
    /// </remarks>
    public interface IPubSubSecurityPolicy
    {
        /// <summary>
        /// Policy URI handled by this bundle (matches one of the
        /// constants in <see cref="PubSubSecurityPolicyUri"/>).
        /// </summary>
        string PolicyUri { get; }

        /// <summary>
        /// Length, in bytes, of the signing key.
        /// </summary>
        int SigningKeyLength { get; }

        /// <summary>
        /// Length, in bytes, of the encrypting key.
        /// </summary>
        int EncryptingKeyLength { get; }

        /// <summary>
        /// Length, in bytes, of the per-message nonce required by
        /// the encryption primitive.
        /// </summary>
        int NonceLength { get; }

        /// <summary>
        /// Length, in bytes, of the signature appended to a secured
        /// NetworkMessage.
        /// </summary>
        int SignatureLength { get; }

        /// <summary>
        /// Computes a signature over <paramref name="data"/>.
        /// </summary>
        /// <param name="data">Message bytes to sign.</param>
        /// <param name="signingKey">Signing key.</param>
        /// <param name="signature">
        /// Destination span; must be at least
        /// <see cref="SignatureLength"/> bytes long.
        /// </param>
        void Sign(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signingKey,
            Span<byte> signature);

        /// <summary>
        /// Verifies a signature computed by <see cref="Sign"/>.
        /// </summary>
        /// <param name="data">Original message bytes.</param>
        /// <param name="signature">Signature bytes.</param>
        /// <param name="signingKey">Signing key.</param>
        /// <returns>
        /// <see langword="true"/> when the signature is valid;
        /// otherwise <see langword="false"/>.
        /// </returns>
        bool Verify(
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature,
            ReadOnlySpan<byte> signingKey);

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> with the supplied
        /// key and nonce.
        /// </summary>
        /// <param name="plaintext">Plain bytes.</param>
        /// <param name="encryptingKey">Encrypting key.</param>
        /// <param name="nonce">Per-message nonce.</param>
        /// <param name="ciphertext">
        /// Destination buffer; must be at least
        /// <c>plaintext.Length</c> bytes long (CTR mode preserves
        /// the message length).
        /// </param>
        void Encrypt(
            ReadOnlySpan<byte> plaintext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> ciphertext);

        /// <summary>
        /// Decrypts <paramref name="ciphertext"/> with the supplied
        /// key and nonce.
        /// </summary>
        /// <param name="ciphertext">Cipher bytes.</param>
        /// <param name="encryptingKey">Encrypting key.</param>
        /// <param name="nonce">Per-message nonce.</param>
        /// <param name="plaintext">
        /// Destination buffer; must be at least
        /// <c>ciphertext.Length</c> bytes long.
        /// </param>
        void Decrypt(
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> encryptingKey,
            ReadOnlySpan<byte> nonce,
            Span<byte> plaintext);
    }
}
