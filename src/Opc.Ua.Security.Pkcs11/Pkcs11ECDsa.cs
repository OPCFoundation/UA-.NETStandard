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
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// An <see cref="ECDsa"/> whose private key lives on a PKCS#11 token and is
    /// never present in this process.
    /// </summary>
    /// <remarks>
    /// <c>CKM_ECDSA</c> takes a bare hash and returns the signature as the fixed
    /// width concatenation of r and s, which is the same layout
    /// <see cref="ECDsa.SignHash(byte[])"/> is defined to return. No conversion
    /// is needed in either direction.
    /// </remarks>
    public sealed class Pkcs11ECDsa : ECDsa
    {
        /// <summary>
        /// Initializes a new instance bound to a key on a token.
        /// </summary>
        /// <param name="token">The token holding the key.</param>
        /// <param name="privateKey">The private key object on the token.</param>
        /// <param name="publicParameters">
        /// The public key, normally taken from the matching certificate.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="token"/> or <paramref name="privateKey"/> is <c>null</c>.
        /// </exception>
        internal Pkcs11ECDsa(
            Pkcs11Token token,
            IObjectHandle privateKey,
            ECParameters publicParameters)
        {
            m_token = token ?? throw new ArgumentNullException(nameof(token));
            m_privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));

            m_publicKey = Create();
            m_publicKey.ImportParameters(publicParameters);

            KeySizeValue = m_publicKey.KeySize;
            LegalKeySizesValue = [new KeySizes(256, 521, 0)];
        }

        /// <inheritdoc/>
        public override string? KeyExchangeAlgorithm => m_publicKey.KeyExchangeAlgorithm;

        /// <inheritdoc/>
        public override string SignatureAlgorithm => "ECDsa";

        /// <inheritdoc/>
        /// <exception cref="CryptographicException">
        /// Thrown when <paramref name="includePrivateParameters"/> is <c>true</c>.
        /// The private key is held on the token and cannot be extracted.
        /// </exception>
        public override ECParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new CryptographicException(
                    "The private key is held on a PKCS#11 token and cannot be exported.");
            }

            return m_publicKey.ExportParameters(false);
        }

        /// <inheritdoc/>
        /// <exception cref="CryptographicException">
        /// Thrown when <paramref name="includePrivateParameters"/> is <c>true</c>.
        /// </exception>
        public override ECParameters ExportExplicitParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new CryptographicException(
                    "The private key is held on a PKCS#11 token and cannot be exported.");
            }

            return m_publicKey.ExportExplicitParameters(false);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// Always thrown. Key material cannot be injected this way.
        /// </exception>
        public override void ImportParameters(ECParameters parameters)
        {
            throw new NotSupportedException(
                "Key material cannot be imported into a PKCS#11 backed key.");
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// Always thrown. Keys are generated on the token, not here.
        /// </exception>
        public override void GenerateKey(ECCurve curve)
        {
            throw new NotSupportedException(
                "A PKCS#11 backed key is generated on the token, not in this process.");
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="hash"/> is <c>null</c>.
        /// </exception>
        public override byte[] SignHash(byte[] hash)
        {
            if (hash == null)
            {
                throw new ArgumentNullException(nameof(hash));
            }

            using IMechanism mechanism = m_token.CreateMechanism(CKM.CKM_ECDSA);
            return m_token.Sign(mechanism, m_privateKey, hash);
        }

        /// <inheritdoc/>
        public override bool VerifyHash(byte[] hash, byte[] signature)
        {
            return m_publicKey.VerifyHash(hash, signature);
        }

        /// <inheritdoc/>
        protected override byte[] HashData(
            byte[] data,
            int offset,
            int count,
            HashAlgorithmName hashAlgorithm)
        {
            return Pkcs11Digest.Compute(data, offset, count, hashAlgorithm);
        }

        /// <inheritdoc/>
        protected override byte[] HashData(Stream data, HashAlgorithmName hashAlgorithm)
        {
            return Pkcs11Digest.Compute(data, hashAlgorithm);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_publicKey.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly Pkcs11Token m_token;
        private readonly IObjectHandle m_privateKey;
        private readonly ECDsa m_publicKey;
    }
}
