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
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Write certificate/crl data in PEM format.
    /// </summary>
    public static partial class PEMWriter
    {
        #region Public Methods
        /// <summary>
        /// Returns a byte array containing the CRL in PEM format.
        /// </summary>
        public static byte[] ExportCRLAsPEM(byte[] crl)
        {
            return EncodeAsPEM(crl, "X509 CRL");
        }

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

#if NETSTANDARD2_1 || NET5_0_OR_GREATER
        /// <summary>
        /// Returns a byte array containing the public key in PEM format.
        /// </summary>
        public static byte[] ExportPublicKeyAsPEM(
            X509Certificate2 certificate
            )
        {
            byte[] exportedPublicKey = null;
            using (RSA rsaPublicKey = certificate.GetRSAPublicKey())
            {
                exportedPublicKey = rsaPublicKey.ExportSubjectPublicKeyInfo();
            }
            return EncodeAsPEM(exportedPublicKey, "PUBLIC KEY");
        }

        /// <summary>
        /// Returns a byte array containing the RSA private key in PEM format.
        /// </summary>
        public static byte[] ExportRSAPrivateKeyAsPEM(
            X509Certificate2 certificate)
        {
            byte[] exportedRSAPrivateKey = null;
            using (RSA rsaPrivateKey = certificate.GetRSAPrivateKey())
            {
                // write private key as PKCS#1
                exportedRSAPrivateKey = rsaPrivateKey.ExportRSAPrivateKey();
            }
            return EncodeAsPEM(exportedRSAPrivateKey, "RSA PRIVATE KEY");
        }

        /// <summary>
        /// Returns a byte array containing the ECDsa private key in PEM format.
        /// </summary>
        public static byte[] ExportECDsaPrivateKeyAsPEM(
            X509Certificate2 certificate)
        {
            byte[] exportedECPrivateKey = null;
            using (ECDsa ecdsaPrivateKey = certificate.GetECDsaPrivateKey())
            {
                // write private key as PKCS#1
                exportedECPrivateKey = ecdsaPrivateKey.ExportECPrivateKey();
            }
            return EncodeAsPEM(exportedECPrivateKey, "EC PRIVATE KEY");
        }

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        public static byte[] ExportPrivateKeyAsPEM(
            X509Certificate2 certificate,
            string password = null
            )
        {
            byte[] exportedPkcs8PrivateKey = null;
            using (RSA rsaPrivateKey = certificate.GetRSAPrivateKey())
            {
                if (rsaPrivateKey != null)
                {
                    // write private key as PKCS#8
                    exportedPkcs8PrivateKey = String.IsNullOrEmpty(password) ?
                        rsaPrivateKey.ExportPkcs8PrivateKey() :
                        rsaPrivateKey.ExportEncryptedPkcs8PrivateKey(password.ToCharArray(),
                            new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2000));
                }
                else
                {
                    using (ECDsa ecdsaPrivateKey = certificate.GetECDsaPrivateKey())
                    {
                        if (ecdsaPrivateKey != null)
                        {
                            // write private key as PKCS#8
                            exportedPkcs8PrivateKey = String.IsNullOrEmpty(password) ?
                            ecdsaPrivateKey.ExportPkcs8PrivateKey() :
                            ecdsaPrivateKey.ExportEncryptedPkcs8PrivateKey(password.ToCharArray(),
                                new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2000));
                        }
                    }
                }
            }

            return EncodeAsPEM(exportedPkcs8PrivateKey,
                String.IsNullOrEmpty(password) ? "PRIVATE KEY" : "ENCRYPTED PRIVATE KEY");
        }
#endif
        #endregion

        #region Private Methods
        private static byte[] EncodeAsPEM(byte[] content, string contentType)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (String.IsNullOrEmpty(contentType)) throw new ArgumentNullException(nameof(contentType));

            const int LineLength = 64;
            string base64 = Convert.ToBase64String(content);
            using (TextWriter textWriter = new StringWriter())
            {
                textWriter.WriteLine("-----BEGIN {0}-----", contentType);
                while (base64.Length > LineLength)
                {
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
                    textWriter.WriteLine(base64.AsSpan(0, LineLength));
#else
                    textWriter.WriteLine(base64.Substring(0, LineLength));
#endif
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
