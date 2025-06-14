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

#if NETSTANDARD2_1 || NET5_0_OR_GREATER

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        /// Checks if the PEM data contains a private key.
        /// </summary>
        /// <param name="pemDataBlob">The PEM data as a byte span.</param>
        /// <returns>True if a private key is found.</returns>
        public static bool ContainsPrivateKey(ReadOnlySpan<byte> pemDataBlob)
        {
            try
            {
                string pemText = Encoding.UTF8.GetString(pemDataBlob);


                string[] valuesToCheck = {
                    "-----BEGIN PRIVATE KEY-----",
                    "-----BEGIN RSA PRIVATE KEY-----",
                    "-----BEGIN ENCRYPTED PRIVATE KEY-----",
                    "-----BEGIN EC PRIVATE KEY-----"
                };

                return valuesToCheck.Any(value => pemText.Contains(value, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Import multiple X509 certificates from PEM data.
        /// Supports a maximum of 99 certificates in the PEM data.
        /// </summary>
        /// <param name="pemDataBlob">The PEM datablob as byte array.</param>
        /// <returns>The certificates.</returns>
        public static X509Certificate2Collection ImportPublicKeysFromPEM(
            ReadOnlySpan<byte> pemDataBlob)
        {
            var certificates = new X509Certificate2Collection();
            string label = "CERTIFICATE";
            string beginlabel = $"-----BEGIN {label}-----";
            string endlabel = $"-----END {label}-----";
            try
            {
                ReadOnlySpan<char> pemText = Encoding.UTF8.GetString(pemDataBlob).AsSpan();
                int count = 0;
                int endIndex = 0;
                while (endIndex > -1 && count < 99)
                {
                    count++;
                    int beginIndex = pemText.IndexOf(beginlabel, StringComparison.Ordinal);
                    if (beginIndex < 0)
                    {
                        return certificates;
                    }
                    endIndex = pemText.IndexOf(endlabel, StringComparison.Ordinal);
                    beginIndex += beginlabel.Length;
                    if (endIndex < 0 || endIndex <= beginIndex)
                    {
                        return certificates;
                    }
                    var pemCertificateContent = pemText.Slice(beginIndex, endIndex - beginIndex);
                    Span<byte> pemCertificateDecoded = new Span<byte>(new byte[pemCertificateContent.Length]);
                    if (Convert.TryFromBase64Chars(pemCertificateContent, pemCertificateDecoded, out var bytesWritten))
                    {
#if NET6_0_OR_GREATER
                        certificates.Add(X509CertificateLoader.LoadCertificate(pemCertificateDecoded));
#else
                        certificates.Add(X509CertificateLoader.LoadCertificate(pemCertificateDecoded.ToArray()));
#endif
                    }

                    pemText = pemText.Slice(endIndex + endlabel.Length);
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

        /// <summary>
        /// Import a PKCS#8 private key or RSA private key from PEM.
        /// The PKCS#8 private key may be encrypted using a password.
        /// </summary>
        /// <param name="pemDataBlob">The PEM datablob as byte array.</param>
        /// <param name="password">The password to use (optional).</param>
        /// <returns>The RSA private key.</returns>
        public static RSA ImportRsaPrivateKeyFromPEM(
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
                    int beginIndex = pemText.IndexOf(beginlabel, StringComparison.Ordinal);
                    if (beginIndex < 0)
                    {
                        continue;
                    }
                    string endlabel = $"-----END {label}-----";
                    int endIndex = pemText.IndexOf(endlabel, StringComparison.Ordinal);
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

        /// <summary>
        /// Import ECDSA private key from PEM data
        /// The PKCS#8 private key may be encrypted using a password
        /// </summary>
        /// <param name="pemDataBlob">The PEM data as byte array.</param>
        /// <param name="password">The password to use if the key is encrypted (optional)</param>
        /// <returns>ECDsa instance containing the private key</returns>
        public static ECDsa ImportECDsaPrivateKeyFromPEM(
            byte[] pemDataBlob,
            string password = null)
        {
            // PEM labels for EC keys. Probably need adjustment
            string[] labels =
            {
                "ENCRYPTED PRIVATE KEY",
                "PRIVATE KEY",
                "EC PRIVATE KEY"
            };

            try
            {
                // Convert PEM data to text for parsing
                string pemText = Encoding.UTF8.GetString(pemDataBlob);

                int labelIndex = 0;
                foreach (var label in labels)
                {
                    labelIndex++;
                    string beginLabel = $"-----BEGIN {label}-----";
                    int beginIndex = pemText.IndexOf(beginLabel, StringComparison.Ordinal);
                    if (beginIndex < 0)
                    {
                        continue;
                    }
                    string endLabel = $"-----END {label}-----";
                    int endIndex = pemText.IndexOf(endLabel, StringComparison.Ordinal);

                    beginIndex += beginLabel.Length;
                    if (endIndex < 0 || endIndex <= beginIndex)
                    {
                        continue;
                    }

                    // Extract the base64-encoded section
                    string pemData = pemText.Substring(beginIndex, endIndex - beginIndex).Trim();
                    byte[] decodedBytes = new byte[pemData.Length];
                    if (Convert.TryFromBase64Chars(pemData, decodedBytes, out int bytesDecoded))
                    {
                        // Resize array to actual decoded length
                        Array.Resize(ref decodedBytes, bytesDecoded);

                        // Create an ECDsa object
                        ECDsa ecdsaKey = ECDsa.Create();
                        switch (labelIndex)
                        {
                            case 1:
                                // ENCRYPTED PRIVATE KEY
                                if (string.IsNullOrEmpty(password))
                                {
                                    throw new ArgumentException("A password is required for an encrypted private key.");
                                }
                                ecdsaKey.ImportEncryptedPkcs8PrivateKey(password.ToCharArray(), decodedBytes, out _);
                                break;

                            case 2:
                                // PRIVATE KEY (Unencrypted PKCS#8)
                                ecdsaKey.ImportPkcs8PrivateKey(decodedBytes, out _);
                                break;

                            case 3:
                                // EC PRIVATE KEY
                                ecdsaKey.ImportECPrivateKey(decodedBytes, out _);
                                break;
                        }
                        return ecdsaKey;
                    }
                }
            }
            catch (CryptographicException)
            {
                // Re-throw to handle upstream
                throw;
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decode the PEM ECDSA private key.", ex);
            }

            // If no recognized PEM label was found
            throw new ArgumentException("No ECDSA private PEM key found.");
        }
        #endregion

        #region Private Methods
        #endregion
    }
}
#endif
