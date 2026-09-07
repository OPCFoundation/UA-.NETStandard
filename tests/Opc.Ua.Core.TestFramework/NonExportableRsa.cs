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
using System.Security.Cryptography;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// An <see cref="RSA"/> implementation that performs every cryptographic
    /// operation correctly but refuses to surrender private key material,
    /// mirroring a key held in a TPM, an HSM, a PKCS#11 token or a remote
    /// key management service.
    /// </summary>
    /// <remarks>
    /// Hardware-backed keys are created with an export policy that makes the
    /// private key permanently non-extractable (for example a CNG key created
    /// with <c>CngExportPolicies.None</c>). Any attempt to export the private
    /// parameters fails with a <see cref="CryptographicException"/>. This type
    /// reproduces that behaviour on every platform and target framework so the
    /// stack can be tested against it without requiring real hardware.
    /// <para>
    /// Only <see cref="ExportParameters"/> needs to reject the request: the
    /// base class funnels <c>ExportRSAPrivateKey</c>, <c>ExportPkcs8PrivateKey</c>
    /// and their encrypted variants through it, so they fail for the same reason
    /// a real hardware key would.
    /// </para>
    /// </remarks>
    public sealed class NonExportableRsa : RSA
    {
        /// <summary>
        /// Initializes a new instance wrapping a freshly generated software key.
        /// </summary>
        /// <param name="keySizeInBits">
        /// The size of the generated key in bits.
        /// </param>
        public NonExportableRsa(int keySizeInBits = 2048)
            : this(Create(keySizeInBits), ownsKey: true)
        {
        }

        /// <summary>
        /// Initializes a new instance that delegates operations to an existing key.
        /// </summary>
        /// <param name="key">
        /// The key that performs the actual cryptographic operations.
        /// </param>
        /// <param name="ownsKey">
        /// <c>true</c> if disposing this instance should also dispose
        /// <paramref name="key"/>; otherwise <c>false</c>.
        /// </param>
        public NonExportableRsa(RSA key, bool ownsKey = false)
        {
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            m_ownsKey = ownsKey;
            KeySizeValue = key.KeySize;
            LegalKeySizesValue = [new KeySizes(2048, 4096, 1024)];
        }

        /// <summary>
        /// Gets the number of times an export of the private key was attempted.
        /// </summary>
        /// <remarks>
        /// Tests use this to assert that a code path never reaches for private
        /// key material, rather than merely tolerating the failure.
        /// </remarks>
        public int PrivateKeyExportAttempts => m_privateKeyExportAttempts;

        /// <inheritdoc/>
        public override string KeyExchangeAlgorithm => m_key.KeyExchangeAlgorithm;

        /// <inheritdoc/>
        public override string SignatureAlgorithm => m_key.SignatureAlgorithm;

        /// <inheritdoc/>
        /// <exception cref="CryptographicException">
        /// Thrown when <paramref name="includePrivateParameters"/> is <c>true</c>,
        /// because the private key is not extractable.
        /// </exception>
        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                m_privateKeyExportAttempts++;
                throw new CryptographicException(
                    "The private key is not exportable because it is held in hardware.");
            }

            return m_key.ExportParameters(false);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// Always thrown; key material cannot be injected into hardware this way.
        /// </exception>
        public override void ImportParameters(RSAParameters parameters)
        {
            throw new NotSupportedException(
                "Key material cannot be imported into a hardware protected key.");
        }

        /// <inheritdoc/>
        public override byte[] SignHash(
            byte[] hash,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding)
        {
            return m_key.SignHash(hash, hashAlgorithm, padding);
        }

        /// <inheritdoc/>
        public override bool VerifyHash(
            byte[] hash,
            byte[] signature,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding)
        {
            return m_key.VerifyHash(hash, signature, hashAlgorithm, padding);
        }

        /// <inheritdoc/>
        public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding)
        {
            return m_key.Encrypt(data, padding);
        }

        /// <inheritdoc/>
        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
        {
            return m_key.Decrypt(data, padding);
        }

        /// <inheritdoc/>
        protected override byte[] HashData(
            byte[] data,
            int offset,
            int count,
            HashAlgorithmName hashAlgorithm)
        {
            return NonExportableKeyHash.Compute(data, offset, count, hashAlgorithm);
        }

        /// <inheritdoc/>
        protected override byte[] HashData(
            System.IO.Stream data,
            HashAlgorithmName hashAlgorithm)
        {
            return NonExportableKeyHash.Compute(data, hashAlgorithm);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing && m_ownsKey)
            {
                m_key.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly RSA m_key;
        private readonly bool m_ownsKey;
        private int m_privateKeyExportAttempts;
    }
}
