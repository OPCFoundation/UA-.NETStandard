/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates.X509
{
    public static class X509Extensions
    {
        public static T FindExtension<T>(X509Certificate2 certificate) where T : X509Extension
        {
            // search known custom extensions
            if (typeof(T) == typeof(X509AuthorityKeyIdentifierExtension))
            {
                var extension = certificate.Extensions.Cast<X509Extension>().Where(e => (
                    e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifierOid ||
                    e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid)
                ).FirstOrDefault();
                if (extension != null)
                {
                    return new X509AuthorityKeyIdentifierExtension(extension, extension.Critical) as T;
                }
            }

            if (typeof(T) == typeof(X509SubjectAltNameExtension))
            {
                var extension = certificate.Extensions.Cast<X509Extension>().Where(e => (
                    e.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid ||
                    e.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                ).FirstOrDefault();
                if (extension != null)
                {
                    return new X509SubjectAltNameExtension(extension, extension.Critical) as T;
                }
            }

            // search builtin extension
            return certificate.Extensions.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// Determines whether the certificate is issued by a Certificate Authority.
        /// </summary>
        public static bool IsCertificateAuthority(X509Certificate2 certificate)
        {
            var constraints = FindExtension<X509BasicConstraintsExtension>(certificate);
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
            var allFlags = X509KeyUsageFlags.None;
            foreach (X509KeyUsageExtension ext in cert.Extensions.OfType<X509KeyUsageExtension>())
            {
                allFlags |= ext.KeyUsages;
            }
            return allFlags;
        }

        /// <summary>
        /// Build the Authority information Access extension.
        /// </summary>
        /// <param name="caIssuerUrls">Array of CA Issuer Urls</param>
        /// <param name="ocspResponder">optional, the OCSP responder </param>
        public static X509Extension BuildX509AuthorityInformationAccess(
            string[] caIssuerUrls,
            string ocspResponder = null
            )
        {
            if (String.IsNullOrEmpty(ocspResponder) &&
               (caIssuerUrls == null || caIssuerUrls.Length == 0))
            {
                throw new ArgumentNullException(nameof(caIssuerUrls), "One CA Issuer Url or OCSP responder is required for the extension.");
            }

            var context0 = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            Asn1Tag generalNameUriChoice = new Asn1Tag(TagClass.ContextSpecific, 6);
            {
                AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
                writer.PushSequence();
                if (caIssuerUrls != null)
                {
                    foreach (var caIssuerUrl in caIssuerUrls)
                    {
                        writer.PushSequence();
                        writer.WriteObjectIdentifier("1.3.6.1.5.5.7.48.2");
                        writer.WriteCharacterString(
                            UniversalTagNumber.IA5String,
                            caIssuerUrl,
                            generalNameUriChoice
                            );
                        writer.PopSequence();
                    }
                }
                if (!String.IsNullOrEmpty(ocspResponder))
                {
                    writer.PushSequence();
                    writer.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1");
                    writer.WriteCharacterString(
                        UniversalTagNumber.IA5String,
                        ocspResponder,
                        generalNameUriChoice
                        );
                    writer.PopSequence();
                }
                writer.PopSequence();
                return new X509Extension("1.3.6.1.5.5.7.1.1", writer.Encode(), false);
            }
        }

        /// <summary>
        /// Build the Authority Key Identifier from an Issuer CA certificate.
        /// </summary>
        /// <param name="issuerCaCertificate">The issuer CA certificate</param>
        public static X509Extension BuildAuthorityKeyIdentifier(X509Certificate2 issuerCaCertificate)
        {
            // force exception if SKI is not present
            var ski = issuerCaCertificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();
            return new X509AuthorityKeyIdentifierExtension(issuerCaCertificate.SubjectName,
                issuerCaCertificate.GetSerialNumber().Reverse().ToArray(), Utils.FromHexString(ski.SubjectKeyIdentifier));
        }

        /// <summary>
        /// Build the CRL number.
        /// </summary>
        public static X509Extension BuildCRLNumber(System.Numerics.BigInteger crlNumber)
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteInteger(crlNumber);
            return new X509Extension(OidConstants.CrlNumber, writer.Encode(), false);
        }

        /// <summary>
        /// Extracts the DNS names specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The DNS names.</returns>
        public static IList<string> GetDomainsFromCertficate(X509Certificate2 certificate)
        {
            List<string> dnsNames = new List<string>();

            // extracts the domain from the subject name.
            List<string> fields = X509Utils.ParseDistinguishedName(certificate.Subject);

            StringBuilder builder = new StringBuilder();

            for (int ii = 0; ii < fields.Count; ii++)
            {
                if (fields[ii].StartsWith("DC="))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append('.');
                    }

                    builder.Append(fields[ii].Substring(3));
                }
            }

            if (builder.Length > 0)
            {
                dnsNames.Add(builder.ToString().ToUpperInvariant());
            }

            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = FindExtension<X509SubjectAltNameExtension>(certificate);
            if (alternateName != null)
            {
                for (int ii = 0; ii < alternateName.DomainNames.Count; ii++)
                {
                    string hostname = alternateName.DomainNames[ii];

                    // do not add duplicates to the list.
                    bool found = false;

                    for (int jj = 0; jj < dnsNames.Count; jj++)
                    {
                        if (String.Compare(dnsNames[jj], hostname, StringComparison.OrdinalIgnoreCase) == 0)
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
        /// Extracts the application URI specified in the certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>The application URI.</returns>
        public static string GetApplicationUriFromCertificate(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = FindExtension<X509SubjectAltNameExtension>(certificate);

            // get the application uri.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                return alternateName.Uris[0];
            }

            return string.Empty;
        }

        /// <summary>
        /// Check if certificate has an application urn.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>true if the application URI starts with urn: </returns>
        public static bool HasApplicationURN(X509Certificate2 certificate)
        {
            // extract the alternate domains from the subject alternate name extension.
            X509SubjectAltNameExtension alternateName = FindExtension<X509SubjectAltNameExtension>(certificate);

            // find the application urn.
            if (alternateName != null && alternateName.Uris.Count > 0)
            {
                string urn = "urn:";
                for (int i = 0; i < alternateName.Uris.Count; i++)
                {
                    if (string.Compare(alternateName.Uris[i], 0, urn, 0, urn.Length, StringComparison.OrdinalIgnoreCase) == 0)
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

            IList<string> domainNames = GetDomainsFromCertficate(certificate);

            for (int jj = 0; jj < domainNames.Count; jj++)
            {
                if (String.Compare(domainNames[jj], endpointUrl.DnsSafeHost, StringComparison.OrdinalIgnoreCase) == 0)
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
            X509BasicConstraintsExtension constraints = FindExtension<X509BasicConstraintsExtension>(certificate);

            if (constraints != null)
            {
                return constraints.CertificateAuthority;
            }

            return false;
        }

    }
}
