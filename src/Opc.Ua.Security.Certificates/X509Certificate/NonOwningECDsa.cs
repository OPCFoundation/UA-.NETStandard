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
    /// Forwards every operation to a shared <see cref="ECDsa"/> instance whose
    /// lifetime is owned by something else, and ignores disposal.
    /// </summary>
    /// <remarks>
    /// See <see cref="NonOwningRsa"/> for the rationale.
    /// </remarks>
    internal sealed class NonOwningECDsa : ECDsa
    {
        /// <summary>
        /// Initializes a new non owning view over a shared key.
        /// </summary>
        /// <param name="key">The shared key. Must not be <c>null</c>.</param>
        public NonOwningECDsa(ECDsa key)
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
        public override ECParameters ExportParameters(bool includePrivateParameters)
        {
            return m_key.ExportParameters(includePrivateParameters);
        }

        /// <inheritdoc/>
        public override ECParameters ExportExplicitParameters(bool includePrivateParameters)
        {
            return m_key.ExportExplicitParameters(includePrivateParameters);
        }

        /// <inheritdoc/>
        public override void ImportParameters(ECParameters parameters)
        {
            m_key.ImportParameters(parameters);
        }

        /// <inheritdoc/>
        public override void GenerateKey(ECCurve curve)
        {
            m_key.GenerateKey(curve);
        }

        /// <inheritdoc/>
        public override byte[] SignHash(byte[] hash)
        {
            return m_key.SignHash(hash);
        }

        /// <inheritdoc/>
        public override bool VerifyHash(byte[] hash, byte[] signature)
        {
            return m_key.VerifyHash(hash, signature);
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

        private readonly ECDsa m_key;
    }
}
