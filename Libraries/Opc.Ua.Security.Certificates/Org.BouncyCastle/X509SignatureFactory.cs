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
        private readonly AlgorithmIdentifier _algID;
        private readonly HashAlgorithmName _hashAlgorithm;
        private readonly X509SignatureGenerator _generator;

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
            _hashAlgorithm = hashAlgorithm;
            _generator = generator;
            _algID = new AlgorithmIdentifier(sigOid);
        }

        /// <inheritdoc/>
        public Object AlgorithmDetails => _algID;

        /// <inheritdoc/>
        public IStreamCalculator CreateCalculator()
        {
            return new X509StreamCalculator(_generator, _hashAlgorithm);
        }

        /// <summary>
        /// Signs a Bouncy Castle digest stream with the .Net X509SignatureGenerator.
        /// </summary>
        class X509StreamCalculator : IStreamCalculator
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
            public object GetResult()
            {
                var memStream = Stream as MemoryStream;
                if (memStream == null) throw new ArgumentNullException(nameof(Stream));
                var digest = memStream.ToArray();
                var signature = _generator.SignData(digest, _hashAlgorithm);
                return new MemoryBlockResult(signature);
            }
        }

        /// <summary>
        /// Helper for Bouncy Castle signing operation to store the result in a memory block.
        /// </summary>
        class MemoryBlockResult : IBlockResult
        {
            private readonly byte[] _data;
            /// <inheritdoc/>
            public MemoryBlockResult(byte[] data)
            {
                _data = data;
            }
            /// <inheritdoc/>
            public byte[] Collect()
            {
                return _data;
            }
            /// <inheritdoc/>
            public int Collect(byte[] destination, int offset)
            {
                throw new NotImplementedException();
            }
        }
    }
}
#endif
