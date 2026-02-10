/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
#if !NETFRAMEWORK
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// Utility functions for X509 certificates.
    /// </summary>
    public static class X509Utils
    {
        /// <summary>
        /// Extracts the DNS names specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The DNS names.</returns>
        public static IList<string> GetDomainsFromCertificate(X509Certificate2 certificate)
        {
            var dnsNames = new List<string>();

            // extracts the domain from the subject name.
            List<string> fields = ParseDistinguishedName(certificate.Subject);

            var builder = new StringBuilder();

            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (fields[ii].StartsWith("DC=", StringComparison.Ordinal))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }
#if NET5_0_OR_GREATER || NETSTANDARD2_1
                    builder.Append(fields[ii].AsSpan(3));
#else
                    builder.Append(fields[ii][3..]);
#endif
                }
            }

            if (builder.Length > 0)
            {
                dnsNames.Add(builder.ToString().ToUpperInvariant());
            }

            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = certificate
                .FindExtension<X509SubjectAltNameExtension>();
            if (alternateName != null)
            {
                for (int ii = 0; ii < alternateName.DomainNames.Count; ii++)
                {
                    string hostname = alternateName.DomainNames[ii];

                    // do not add duplicates to the list.
                    bool found = false;

                    for (int jj = 0; jj < dnsNames.Count; jj++)
                    {
                        if (string.Equals(
                            dnsNames[jj],
                            hostname,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        dnsNames.Add(hostname.ToUpperInvariant());
                    }
                }

                for (int ii = 0; ii < alternateName.IPAddresses.Count; ii++)
                {
                    string ipAddress = alternateName.IPAddresses[ii];

                    if (!dnsNames.Contains(ipAddress))
                    {
                        dnsNames.Add(ipAddress);
                    }
                }
            }

            // return the list.
            return dnsNames;
        }

        /// <summary>
        /// Returns the size of the public key and disposes RSA key.
        /// </summary>
        /// <param name="certificate">The certificate</param>
        public static int GetRSAPublicKeySize(X509Certificate2 certificate)
        {
            using RSA rsaPublicKey = certificate.GetRSAPublicKey();
            if (rsaPublicKey != null)
            {
                return rsaPublicKey.KeySize;
            }
            return -1;
        }

        /// <summary>
        /// Returns the size of the public key of a given certificate
        /// </summary>
        /// <param name="certificate">The certificate</param>
        public static int GetPublicKeySize(X509Certificate2 certificate)
        {
            using (RSA rsaPublicKey = certificate.GetRSAPublicKey())
            {
                if (rsaPublicKey != null)
                {
                    return rsaPublicKey.KeySize;
                }
            }

            using ECDsa ecdsaPublicKey = certificate.GetECDsaPublicKey();
            if (ecdsaPublicKey != null)
            {
                return ecdsaPublicKey.KeySize;
            }

            return -1;
        }

        /// <summary>
        /// Extracts the application URI specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The application URI.</returns>
        [Obsolete("Use GetApplicationUrisFromCertificate instead. The certificate may contain more than one Uri.")]
        public static string GetApplicationUriFromCertificate(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = certificate
                .FindExtension<X509SubjectAltNameExtension>();

            // get the application uri.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                return alternateName.Uris[0];
            }

            return string.Empty;
        }

        /// <summary>
        /// Extracts the application URIs specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The application URIs.</returns>
        public static IReadOnlyList<string> GetApplicationUrisFromCertificate(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = certificate
                .FindExtension<X509SubjectAltNameExtension>();

            // get the application uris.
            if (alternateName != null && alternateName.Uris != null)
            {
                return alternateName.Uris;
            }

            return [];
        }

        /// <summary>
        /// Checks if the specified application URI matches any of the URIs in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate to check.</param>
        /// <param name="applicationUri">The application URI to match.</param>
        /// <returns>True if the application URI matches any URI in the certificate; otherwise, false.</returns>
        public static bool CompareApplicationUriWithCertificate(X509Certificate2 certificate, string applicationUri)
        {
            return CompareApplicationUriWithCertificate(certificate, applicationUri, out _);
        }

        /// <summary>
        /// Checks if the specified application URI matches any of the URIs in the certificate.
        /// Returns the list of application URIs found in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate to check.</param>
        /// <param name="applicationUri">The application URI to match.</param>
        /// <param name="certificateApplicationUris">The list of application URIs found in the certificate.</param>
        /// <returns>True if the application URI matches any URI in the certificate; otherwise, false.</returns>
        public static bool CompareApplicationUriWithCertificate(
            X509Certificate2 certificate,
            string applicationUri,
            out IReadOnlyList<string> certificateApplicationUris)
        {
            certificateApplicationUris = GetApplicationUrisFromCertificate(certificate);

            if (string.IsNullOrEmpty(applicationUri))
            {
                return false;
            }

            foreach (string certificateApplicationUri in certificateApplicationUris)
            {
                if (!string.IsNullOrEmpty(certificateApplicationUri) &&
                    string.Equals(certificateApplicationUri, applicationUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if certificate has an application urn.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>true if the application URI starts with urn: </returns>
        public static bool HasApplicationURN(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = certificate
                .FindExtension<X509SubjectAltNameExtension>();

            // find the application urn.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                const string urn = "urn:";
                for (int i = 0; i < alternateName.Uris.Count; i++)
                {
                    if (string.Compare(
                            alternateName.Uris[i],
                            0,
                            urn,
                            0,
                            urn.Length,
                            StringComparison.OrdinalIgnoreCase) ==
                        0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks that the domain in the URL provided matches one of the domains in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="endpointUrl">The endpoint url to verify.</param>
        /// <returns>True if the certificate matches the url.</returns>
        public static bool DoesUrlMatchCertificate(X509Certificate2 certificate, Uri endpointUrl)
        {
            if (endpointUrl == null || certificate == null)
            {
                return false;
            }

            IList<string> domainNames = GetDomainsFromCertificate(certificate);

            for (int jj = 0; jj < domainNames.Count; jj++)
            {
                if (string.Equals(
                    domainNames[jj],
                    endpointUrl.IdnHost,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the certificate is allowed to be an issuer.
        /// </summary>
        public static bool IsIssuerAllowed(X509Certificate2 certificate)
        {
            X509BasicConstraintsExtension constraints = certificate
                .FindExtension<X509BasicConstraintsExtension>();

            if (constraints != null)
            {
                return constraints.CertificateAuthority;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the certificate is issued by a Certificate Authority.
        /// </summary>
        public static bool IsCertificateAuthority(X509Certificate2 certificate)
        {
            X509BasicConstraintsExtension constraints = certificate
                .FindExtension<X509BasicConstraintsExtension>();
            if (constraints != null)
            {
                return constraints.CertificateAuthority;
            }
            return false;
        }

        /// <summary>
        /// Return the key usage flags of a certificate.
        /// </summary>
        public static X509KeyUsageFlags GetKeyUsage(X509Certificate2 cert)
        {
            X509KeyUsageFlags allFlags = X509KeyUsageFlags.None;
            foreach (X509KeyUsageExtension ext in cert.Extensions.OfType<X509KeyUsageExtension>())
            {
                allFlags |= ext.KeyUsages;
            }
            return allFlags;
        }

        /// <summary>
        /// Check for self signed certificate if there is match of the Subject/Issuer.
        /// </summary>
        /// <param name="certificate">The certificate to test.</param>
        /// <returns>True if self signed.</returns>
        public static bool IsSelfSigned(X509Certificate2 certificate)
        {
            return CompareDistinguishedName(certificate.SubjectName, certificate.IssuerName);
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(
            X500DistinguishedName name1,
            X500DistinguishedName name2)
        {
            // check for simple binary equality.
            return Utils.IsEqual(name1.RawData, name2.RawData);
        }

        /// <summary>
        /// Compares two distinguished names as strings.
        /// </summary>
        /// <remarks>
        /// Where possible, distinguished names should be compared
        /// by using the <see cref="X500DistinguishedName"/> version.
        /// </remarks>
        public static bool CompareDistinguishedName(string name1, string name2)
        {
            // check for simple equality.
            if (string.Equals(name1, name2, StringComparison.Ordinal))
            {
                return true;
            }

            // parse the names.
            List<string> fields1 = ParseDistinguishedName(name1);
            List<string> fields2 = ParseDistinguishedName(name2);

            // can't be equal if the number of fields is different.
            if (fields1.Count != fields2.Count)
            {
                return false;
            }

            return CompareDistinguishedNameFields(fields1, fields2);
        }

        /// <summary>
        /// Compares string fields of two distinguished names.
        /// </summary>
        /// <summary>
        /// Normalizes distinguished name field abbreviations to handle platform-specific variations.
        /// For example, Windows may use 'S=' while OpenSSL uses 'ST=' for stateOrProvinceName.
        /// </summary>
        /// <param name="field">The distinguished name field to normalize.</param>
        /// <returns>The normalized field with standardized abbreviations.</returns>
        private static string NormalizeDistinguishedNameField(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                return field;
            }

            // Handle state/province: S= -> ST=
            // Windows may use S= while documentation and OpenSSL use ST=
            if (field.StartsWith("S=", StringComparison.OrdinalIgnoreCase) &&
                !field.StartsWith("ST=", StringComparison.OrdinalIgnoreCase))
            {
                return $"ST={field[2..]}";
            }

            return field;
        }

        private static bool CompareDistinguishedNameFields(
            List<string> fields1,
            List<string> fields2)
        {
            // compare each.
            for (int ii = 0; ii < fields1.Count; ii++)
            {
                // Normalize field abbreviations to handle platform-specific variations
                string normalizedField1 = NormalizeDistinguishedNameField(fields1[ii]);
                string normalizedField2 = NormalizeDistinguishedNameField(fields2[ii]);

                StringComparison comparison = StringComparison.Ordinal;
                if (normalizedField1.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
                {
                    // DC hostnames may have different case
                    comparison = StringComparison.OrdinalIgnoreCase;
                }
                if (!string.Equals(normalizedField1, normalizedField2, comparison))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Compares two distinguished names.
        /// </summary>
        public static bool CompareDistinguishedName(
            X509Certificate2 certificate,
            List<string> parsedName)
        {
            // can't compare if the number of fields is 0.
            if (parsedName.Count == 0)
            {
                return false;
            }

            // parse the names.
            List<string> certificateName = ParseDistinguishedName(certificate.Subject);

            // can't be equal if the number of fields is different.
            if (parsedName.Count != certificateName.Count)
            {
                return false;
            }

            return CompareDistinguishedNameFields(parsedName, certificateName);
        }

        private static readonly char[] s_anyOf = ['/', ',', '='];

        /// <summary>
        /// Parses a distingushed name.
        /// </summary>
        public static List<string> ParseDistinguishedName(string name)
        {
            var fields = new List<string>();

            if (string.IsNullOrEmpty(name))
            {
                return fields;
            }

            // determine the delimiter used.
            char delimiter = ',';
            bool quoted = false;

            for (int ii = name.Length - 1; ii >= 0; ii--)
            {
                char ch = name[ii];

                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && ch == '=')
                {
                    ii--;

                    while (ii >= 0 && char.IsWhiteSpace(name[ii]))
                    {
                        ii--;
                    }

                    while (ii >= 0 && (char.IsLetterOrDigit(name[ii]) || name[ii] == '.'))
                    {
                        ii--;
                    }

                    while (ii >= 0 && char.IsWhiteSpace(name[ii]))
                    {
                        ii--;
                    }

                    if (ii >= 0)
                    {
                        delimiter = name[ii];
                    }

                    break;
                }
            }

            var buffer = new StringBuilder();

            string key = null;
            bool found = false;

            for (int ii = 0; ii < name.Length; ii++)
            {
                while (ii < name.Length && char.IsWhiteSpace(name[ii]))
                {
                    ii++;
                }

                if (ii >= name.Length)
                {
                    break;
                }

                char ch;
                if (found)
                {
                    char end = delimiter;

                    if (ii < name.Length && name[ii] == '"')
                    {
                        ii++;
                        end = '"';
                    }

                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == end)
                        {
                            while (ii < name.Length && name[ii] != delimiter)
                            {
                                ii++;
                            }

                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    string value = buffer.ToString().TrimEnd();
                    found = false;

                    buffer.Length = 0;
                    buffer.Append(key)
                        .Append('=');

                    if (value.IndexOfAny(s_anyOf) != -1)
                    {
                        if (value.Length > 0 && value[0] != '"')
                        {
                            buffer.Append('"');
                        }

                        buffer.Append(value);

                        if (value.Length > 0 && value[^1] != '"')
                        {
                            buffer.Append('"');
                        }
                    }
                    else
                    {
                        buffer.Append(value);
                    }

                    fields.Add(buffer.ToString());
                    buffer.Length = 0;
                }
                else
                {
                    while (ii < name.Length)
                    {
                        ch = name[ii];

                        if (ch == '=')
                        {
                            break;
                        }

                        buffer.Append(ch);
                        ii++;
                    }

                    key = buffer.ToString().TrimEnd().ToUpperInvariant();
                    buffer.Length = 0;
                    found = true;
                }
            }

            return fields;
        }

        /// <summary>
        /// Return if a certificate has a ECDsa signature.
        /// </summary>
        /// <param name="cert">The certificate to test.</param>
        public static bool IsECDsaSignature(X509Certificate2 cert)
        {
            return X509PfxUtils.IsECDsaSignature(cert);
        }

        /// <summary>
        /// Return a qualifier string if a ECDsa signature algorithm used.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        public static string GetECDsaQualifier(X509Certificate2 certificate)
        {
            return EccUtils.GetECDsaQualifier(certificate);
        }

        /// <summary>
        /// Verify RSA/ECDsa key pair of two certificates.
        /// </summary>
        public static bool VerifyKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            return X509PfxUtils.VerifyKeyPair(certWithPublicKey, certWithPrivateKey, throwOnError);
        }

        /// <summary>
        /// Verify ECDsa key pair of two certificates.
        /// </summary>
        /// <exception cref="NotSupportedException"></exception>
        public static bool VerifyECDsaKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            return X509PfxUtils.VerifyECDsaKeyPair(
                certWithPublicKey,
                certWithPrivateKey,
                throwOnError);
        }

        /// <summary>
        /// Verify RSA key pair of two certificates.
        /// </summary>
        public static bool VerifyRSAKeyPair(
            X509Certificate2 certWithPublicKey,
            X509Certificate2 certWithPrivateKey,
            bool throwOnError = false)
        {
            return X509PfxUtils.VerifyRSAKeyPair(
                certWithPublicKey,
                certWithPrivateKey,
                throwOnError);
        }

        /// <summary>
        /// Verify the signature of a self signed certificate.
        /// </summary>
        public static bool VerifySelfSigned(X509Certificate2 cert)
        {
            try
            {
                var signature = new X509Signature(cert.RawData);
                return signature.Verify(cert);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a copy of a certificate with a private key.
        /// If the platform defaults to an ephemeral key set,
        /// the private key requires an extra copy.
        /// </summary>
        /// <returns>The certificate</returns>
        public static X509Certificate2 CreateCopyWithPrivateKey(
            X509Certificate2 certificate,
            bool persisted)
        {
            // a copy is only necessary on windows
            if (certificate.HasPrivateKey
#if !NETFRAMEWORK
                && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
#endif
                )
            {
                // see https://github.com/dotnet/runtime/issues/29144
                char[] passcode = GeneratePasscode();
                try
                {
                    // create a secure string for the passcode only on windows
                    using var securePasscode = new SecureString();
                    foreach (char c in passcode)
                    {
                        securePasscode.AppendChar(c);
                    }
                    securePasscode.MakeReadOnly();
                    X509KeyStorageFlags storageFlags =
                        persisted ? X509KeyStorageFlags.PersistKeySet : X509KeyStorageFlags.Exportable;
                    return X509CertificateLoader.LoadPkcs12(
                        certificate.Export(X509ContentType.Pfx, securePasscode),
                        passcode,
                        storageFlags);
                }
                finally
                {
                    Array.Clear(passcode, 0, passcode.Length);
                }
            }
            return certificate;
        }

        /// <summary>
        /// Creates a certificate from a PKCS #12 store with a private key.
        /// </summary>
        /// <param name="rawData">The raw PKCS #12 store data.</param>
        /// <param name="password">The password to use to access the store.</param>
        /// <param name="noEphemeralKeySet">Set to true if the key should not use the ephemeral key set.</param>
        /// <returns>The certificate with a private key.</returns>
        public static X509Certificate2 CreateCertificateFromPKCS12(
            byte[] rawData,
            ReadOnlySpan<char> password,
            bool noEphemeralKeySet = false)
        {
            return X509PfxUtils.CreateCertificateFromPKCS12(rawData, password, noEphemeralKeySet);
        }

        /// <summary>
        /// Get the certificate by issuer and serial number.
        /// </summary>
        public static async Task<X509Certificate2> FindIssuerCABySerialNumberAsync(
            ICertificateStore store,
            X500DistinguishedName issuer,
            string serialnumber)
        {
            X509Certificate2Collection certificates = await store.EnumerateAsync()
                .ConfigureAwait(false);

            foreach (X509Certificate2 certificate in certificates)
            {
                if (CompareDistinguishedName(certificate.SubjectName, issuer) &&
                    Utils.IsEqual(certificate.SerialNumber, serialnumber))
                {
                    return certificate;
                }
            }

            return null;
        }


        /// <summary>
        /// Get the certificate issuer by its key identifier.
        /// </summary>
        public static async Task<X509Certificate2> FindIssuerCAByKeyIdentifierAsync(
            ICertificateStore store,
            X500DistinguishedName issuer,
            string keyIdentifier)
        {
            X509Certificate2Collection certificates = await store.EnumerateAsync()
                .ConfigureAwait(false);
            foreach (X509Certificate2 certificate in certificates)
            {
                if (CompareDistinguishedName(certificate.SubjectName, issuer))
                {
                    X509SubjectKeyIdentifierExtension subject = certificate.FindExtension<X509SubjectKeyIdentifierExtension>();
                    if (subject != null && Utils.IsEqual(subject.SubjectKeyIdentifier, keyIdentifier))
                    {
                        return certificate;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Extension to add a certificate to a <see cref="ICertificateStore"/>.
        /// </summary>
        /// <remarks>
        /// Saves also the private key, if available.
        /// If written to a Pfx file, the password is used for protection.
        /// </remarks>
        /// <param name="certificate">The certificate to store.</param>
        /// <param name="storeType">Type of certificate store (Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="storePath">The store path (syntax depends on storeType).</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use AddToStoreAsync instead")]
        public static X509Certificate2 AddToStore(
            this X509Certificate2 certificate,
            string storeType,
            string storePath,
            string password = null)
        {
            return AddToStoreAsync(
                certificate,
                storeType,
                storePath,
                password?.ToCharArray())
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Extension to add a certificate to a <see cref="ICertificateStore"/>.
        /// </summary>
        /// <remarks>
        /// Saves also the private key, if available.
        /// If written to a Pfx file, the password is used for protection.
        /// </remarks>
        /// <param name="certificate">The certificate to store.</param>
        /// <param name="storeIdentifier">The certificate store.</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <exception cref="ArgumentException"></exception>
        [Obsolete("Use AddToStoreAsync instead")]
        public static X509Certificate2 AddToStore(
            this X509Certificate2 certificate,
            CertificateStoreIdentifier storeIdentifier,
            string password = null)
        {
            return AddToStoreAsync(
                certificate,
                storeIdentifier,
                password?.ToCharArray())
                .GetAwaiter().GetResult();
        }

        /// <summary>e
        /// Extension to add a certificate to a <see cref="ICertificateStore"/>.
        /// </summary>
        /// <remarks>
        /// Saves also the private key, if available.
        /// If written to a Pfx file, the password is used for protection.
        /// </remarks>
        /// <param name="certificate">The certificate to store.</param>
        /// <param name="storeType">Type of certificate store (Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="storePath">The store path (syntax depends on storeType).</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<X509Certificate2> AddToStoreAsync(
            this X509Certificate2 certificate,
            string storeType,
            string storePath,
            char[] password = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            // add cert to the store.
            if (!string.IsNullOrEmpty(storePath) && !string.IsNullOrEmpty(storeType))
            {
                var certificateStoreIdentifier = new CertificateStoreIdentifier(
                    storePath,
                    storeType,
                    false);
                using ICertificateStore store =
                    certificateStoreIdentifier.OpenStore(telemetry) ??
                    throw new ArgumentException("Invalid store type");

                await store.AddAsync(certificate, password, ct).ConfigureAwait(false);
                store.Close();
            }
            return certificate;
        }

        /// <summary>e
        /// Extension to add a certificate to a <see cref="ICertificateStore"/>.
        /// </summary>
        /// <remarks>
        /// Saves also the private key, if available.
        /// If written to a Pfx file, the password is used for protection.
        /// </remarks>
        /// <param name="certificate">The certificate to store.</param>
        /// <param name="storeIdentifier">Type of certificate store (Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <param name="telemetry">Telemetry context to use</param>
        /// <param name="ct">The cancellation token.</param>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<X509Certificate2> AddToStoreAsync(
            this X509Certificate2 certificate,
            CertificateStoreIdentifier storeIdentifier,
            char[] password = null,
            ITelemetryContext telemetry = null,
            CancellationToken ct = default)
        {
            // add cert to the store.
            if (storeIdentifier != null)
            {
                ICertificateStore store = storeIdentifier.OpenStore(telemetry);
                try
                {
                    if (store == null)
                    {
                        throw new ArgumentException("Invalid store type");
                    }
                    await store.AddAsync(certificate, password, ct).ConfigureAwait(false);
                }
                finally
                {
                    store?.Close();
                }
            }
            return certificate;
        }

        /// <summary>
        /// Get the hash algorithm from the hash size in bits.
        /// </summary>
        public static HashAlgorithmName GetRSAHashAlgorithmName(uint hashSizeInBits)
        {
            if (hashSizeInBits <= 160)
            {
                return HashAlgorithmName.SHA1;
            }
            else if (hashSizeInBits <= 256)
            {
                return HashAlgorithmName.SHA256;
            }
            else if (hashSizeInBits <= 384)
            {
                return HashAlgorithmName.SHA384;
            }
            else
            {
                return HashAlgorithmName.SHA512;
            }
        }

        /// <summary>
        /// Create secure temporary passcode.
        /// </summary>
        /// <remarks>
        /// Caller is responsible to clear memory after usage.
        /// </remarks>
        internal static char[] GeneratePasscode()
        {
            const int kLength = 18;
            byte[] tokenBuffer = Nonce.CreateRandomNonceData(kLength);
            char[] charToken = new char[kLength * 3];
            int length = Convert.ToBase64CharArray(
                tokenBuffer,
                0,
                tokenBuffer.Length,
                charToken,
                0,
                Base64FormattingOptions.None);
            Array.Clear(tokenBuffer, 0, tokenBuffer.Length);
            char[] passcode = new char[length];
            charToken.AsSpan(0, length).CopyTo(passcode);
            Array.Clear(charToken, 0, charToken.Length);
            return passcode;
        }
    }
}
