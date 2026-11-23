// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if NETFRAMEWORK
using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua.Security.Certificates.BouncyCastle;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Write certificate data in PEM format.
    /// </summary>
    public static partial class PEMWriter
    {
        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] ExportPrivateKeyAsPEM(
            Certificate certificate,
            ReadOnlySpan<char> password = default)
        {
            bool isECDsaSignature = X509PfxUtils.IsECDsaSignature(certificate);
            // check if certificate is valid for use as app/sw or user cert
            if (!isECDsaSignature)
            {
                if (!password.IsEmpty && !password.IsWhiteSpace())
                {
                    throw new ArgumentException(
                        "Export with password not supported on this platform.",
                        nameof(password));
                }

                RsaPrivateCrtKeyParameters? privateKeyParameter = X509Utils
                    .GetRsaPrivateKeyParameter(certificate);
                // write private key as PKCS#8
                PrivateKeyInfo privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(
                    privateKeyParameter);
                byte[] serializedPrivateBytes = privateKeyInfo.ToAsn1Object().GetDerEncoded();
                return EncodeAsPEM(serializedPrivateBytes, "PRIVATE KEY");
            }
            else
            {
                if (!password.IsEmpty && !password.IsWhiteSpace())
                {
                    throw new ArgumentException(
                        "Export with password not supported on this platform.",
                        nameof(password));
                }

                ECPrivateKeyParameters? privateKeyParameter = X509Utils.GetECDsaPrivateKeyParameter(
                    certificate);
                // write private key as PKCS#8
                PrivateKeyInfo privateKeyInfo = PrivateKeyInfoFactory.CreatePrivateKeyInfo(
                    privateKeyParameter);
                byte[] serializedPrivateBytes = privateKeyInfo.ToAsn1Object().GetDerEncoded();
                return EncodeAsPEM(serializedPrivateBytes, "PRIVATE KEY");
            }
        }

        /// <summary>
        /// Returns a byte array containing the private key in PEM format.
        /// </summary>
        public static bool TryRemovePublicKeyFromPEM(
            string thumbprint,
            byte[] pemDataBlob,
            [NotNullWhen(true)] out byte[]? modifiedPemDataBlob)
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

                    using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(
                        pemCertificateDecoded);
                    if (thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        // Substring takes a length, not an end index. Passing
                        // the end index removed the wrong span - and for any
                        // block that did not start at the beginning of the file
                        // it ran off the end and threw, which this method
                        // reports as "not found" and the caller then treats as
                        // a file with nothing left in it.
                        int blockStart = beginIndex - beginlabel.Length;
                        int blockEnd = endIndex + endlabel.Length;
                        int blockLength = blockEnd - blockStart;

                        modifiedPemDataBlob = Encoding.ASCII.GetBytes(
                            pemText.Replace(
                                pemText.Substring(blockStart, blockLength),
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
    }
}
#endif
