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
using Net.Pkcs11Interop.HighLevelAPI.MechanismParams;

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// An <see cref="RSA"/> whose private key lives on a PKCS#11 token and is
    /// never present in this process.
    /// </summary>
    /// <remarks>
    /// The stack asks a certificate for an <see cref="RSA"/> and then signs or
    /// decrypts with it. Presenting the token as an <see cref="RSA"/> is what
    /// lets every existing consumer work unchanged.
    /// <para>
    /// The public key is taken from the certificate rather than read back from
    /// the token, so verification and encryption are done locally and only the
    /// two operations that genuinely need the private key cross into hardware.
    /// </para>
    /// <para>
    /// Every private key export member fails, exactly as a real token requires.
    /// The base class routes <c>ExportRSAPrivateKey</c>, <c>ExportPkcs8PrivateKey</c>
    /// and the encrypted variants through <see cref="ExportParameters"/>, so
    /// rejecting there covers all of them.
    /// </para>
    /// </remarks>
    public sealed class Pkcs11Rsa : RSA
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
        internal Pkcs11Rsa(
            Pkcs11Token token,
            IObjectHandle privateKey,
            RSAParameters publicParameters)
        {
            m_token = (token ?? throw new ArgumentNullException(nameof(token))).AddRef();
            m_privateKey = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
            m_publicParameters = publicParameters;

            m_publicKey = Create();
            m_publicKey.ImportParameters(publicParameters);

            KeySizeValue = m_publicKey.KeySize;
            LegalKeySizesValue = [new KeySizes(1024, 8192, 8)];
        }

        /// <inheritdoc/>
        public override string SignatureAlgorithm => "RSA";

        /// <inheritdoc/>
        public override string KeyExchangeAlgorithm => "RSA";

        /// <inheritdoc/>
        /// <exception cref="CryptographicException">
        /// Thrown when <paramref name="includePrivateParameters"/> is <c>true</c>.
        /// The private key is held on the token and cannot be extracted.
        /// </exception>
        public override RSAParameters ExportParameters(bool includePrivateParameters)
        {
            if (includePrivateParameters)
            {
                throw new CryptographicException(
                    "The private key is held on a PKCS#11 token and cannot be exported.");
            }

            return m_publicParameters;
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// Always thrown. Key material cannot be injected this way.
        /// </exception>
        public override void ImportParameters(RSAParameters parameters)
        {
            throw new NotSupportedException(
                "Key material cannot be imported into a PKCS#11 backed key.");
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="hash"/> or <paramref name="padding"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// The padding or hash algorithm is not supported.
        /// </exception>
        public override byte[] SignHash(
            byte[] hash,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding)
        {
            if (hash == null)
            {
                throw new ArgumentNullException(nameof(hash));
            }

            if (padding == null)
            {
                throw new ArgumentNullException(nameof(padding));
            }

            if (padding == RSASignaturePadding.Pkcs1)
            {
                // CKM_RSA_PKCS applies the PKCS#1 v1.5 block padding but does not
                // build the DigestInfo, so the caller has to supply it.
                byte[] digestInfo = Pkcs11Digest.WrapInDigestInfo(hash, hashAlgorithm);

                using IMechanism mechanism = m_token.CreateMechanism(CKM.CKM_RSA_PKCS);
                return m_token.Sign(mechanism, m_privateKey, digestInfo);
            }

            if (padding.Mode == RSASignaturePaddingMode.Pss)
            {
                CKM hashMechanism = Pkcs11Digest.ToMechanism(hashAlgorithm);
                CKG maskGenerationFunction = Pkcs11Digest.ToMaskGenerationFunction(hashAlgorithm);

                using IMechanismParams parameters = m_token.CreatePssParams(
                    hashMechanism,
                    maskGenerationFunction,
                    (ulong)hash.Length);

                using IMechanism mechanism = m_token.CreateMechanism(
                    CKM.CKM_RSA_PKCS_PSS,
                    parameters);

                return m_token.Sign(mechanism, m_privateKey, hash);
            }

            throw new CryptographicException(
                $"RSA signature padding '{padding}' is not supported by the PKCS#11 provider.");
        }

        /// <inheritdoc/>
        public override bool VerifyHash(
            byte[] hash,
            byte[] signature,
            HashAlgorithmName hashAlgorithm,
            RSASignaturePadding padding)
        {
            return m_publicKey.VerifyHash(hash, signature, hashAlgorithm, padding);
        }

        /// <inheritdoc/>
        public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding)
        {
            return m_publicKey.Encrypt(data, padding);
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="data"/> or <paramref name="padding"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="CryptographicException">
        /// The padding is not supported.
        /// </exception>
        public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (padding == null)
            {
                throw new ArgumentNullException(nameof(padding));
            }

            if (padding == RSAEncryptionPadding.Pkcs1)
            {
                using IMechanism mechanism = m_token.CreateMechanism(CKM.CKM_RSA_PKCS);
                return m_token.Decrypt(mechanism, m_privateKey, data);
            }

            if (padding.Mode == RSAEncryptionPaddingMode.Oaep)
            {
                CKM hashMechanism = Pkcs11Digest.ToMechanism(padding.OaepHashAlgorithm);
                CKG maskGenerationFunction = Pkcs11Digest.ToMaskGenerationFunction(
                    padding.OaepHashAlgorithm);

                using IMechanismParams parameters = m_token.CreateOaepParams(
                    hashMechanism,
                    maskGenerationFunction);

                using IMechanism mechanism = m_token.CreateMechanism(
                    CKM.CKM_RSA_PKCS_OAEP,
                    parameters);

                return m_token.Decrypt(mechanism, m_privateKey, data);
            }

            throw new CryptographicException(
                $"RSA encryption padding '{padding}' is not supported by the PKCS#11 provider.");
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

                // Releases this key's reference; the session closes with the last.
                m_token.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly Pkcs11Token m_token;
        private readonly IObjectHandle m_privateKey;
        private readonly RSAParameters m_publicParameters;
        private readonly RSA m_publicKey;
    }
}
