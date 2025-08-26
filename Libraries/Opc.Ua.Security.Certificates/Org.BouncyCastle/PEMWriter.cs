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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Security.Certificates.BouncyCastle;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
#endif

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Write certificate data in PEM format.
    /// </summary>
    public static partial class PEMWriter
    {
#if !NETSTANDARD2_1 && !NET5_0_OR_GREATER

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] ExportPrivateKeyAsPEM(
            X509Certificate2 certificate,
            string password = null)
        {
            bool isECDsaSignature = X509PfxUtils.IsECDsaSignature(certificate);
            // check if certificate is valid for use as app/sw or user cert
            if (!isECDsaSignature)
            {
                if (!string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException(
                        "Export with password not supported on this platform.",
                        nameof(password));
                }

                RsaPrivateCrtKeyParameters privateKeyParameter = X509Utils
                    .GetRsaPrivateKeyParameter(certificate);
                // write private key as PKCS#8
                PrivateKeyInfo privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(
                    privateKeyParameter);
                byte[] serializedPrivateBytes = privateKeyInfo.ToAsn1Object().GetDerEncoded();
                return EncodeAsPEM(serializedPrivateBytes, "PRIVATE KEY");
            }
#if ECC_SUPPORT
            else
            {
                if (!string.IsNullOrEmpty(password))
                {
                    throw new ArgumentException(
                        "Export with password not supported on this platform.",
                        nameof(password));
                }

                ECPrivateKeyParameters privateKeyParameter = X509Utils.GetECDsaPrivateKeyParameter(
                    certificate.GetECDsaPrivateKey());
                // write private key as PKCS#8
                PrivateKeyInfo privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(
                    privateKeyParameter);
                byte[] serializedPrivateBytes = privateKeyInfo.ToAsn1Object().GetDerEncoded();
                return EncodeAsPEM(serializedPrivateBytes, "PRIVATE KEY");
            }
#else
            throw new ArgumentException("ExportPrivateKeyAsPEM not supported on this platform."); // Only on NETSTANDARD2_0
#endif
        }

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        public static bool TryRemovePublicKeyFromPEM(
            string thumbprint,
            byte[] pemDataBlob,
            out byte[] modifiedPemDataBlob)
        {
            modifiedPemDataBlob = null;
            const string label = "CERTIFICATE";
            string beginlabel = $"-----BEGIN {label}-----";
            string endlabel = $"-----END {label}-----";
            try
            {
                string pemText = Encoding.UTF8.GetString(pemDataBlob);
                int searchPosition = 0;
                int count = 0;
                int endIndex = 0;
                while (endIndex > -1 && count < 99)
                {
                    count++;
                    int beginIndex = pemText.IndexOf(
                        beginlabel,
                        searchPosition,
                        StringComparison.Ordinal);
                    if (beginIndex < 0)
                    {
                        return false;
                    }
                    endIndex = pemText.IndexOf(endlabel, searchPosition, StringComparison.Ordinal);
                    beginIndex += beginlabel.Length;
                    if (endIndex < 0 || endIndex <= beginIndex)
                    {
                        return false;
                    }
                    string pemCertificateContent = pemText[beginIndex..endIndex];
                    byte[] pemCertificateDecoded = Convert.FromBase64CharArray(
                        pemCertificateContent.ToCharArray(),
                        0,
                        pemCertificateContent.Length);

                    X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(
                        pemCertificateDecoded);
                    if (thumbprint.Equals(
                        certificate.Thumbprint,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        modifiedPemDataBlob = Encoding.ASCII.GetBytes(
                            pemText.Replace(
                                pemText.Substring(
                                    beginIndex -= beginlabel.Length,
                                    endIndex + endlabel.Length),
                                string.Empty));
                        return true;
                    }

                    searchPosition = endIndex + endlabel.Length;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

#endif
    }
}
