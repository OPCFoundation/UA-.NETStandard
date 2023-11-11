/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
#if !NETSTANDARD2_1 && !NET472_OR_GREATER && !NET5_0_OR_GREATER

using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using System.IO;

namespace Opc.Ua.Security.Certificates.BouncyCastle
{
    /// <summary>
    /// The signature factory for Bouncy Castle to sign a digest with a KeyVault key.
    /// </summary>
    public class X509SignatureFactory : ISignatureFactory
    {
        private readonly AlgorithmIdentifier m_algID;
        private readonly HashAlgorithmName m_hashAlgorithm;
        private readonly X509SignatureGenerator m_generator;

        /// <summary>
        /// Constructor which also specifies a source of randomness to be used if one is required.
        /// </summary>
        /// <param name="hashAlgorithm">The name of the signature algorithm to use.</param>
        /// <param name="generator">The signature generator.</param>
        public X509SignatureFactory(HashAlgorithmName hashAlgorithm, X509SignatureGenerator generator)
        {
            Org.BouncyCastle.Asn1.DerObjectIdentifier sigOid;
            if (hashAlgorithm == HashAlgorithmName.SHA1)
            {
                sigOid = Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Sha1WithRsaEncryption;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                sigOid = Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Sha256WithRsaEncryption;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                sigOid = Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Sha384WithRsaEncryption;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                sigOid = Org.BouncyCastle.Asn1.Pkcs.PkcsObjectIdentifiers.Sha512WithRsaEncryption;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(hashAlgorithm));
            }
            m_hashAlgorithm = hashAlgorithm;
            m_generator = generator;
            m_algID = new AlgorithmIdentifier(sigOid);
        }

        /// <inheritdoc/>
        public Object AlgorithmDetails => m_algID;

        /// <inheritdoc/>
        public IStreamCalculator<IBlockResult> CreateCalculator()
        {
            return new X509StreamCalculator(m_generator, m_hashAlgorithm);
        }

        /// <summary>
        /// Signs a Bouncy Castle digest stream with the .Net X509SignatureGenerator.
        /// </summary>
        class X509StreamCalculator : IStreamCalculator<IBlockResult>
        {
            private X509SignatureGenerator _generator;
            private readonly HashAlgorithmName _hashAlgorithm;

            /// <summary>
            /// Ctor for the stream calculator. 
            /// </summary>
            /// <param name="generator">The X509SignatureGenerator to sign the digest.</param>
            /// <param name="hashAlgorithm">The hash algorithm to use for the signature.</param>
            public X509StreamCalculator(
                X509SignatureGenerator generator,
                HashAlgorithmName hashAlgorithm)
            {
                Stream = new MemoryStream();
                _generator = generator;
                _hashAlgorithm = hashAlgorithm;
            }

            /// <summary>
            /// The digest stream (MemoryStream).
            /// </summary>
            public Stream Stream { get; }

            /// <summary>
            /// Callback signs the digest with X509SignatureGenerator.
            /// </summary>
            public IBlockResult GetResult()
            {
                if (!(Stream is MemoryStream memStream)) throw new ArgumentNullException(nameof(Stream));
                byte[] digest = memStream.ToArray();
                byte[] signature = _generator.SignData(digest, _hashAlgorithm);
                return new Org.BouncyCastle.Crypto.SimpleBlockResult(signature);
            }
        }
    }
}
#endif
