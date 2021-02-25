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

#if !NETSTANDARD2_1 

using System;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Text;
using Opc.Ua.Security.Certificates.BouncyCastle;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Asn1.Pkcs;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Write certificate data in PEM format.
    /// </summary>
    public static class PEMWriter
    {
        #region Public Methods
        /// <summary>
        /// Returns a byte array containing the CSR in PEM format.
        /// </summary>
        public static byte[] ExportCSRAsPEM(byte[] csr)
        {
            return EncodeAsPEM(csr, "CERTIFICATE REQUEST");
        }

        /// <summary>
        /// Returns a byte array containing the cert in PEM format.
        /// </summary>
        public static byte[] ExportCertificateAsPEM(X509Certificate2 certificate)
        {
            return EncodeAsPEM(certificate.RawData, "CERTIFICATE");
        }

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        public static byte[] ExportPrivateKeyAsPEM(
            X509Certificate2 certificate,
            string password = null
            )
        {
            if (!String.IsNullOrEmpty(password)) throw new ArgumentException(nameof(password), "Export with password not supported on this platform.");
            RsaPrivateCrtKeyParameters privateKeyParameter = X509Utils.GetPrivateKeyParameter(certificate);
            // write private key as PKCS#8
            PrivateKeyInfo privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKeyParameter);
            byte[] serializedPrivateBytes = privateKeyInfo.ToAsn1Object().GetDerEncoded();
            return EncodeAsPEM(serializedPrivateBytes, "PRIVATE KEY");
        }
        #endregion

        #region Private Methods
        private static byte[] EncodeAsPEM(byte[] content, string contentType)
        {
            const int LineLength = 64;
            string base64 = Convert.ToBase64String(content);
            using (TextWriter textWriter = new StringWriter())
            {
                textWriter.WriteLine("-----BEGIN {0}-----", contentType);
                while (base64.Length > LineLength)
                {
                    textWriter.WriteLine(base64.Substring(0, LineLength));
                    base64 = base64.Substring(LineLength);
                }
                textWriter.WriteLine(base64);
                textWriter.WriteLine("-----END {0}-----", contentType);
                return Encoding.ASCII.GetBytes(textWriter.ToString());
            }
        }
        #endregion
    }
}
#endif
