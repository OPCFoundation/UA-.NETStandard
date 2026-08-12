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

namespace Opc.Ua
{
    /// <summary>
    /// The default provider, which defers every operation to the cryptography
    /// the .NET platform supplies.
    /// </summary>
    /// <remarks>
    /// This provider claims every purpose without narrowing to a policy or
    /// certificate type, which reproduces today's behaviour exactly: whatever the
    /// platform supports is available, and nothing is filtered out. It reports
    /// <see cref="CryptoValidationLevel.FipsCapablePlatform"/> rather than
    /// claiming validation, because whether the underlying module is running in a
    /// validated mode is a property of how the machine is configured and not
    /// something this stack can assert.
    /// <para>
    /// It also carries the symmetric, key derivation and random facets, so that
    /// those seams ship with an implementation and so that a compliance policy
    /// sees a provider behind every purpose. Callers that resolve this instance
    /// are told to use their inline path instead, because the facets perform the
    /// very same operations and an interface call on the per message path would
    /// cost without buying anything.
    /// </para>
    /// </remarks>
    public sealed class PlatformCryptoProvider :
        ICryptoProvider,
        ISymmetricCryptoProvider,
        IKeyDerivationProvider,
        ISecureRandomSource
    {
        /// <summary>
        /// The shared instance.
        /// </summary>
        public static PlatformCryptoProvider Instance { get; } = new();

        /// <inheritdoc/>
        public string Name => "Platform";

        /// <inheritdoc/>
        public CryptoValidationStatus Validation => CryptoValidationStatus.Platform;

        /// <inheritdoc/>
        public ArrayOf<CryptoCapability> Capabilities => s_capabilities;

        /// <inheritdoc/>
        public bool Supports(SymmetricEncryptionAlgorithm algorithm)
        {
            return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
        }

        /// <inheritdoc/>
        public bool Supports(SymmetricSignatureAlgorithm algorithm)
        {
            return PlatformSymmetricCryptoProvider.Instance.Supports(algorithm);
        }

        /// <inheritdoc/>
        public bool Supports(KeyDerivationAlgorithm algorithm)
        {
            return PlatformKeyDerivationProvider.Instance.Supports(algorithm);
        }

        /// <inheritdoc/>
        public void Encrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext)
        {
            PlatformSymmetricCryptoProvider.Instance
                .Encrypt(algorithm, key, iv, plaintext, ciphertext);
        }

        /// <inheritdoc/>
        public void Decrypt(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> ciphertext,
            Span<byte> plaintext)
        {
            PlatformSymmetricCryptoProvider.Instance
                .Decrypt(algorithm, key, iv, ciphertext, plaintext);
        }

        /// <inheritdoc/>
        public void EncryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData)
        {
            PlatformSymmetricCryptoProvider.Instance.EncryptAuthenticated(
                algorithm, key, nonce, plaintext, ciphertext, tag, associatedData);
        }

        /// <inheritdoc/>
        public bool DecryptAuthenticated(
            SymmetricEncryptionAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData)
        {
            return PlatformSymmetricCryptoProvider.Instance.DecryptAuthenticated(
                algorithm, key, nonce, ciphertext, tag, plaintext, associatedData);
        }

        /// <inheritdoc/>
        public int GetSignatureLength(SymmetricSignatureAlgorithm algorithm)
        {
            return PlatformSymmetricCryptoProvider.Instance.GetSignatureLength(algorithm);
        }

        /// <inheritdoc/>
        public void Sign(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            Span<byte> signature)
        {
            PlatformSymmetricCryptoProvider.Instance.Sign(algorithm, key, data, signature);
        }

        /// <inheritdoc/>
        public bool Verify(
            SymmetricSignatureAlgorithm algorithm,
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> data,
            ReadOnlySpan<byte> signature)
        {
            return PlatformSymmetricCryptoProvider.Instance
                .Verify(algorithm, key, data, signature);
        }

        /// <inheritdoc/>
        public void DeriveKey(
            KeyDerivationAlgorithm algorithm,
            ReadOnlySpan<byte> secret,
            ReadOnlySpan<byte> seed,
            Span<byte> output)
        {
            PlatformKeyDerivationProvider.Instance.DeriveKey(algorithm, secret, seed, output);
        }

        /// <inheritdoc/>
        public void GetBytes(Span<byte> buffer)
        {
            PlatformRandomSource.Instance.GetBytes(buffer);
        }

        private PlatformCryptoProvider()
        {
        }

        private static readonly ArrayOf<CryptoCapability> s_capabilities = new(
            new CryptoCapability[]
            {
                new(CryptoPurpose.ApplicationInstanceKey),
                new(CryptoPurpose.UserIdentityKey),
                new(CryptoPurpose.KeyAgreement),
                new(CryptoPurpose.CertificateIssuance),
                new(CryptoPurpose.ChannelSymmetric),
                new(CryptoPurpose.KeyDerivation),
                new(CryptoPurpose.RandomNumberGeneration)
            });
    }
}
