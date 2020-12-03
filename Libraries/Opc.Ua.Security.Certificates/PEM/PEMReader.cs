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

using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.IO;
using System.Text;

#if !NETSTANDARD2_1
using Opc.Ua.Security.Certificates.BouncyCastle;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Asn1.Pkcs;
#endif

namespace Opc.Ua.Security.Certificates
{
    public static class PEMReader
    {
        #region Public Methods
#if !NETSTANDARD2_1
        /// <summary>
        /// Import a private key from PEM.
        /// </summary>
        public static RSA ImportPrivateKeyFromPEM(
            byte[] pemDataBlob,
            string password = null)
        {
            RSA rsaPrivateKey = null;
            PemReader pemReader;
            using (StreamReader pemStreamReader = new StreamReader(new MemoryStream(pemDataBlob), Encoding.UTF8, true))
            {
                if (String.IsNullOrEmpty(password))
                {
                    pemReader = new PemReader(pemStreamReader);
                }
                else
                {
                    Password pwFinder = new Password(password.ToCharArray());
                    pemReader = new PemReader(pemStreamReader, pwFinder);
                }
                try
                {
                    // find the private key in the PEM blob
                    var pemObject = pemReader.ReadObject();
                    while (pemObject != null)
                    {
                        RsaPrivateCrtKeyParameters privateKey = null;
                        var keypair = pemObject as Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair;
                        if (keypair != null)
                        {
                            privateKey = keypair.Private as RsaPrivateCrtKeyParameters;
                            break;
                        }

                        if (privateKey == null)
                        {
                            privateKey = pemObject as RsaPrivateCrtKeyParameters;
                        }

                        if (privateKey != null)
                        {
                            rsaPrivateKey = RSA.Create();
                            rsaPrivateKey.ImportParameters(DotNetUtilities.ToRSAParameters(privateKey));
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
            }

            if (rsaPrivateKey == null)
            {
                throw new CryptographicException("PEM data blob does not contain a private key.");
            }

            return rsaPrivateKey;
        }
#else
        public static RSA ImportPrivateKeyFromPEM(
            byte[] pemDataBlob,
            string password = null)
        {
            string[] labels = {
                "ENCRYPTED PRIVATE KEY", "PRIVATE KEY", "RSA PRIVATE KEY"
                };
            try
            {
                string pemText = Encoding.UTF8.GetString(pemDataBlob);
                int count = 0;
                foreach (var label in labels)
                {
                    count++;
                    string beginlabel = $"-----BEGIN {label}-----";
                    int beginIndex = pemText.IndexOf(beginlabel);
                    if (beginIndex < 0)
                    {
                        continue;
                    }
                    string endlabel = $"-----END {label}-----";
                    int endIndex = pemText.IndexOf(endlabel);
                    beginIndex += beginlabel.Length;
                    if (endIndex < 0 || endIndex <= beginIndex)
                    {
                        continue;
                    }
                    var pemData = pemText.Substring(beginIndex, endIndex - beginIndex);
                    byte[] pemDecoded = new byte[pemData.Length];
                    int bytesDecoded;
                    if (Convert.TryFromBase64Chars(pemData, pemDecoded, out bytesDecoded))
                    {
                        RSA rsaPrivateKey = RSA.Create();
                        int bytesRead;
                        switch (count)
                        {
                            case 1:
                                if (String.IsNullOrEmpty(password))
                                {
                                    throw new ArgumentException("Need password for encrypted private key.");
                                }
                                rsaPrivateKey.ImportEncryptedPkcs8PrivateKey(password.ToCharArray(), pemDecoded, out bytesRead);
                                break;
                            case 2: rsaPrivateKey.ImportPkcs8PrivateKey(pemDecoded, out bytesRead); break;
                            case 3: rsaPrivateKey.ImportRSAPrivateKey(pemDecoded, out bytesRead); break;
                        }
                        return rsaPrivateKey;
                    }
                }
            }
            catch (CryptographicException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decode the PEM private key.", ex);
            }
            throw new ArgumentException("No private PEM key found.");
        }
#endif
        #endregion

        #region Private Methods
        #endregion
    }
}
