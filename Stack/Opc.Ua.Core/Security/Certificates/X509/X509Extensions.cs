/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/


using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Net;
using System.Numerics;
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

#if NETSTANDARD2_1
        /// <summary>
        /// Build the Subject Alternative name extension (for OPC UA application certs)
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="domainNames">The domain names. DNS Hostnames, IPv4 or IPv6 addresses</param>
        public static X509Extension BuildSubjectAlternativeName(string applicationUri, IList<string> domainNames)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddUri(new Uri(applicationUri));
            foreach (string domainName in domainNames)
            {
                IPAddress ipAddr;
                if (String.IsNullOrWhiteSpace(domainName))
                {
                    continue;
                }
                if (IPAddress.TryParse(domainName, out ipAddr))
                {
                    sanBuilder.AddIpAddress(ipAddr);
                }
                else
                {
                    sanBuilder.AddDnsName(domainName);
                }
            }

            return sanBuilder.Build();
        }
#endif

        /// <summary>
        /// Build the CRL Distribution Point extension.
        /// </summary>
        /// <param name="distributionPoint">The CRL distribution point</param>
        public static X509Extension BuildX509CRLDistributionPoints(
            string distributionPoint
            )
        {
            var context0 = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            Asn1Tag distributionPointChoice = context0;
            Asn1Tag fullNameChoice = context0;
            Asn1Tag generalNameUriChoice = new Asn1Tag(TagClass.ContextSpecific, 6);

            {
                AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
                writer.PushSequence();
                writer.PushSequence();
                writer.PushSequence(distributionPointChoice);
                writer.PushSequence(fullNameChoice);
                writer.WriteCharacterString(
                    UniversalTagNumber.IA5String,
                    distributionPoint,
                    generalNameUriChoice
                    );
                writer.PopSequence(fullNameChoice);
                writer.PopSequence(distributionPointChoice);
                writer.PopSequence();
                writer.PopSequence();
                return new X509Extension("2.5.29.31", writer.Encode(), false);
            }
        }

        /// <summary>
        /// Build the CRL Reason extension.
        /// </summary>
        public static X509Extension BuildX509CRLReason(
            CRLReason reason
            )
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.WriteObjectIdentifier(OidConstants.CertificateRevocationReasonCode);
            // TODO: is there a better way to encode CRLReason?
            writer.WriteOctetString(new byte[] { (byte)UniversalTagNumber.Enumerated, 0x1, (byte)reason });
            writer.PopSequence();
            return new X509Extension(OidConstants.CertificateRevocationReasonCode, writer.Encode(), false);
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
                issuerCaCertificate.GetSerialNumber(), Utils.FromHexString(ski.SubjectKeyIdentifier));
        }

        /// <summary>
        /// Build the CRL number.
        /// </summary>
        public static X509Extension BuildCRLNumber(BigInteger crlNumber)
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteInteger(crlNumber);
            return new X509Extension(OidConstants.CrlNumber, writer.Encode(), false);
        }
    }
}
