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

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Forwards every operation to a shared <see cref="RSA"/> instance whose
    /// lifetime is owned by something else, and ignores disposal.
    /// </summary>
    /// <remarks>
    /// Callers of <see cref="Certificate.GetRSAPrivateKey"/> own the returned
    /// object and normally dispose it, because the platform hands out a fresh
    /// handle on every call. A detached private key is instead a single shared
    /// handle owned by the <see cref="Certificate"/> — frequently a key that
    /// lives in a TPM, an HSM or a remote key service. Handing that shared
    /// handle out directly would let the first caller destroy it for everyone,
    /// so each call returns one of these non owning views instead.
    /// </remarks>
    internal sealed class NonOwningRsa : RSA
    {
        /// <summary>
        /// Initializes a new non owning view over a shared key.
        /// </summary>
        /// <param name="key">The shared key. Must not be <c>null</c>.</param>
        public NonOwningRsa(RSA key)
        {
            m_key = key ?? throw new ArgumentNullException(nameof(key));
            KeySizeValue = key.KeySize;
            LegalKeySizesValue = key.LegalKeySizes;
        }

        /// <inheritdoc/>
        public override string? KeyExchangeAlgorithm => m_key.KeyExchangeAlgorithm;

        /// <inheritdoc/>
        public override string SignatureAlgorithm => m_key.SignatureAlgorithm;

        /// <inheritdoc/>
        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            return m_key.ExportParameters(includePrivateParameters);
        }

        /// <inheritdoc/>
        public override void ImportParameters(RSAParameters parameters)
        {
            m_key.ImportParameters(parameters);
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
            return DetachedKeyHash.Compute(data, offset, count, hashAlgorithm);
        }

        /// <inheritdoc/>
        protected override byte[] HashData(Stream data, HashAlgorithmName hashAlgorithm)
        {
            return DetachedKeyHash.Compute(data, hashAlgorithm);
        }

        /// <summary>
        /// Deliberately does not dispose the shared key.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private readonly RSA m_key;
    }
}
