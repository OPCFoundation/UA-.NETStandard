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

#if NETSTANDARD2_1

using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Methods or read PEM data.
    /// </summary>
    public static class PEMReader
    {
        #region Public Methods
        /// <summary>
        /// Import a PKCS#8 private key or RSA private key from PEM.
        /// The PKCS#8 private key may be encrypted using a password.
        /// </summary>
        /// <param name="pemDataBlob">The PEM datablob as byte array.</param>
        /// <param name="password">The password to use (optional).</param>
        /// <returns>The RSA private key.</returns>
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
        #endregion

        #region Private Methods
        #endregion
    }
}
#endif
