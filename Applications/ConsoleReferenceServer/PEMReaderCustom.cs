using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

#if NETSTANDARD2_1 || NET5_0_OR_GREATER

namespace Quickstarts
{
    public static class PEMReaderCustom
    {
        #region Public Methods
        /// <summary>
        /// Import multiple X509 certificates from PEM data.
        /// Supports a maximum of 99 certificates in the PEM data.
        /// </summary>
        /// <param name="pemDataBlob">The PEM datablob as byte array.</param>
        /// <returns>The certificates.</returns>
        public static X509Certificate2Collection ImportX509CertificatesFromPEM(
            ReadOnlySpan<byte> pemDataBlob)
        {
            var certificates = new X509Certificate2Collection();
            string label = "CERTIFICATE";
            try
            {
                string pemText = Encoding.UTF8.GetString(pemDataBlob);
                int count = 0;
                int endIndex = 0;
                while (endIndex > -1 && count < 99)
                {
                    count++;
                    string beginlabel = $"-----BEGIN {label}-----";
                    int beginIndex = pemText.IndexOf(beginlabel, StringComparison.Ordinal);
                    if (beginIndex < 0)
                    {
                        return certificates;
                    }
                    string endlabel = $"-----END {label}-----";
                    endIndex = pemText.IndexOf(endlabel, StringComparison.Ordinal);
                    beginIndex += beginlabel.Length;
                    if (endIndex < 0 || endIndex <= beginIndex)
                    {
                        return certificates;
                    }
                    var pemCertificateContent = pemText.Substring(beginIndex, endIndex - beginIndex);
                    Span<byte> pemCertificateDecoded = new Span<byte>(new byte[pemCertificateContent.Length]);
                    if (Convert.TryFromBase64Chars(pemCertificateContent, pemCertificateDecoded, out var bytesWritten))
                    {
#if NET6_0_OR_GREATER
                        certificates.Add(X509CertificateLoader.LoadCertificate(pemCertificateDecoded));
#else
                        certificates.Add(X509CertificateLoader.LoadCertificate(pemCertificateDecoded.ToArray()));
#endif
                    }

                    pemText = pemText.Substring(endIndex + endlabel.Length);
                }
            }
            catch (CryptographicException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decode the PEM encoded Certificates.", ex);
            }
            return certificates;
        }
    }
    #endregion
}
#endif
