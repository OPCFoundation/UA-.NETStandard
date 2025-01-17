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

#if !NETSTANDARD2_1 && !NET5_0_OR_GREATER
using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Parameters;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Methods or read PEM data.
    /// </summary>
    public static class PEMReader
    {
        #region Public Methods
        /// <summary>
        /// Import an RSA private key from PEM.
        /// </summary>
        public static RSA ImportRsaPrivateKeyFromPEM(
            byte[] pemDataBlob,
            string password = null)
        {
            AsymmetricAlgorithm key = ImportPrivateKey(pemDataBlob, password);
            if (key is RSA rsaKey)
            {
                return rsaKey;
            }
            else
            {
                throw new CryptographicException("PEM data does not contain a valid RSA private key");
            }
        }

        /// <summary>
        /// Import an ECDSa private key from PEM.
        /// </summary>
        public static ECDsa ImportECDsaPrivateKeyFromPEM(
            byte[] pemDataBlob,
            string password = null)
        {
            AsymmetricAlgorithm key = ImportPrivateKey(pemDataBlob, password);
            if (key is ECDsa ecKey)
            {
                return ecKey;
            }
            else
            {
                throw new CryptographicException("PEM data does not contain a valid RSA private key");
            }
        }


        #endregion

        #region Private
        /// <summary>
        /// Import a private key from PEM.
        /// </summary>
        private static AsymmetricAlgorithm ImportPrivateKey(
            byte[] pemDataBlob,
            string password = null)
        {
            
            Org.BouncyCastle.OpenSsl.PemReader pemReader;
            using (var pemStreamReader = new StreamReader(new MemoryStream(pemDataBlob), Encoding.UTF8, true))
            {
                if (String.IsNullOrEmpty(password))
                {
                    pemReader = new Org.BouncyCastle.OpenSsl.PemReader(pemStreamReader);
                }
                else
                {
                    var pwFinder = new Password(password.ToCharArray());
                    pemReader = new Org.BouncyCastle.OpenSsl.PemReader(pemStreamReader, pwFinder);
                }

                AsymmetricAlgorithm key = null;
                try
                {
                    // find the private key in the PEM blob
                    object pemObject = pemReader.ReadObject();
                    while (pemObject != null)
                    {
                        if (pemObject is Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair keypair)
                        {
                            pemObject = keypair.Private;
                        }

                        // Check for an RSA private key
                        if (pemObject is RsaPrivateCrtKeyParameters rsaParams)
                        {
                            var rsa = RSA.Create();
                            rsa.ImportParameters(DotNetUtilities.ToRSAParameters(rsaParams));
                            key = rsa;
                            break;
                        }
                        // Check for an EC private key
                        if (pemObject is ECPrivateKeyParameters ecParams)
                        {
                            var ecdsa = CreateECDsaFromECPrivateKey(ecParams);
                            key = ecdsa;
                            break;
                        }
                       
                        // read next object
                        pemObject = pemReader.ReadObject();
                    }
                }
                finally
                {
                    pemReader.Reader.Dispose();
                }
                if (key == null)
                {
                    throw new CryptographicException("PEM data blob does not contain a private key.");
                }
                return key;
            }
        }

        private static ECDsa CreateECDsaFromECPrivateKey(ECPrivateKeyParameters eCPrivateKeyParameters)
        {
            var domainParams = eCPrivateKeyParameters.Parameters;

            // calculate keySize round up (bitLength + 7) / 8
            int keySizeBytes = (domainParams.N.BitLength + 7) / 8;

            var curveOid = eCPrivateKeyParameters.PublicKeyParamSet.Id;
            var curve = ECCurve.CreateFromOid(new Oid(curveOid));

            var q = domainParams.G.Multiply(eCPrivateKeyParameters.D).Normalize();
            var x = q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned();
            var y = q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned();
            var d = eCPrivateKeyParameters.D.ToByteArrayUnsigned();

            // pad all to the same length since ToByteArrayUnsigned might drop leading zeroes
            x = PadWithLeadingZeros(x, keySizeBytes);
            y = PadWithLeadingZeros(y, keySizeBytes);
            d = PadWithLeadingZeros(d, keySizeBytes);


            var ecParams = new ECParameters {
                Curve = curve,
                Q =
                {
                    X = x,
                    Y = y
                },
                D = d
            };

            var ecdsa = ECDsa.Create();
            ecdsa.ImportParameters(ecParams);

            return ecdsa;
        }

        /// <summary>
        /// Pads a byte array with leading zeros to reach the specifieed size
        /// If the input is allready the given size, it just returns it
        /// </summary>
        /// <param name="arrayToPad">Provided array to pad</param>
        /// <param name="desiredSize">The desired total length of byte array after padding</param>
        /// <returns></returns>
        private static byte[] PadWithLeadingZeros(byte[] arrayToPad,  int desiredSize)
        {
            if (arrayToPad.Length == desiredSize)
            {
                return arrayToPad;
            }

            int paddingLength = desiredSize - arrayToPad.Length;
            if (paddingLength < 0)
            {
                throw new ArgumentException($"Input byte array is larger than the desired size {desiredSize} bytes.");
            }

            var paddedArray = new byte[desiredSize];

            // Right-align the arrayToPad into paddedArray
            Buffer.BlockCopy(arrayToPad, 0, paddedArray, paddingLength, arrayToPad.Length);

            return paddedArray;

        }
        #endregion

        #region Internal class
        /// <summary>
        /// Wrapper for a password string.
        /// </summary>
        internal class Password
            : IPasswordFinder
        {
            private readonly char[] m_password;

            public Password(
                char[] word)
            {
                this.m_password = (char[])word.Clone();
            }

            public char[] GetPassword()
            {
                return (char[])m_password.Clone();
            }
        }
        #endregion
    }
}
#endif
