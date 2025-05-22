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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
#if NET472_OR_GREATER
using Opc.Ua.Security.Certificates.BouncyCastle;
#endif
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Methods or read PEM data.
    /// </summary>
    public static class PEMReader
    {
        /// <summary>
        /// Checks if the PEM data contains a private key.
        /// </summary>
        /// <param name="pemDataBlob">The PEM data as a byte span.</param>
        /// <returns>True if a private key is found.</returns>
        public static bool ContainsPrivateKey(byte[] pemDataBlob)
        {
            using var ms = new MemoryStream(pemDataBlob);
            using var reader = new StreamReader(ms, Encoding.UTF8, true);
            var pemReader = new PemReader(reader);
            try
            {
                object pemObject = pemReader.ReadObject();
                while (pemObject != null)
                {
                    // Check for AsymmetricCipherKeyPair (private key)
                    if (pemObject is Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair)
                    {
                        return true;
                    }
                    // Check for direct private key parameters
                    if (pemObject is RsaPrivateCrtKeyParameters)
                    {
                        return true;
                    }
#if NET472_OR_GREATER

                    if (pemObject is ECPrivateKeyParameters)
                    {
                        return true;
                    }
#endif
                    pemObject = pemReader.ReadObject();
                }
            }
            finally
            {
                pemReader.Reader.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Import multiple X509 certificates from PEM data.
        /// Supports a maximum of 99 certificates in the PEM data.
        /// </summary>
        /// <param name="pemDataBlob">The PEM datablob as byte array.</param>
        /// <returns>The certificates.</returns>
        public static X509Certificate2Collection ImportPublicKeysFromPEM(byte[] pemDataBlob)
        {
            var certificates = new X509Certificate2Collection();
            using (var ms = new MemoryStream(pemDataBlob))
            using (var reader = new StreamReader(ms, Encoding.UTF8, true))
            {
                var pemReader = new PemReader(reader);
                int certCount = 0;
                try
                {
                    object pemObject = pemReader.ReadObject();
                    while (pemObject != null && certCount < 99)
                    {
                        if (pemObject is Org.BouncyCastle.X509.X509Certificate bcCert)
                        {
                            byte[] rawData = bcCert.GetEncoded();
                            var cert = new X509Certificate2(rawData);
                            certificates.Add(cert);
                            certCount++;
                        }
                        pemObject = pemReader.ReadObject();
                    }
                }
                finally
                {
                    pemReader.Reader.Dispose();
                }
            }
            return certificates;
        }

        /// <summary>
        /// Import an RSA private key from PEM.
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        public static RSA ImportRsaPrivateKeyFromPEM(byte[] pemDataBlob, ReadOnlySpan<char> password)
        {
            AsymmetricAlgorithm key = ImportPrivateKey(pemDataBlob, password);
            if (key is RSA rsaKey)
            {
                return rsaKey;
            }

            throw new CryptographicException("PEM data does not contain a valid RSA private key");
        }

        /// <summary>
        /// Import an ECDSa private key from PEM.
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        public static ECDsa ImportECDsaPrivateKeyFromPEM(byte[] pemDataBlob, ReadOnlySpan<char> password)
        {
            AsymmetricAlgorithm key = ImportPrivateKey(pemDataBlob, password);
            if (key is ECDsa ecKey)
            {
                return ecKey;
            }

            throw new CryptographicException("PEM data does not contain a valid RSA private key");
        }

        /// <summary>
        /// Import a private key from PEM.
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
        private static AsymmetricAlgorithm ImportPrivateKey(
            byte[] pemDataBlob,
            ReadOnlySpan<char> password)
        {
            PemReader pemReader;
            using var pemStreamReader = new StreamReader(
                new MemoryStream(pemDataBlob),
                Encoding.UTF8,
                true);
            if (password.IsEmpty)
            {
                pemReader = new PemReader(pemStreamReader);
            }
            else
            {
                var pwFinder = new Password(password.ToArray());
                pemReader = new PemReader(pemStreamReader, pwFinder);
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
#if NET472_OR_GREATER
                    // Check for an EC private key
                    if (pemObject is ECPrivateKeyParameters ecParams)
                    {
                        key = CreateECDsaFromECPrivateKey(ecParams);
                        break;
                    }
#endif

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

#if NET472_OR_GREATER
        private static ECDsa CreateECDsaFromECPrivateKey(
            ECPrivateKeyParameters eCPrivateKeyParameters)
        {
            ECDomainParameters domainParams = eCPrivateKeyParameters.Parameters;

            // calculate keySize round up (bitLength + 7) / 8
            int keySizeBytes = (domainParams.N.BitLength + 7) / 8;

            string curveOid = eCPrivateKeyParameters.PublicKeyParamSet.Id;
            var curve = ECCurve.CreateFromOid(new Oid(curveOid));

            Org.BouncyCastle.Math.EC.ECPoint q = domainParams.G.Multiply(eCPrivateKeyParameters.D)
                .Normalize();
            byte[] x = q.AffineXCoord.ToBigInteger().ToByteArrayUnsigned();
            byte[] y = q.AffineYCoord.ToBigInteger().ToByteArrayUnsigned();
            byte[] d = eCPrivateKeyParameters.D.ToByteArrayUnsigned();

            // pad all to the same length since ToByteArrayUnsigned might drop leading zeroes
            x = X509Utils.PadWithLeadingZeros(x, keySizeBytes);
            y = X509Utils.PadWithLeadingZeros(y, keySizeBytes);
            d = X509Utils.PadWithLeadingZeros(d, keySizeBytes);

            var ecParams = new ECParameters
            {
                Curve = curve,
                Q = { X = x, Y = y },
                D = d
            };

            var ecdsa = ECDsa.Create();
            ecdsa.ImportParameters(ecParams);

            return ecdsa;
        }
#endif

        /// <summary>
        /// Wrapper for a password string.
        /// </summary>
        internal class Password : IPasswordFinder
        {
            private readonly char[] m_password;

            public Password(char[] word)
            {
                m_password = (char[])word.Clone();
            }

            public char[] GetPassword()
            {
                return (char[])m_password.Clone();
            }
        }
    }
}
#endif
