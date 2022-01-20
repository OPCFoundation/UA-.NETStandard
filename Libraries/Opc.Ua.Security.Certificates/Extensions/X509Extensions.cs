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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Supporting functions for X509 extensions.
    /// </summary>
    public static class X509Extensions
    {
        /// <summary>
        /// Find a typed extension in a certificate.
        /// </summary>
        /// <typeparam name="T">The type of the extension.</typeparam>
        /// <param name="certificate">The certificate with extensions.</param>
        public static T FindExtension<T>(this X509Certificate2 certificate) where T : X509Extension
        {
            return FindExtension<T>(certificate.Extensions);
        }

        /// <summary>
        /// Find a typed extension in a extension collection.
        /// </summary>
        /// <typeparam name="T">The type of the extension.</typeparam>
        /// <param name="extensions">The extensions to search.</param>
        public static T FindExtension<T>(this X509ExtensionCollection extensions) where T : X509Extension
        {
            if (extensions == null) throw new ArgumentNullException(nameof(extensions));
            lock (extensions.SyncRoot)
            {
                // search known custom extensions
                if (typeof(T) == typeof(X509AuthorityKeyIdentifierExtension))
                {
                    var extension = extensions.Cast<X509Extension>().FirstOrDefault(e => (
                        e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifierOid ||
                        e.Oid.Value == X509AuthorityKeyIdentifierExtension.AuthorityKeyIdentifier2Oid)
                    );
                    if (extension != null)
                    {
                        return new X509AuthorityKeyIdentifierExtension(extension, extension.Critical) as T;
                    }
                }

                if (typeof(T) == typeof(X509SubjectAltNameExtension))
                {
                    var extension = extensions.Cast<X509Extension>().FirstOrDefault(e => (
                        e.Oid.Value == X509SubjectAltNameExtension.SubjectAltNameOid ||
                        e.Oid.Value == X509SubjectAltNameExtension.SubjectAltName2Oid)
                    );
                    if (extension != null)
                    {
                        return new X509SubjectAltNameExtension(extension, extension.Critical) as T;
                    }
                }

                if (typeof(T) == typeof(X509CrlNumberExtension))
                {
                    var extension = extensions.Cast<X509Extension>().FirstOrDefault(e => (
                        e.Oid.Value == X509CrlNumberExtension.CrlNumberOid)
                    );
                    if (extension != null)
                    {
                        return new X509CrlNumberExtension(extension, extension.Critical) as T;
                    }
                }

                // search builtin extension
                return extensions.OfType<T>().FirstOrDefault();
            }
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

            Asn1Tag generalNameUriChoice = new Asn1Tag(TagClass.ContextSpecific, 6);
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            if (caIssuerUrls != null)
            {
                foreach (var caIssuerUrl in caIssuerUrls)
                {
                    writer.PushSequence();
                    writer.WriteObjectIdentifier(Oids.CertificateAuthorityIssuers);
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
                writer.WriteObjectIdentifier(Oids.OnlineCertificateStatusProtocol);
                writer.WriteCharacterString(
                    UniversalTagNumber.IA5String,
                    ocspResponder,
                    generalNameUriChoice
                    );
                writer.PopSequence();
            }
            writer.PopSequence();
            return new X509Extension(Oids.AuthorityInfoAccess, writer.Encode(), false);
        }

        /// <summary>
        /// Build the CRL Distribution Point extension.
        /// </summary>
        /// <param name="distributionPoint">The CRL distribution point</param>
        public static X509Extension BuildX509CRLDistributionPoints(
            string distributionPoint)
        {
            return BuildX509CRLDistributionPoints(new string[] { distributionPoint });
        }

        /// <summary>
        /// Build the CRL Distribution Point extension with multiple distribution points.
        /// </summary>
        /// <param name="distributionPoints">The CRL distribution points</param>
        public static X509Extension BuildX509CRLDistributionPoints(
            IEnumerable<string> distributionPoints)
        {
            var context0 = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            Asn1Tag distributionPointChoice = context0;
            Asn1Tag fullNameChoice = context0;
            Asn1Tag generalNameUriChoice = new Asn1Tag(TagClass.ContextSpecific, 6);
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.PushSequence();
            writer.PushSequence(distributionPointChoice);
            writer.PushSequence(fullNameChoice);
            foreach (string distributionPoint in distributionPoints)
            {
                writer.WriteCharacterString(
                    UniversalTagNumber.IA5String,
                    distributionPoint,
                    generalNameUriChoice
                    );
            }
            writer.PopSequence(fullNameChoice);
            writer.PopSequence(distributionPointChoice);
            writer.PopSequence();
            writer.PopSequence();
            return new X509Extension(Oids.CRLDistributionPoint, writer.Encode(), false);
        }

        /// <summary>
        /// Read an ASN.1 extension sequence as X509Extension object.
        /// </summary>
        /// <param name="reader">The ASN reader.</param>
        public static X509Extension ReadExtension(this AsnReader reader)
        {
            if (reader.HasData)
            {
                var boolTag = new Asn1Tag(UniversalTagNumber.Boolean);
                var extReader = reader.ReadSequence();
                var extOid = extReader.ReadObjectIdentifier();
                bool critical = false;
                var peekTag = extReader.PeekTag();
                if (peekTag == boolTag)
                {
                    critical = extReader.ReadBoolean();
                }
                var data = extReader.ReadOctetString();
                extReader.ThrowIfNotEmpty();
                return new X509Extension(new Oid(extOid), data, critical);
            }
            return null;
        }

        /// <summary>
        /// Write an extension object as ASN.1.
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="extension"></param>
        public static void WriteExtension(this AsnWriter writer, X509Extension extension)
        {
            var etag = Asn1Tag.Sequence;
            writer.PushSequence(etag);
            writer.WriteObjectIdentifier(extension.Oid.Value);
            if (extension.Critical)
            {
                writer.WriteBoolean(extension.Critical);
            }
            writer.WriteOctetString(extension.RawData);
            writer.PopSequence(etag);
        }

        /// <summary>
        /// Build the CRL Reason extension.
        /// </summary>
        public static X509Extension BuildX509CRLReason(
            CRLReason reason
            )
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteEnumeratedValue<CRLReason>(reason);
            return new X509Extension(Oids.CrlReasonCode, writer.Encode(), false);
        }

        /// <summary>
        /// Build the Authority Key Identifier from an Issuer CA certificate.
        /// </summary>
        /// <param name="issuerCaCertificate">The issuer CA certificate</param>
        public static X509Extension BuildAuthorityKeyIdentifier(X509Certificate2 issuerCaCertificate)
        {
            // force exception if SKI is not present
            var ski = issuerCaCertificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();
            return new X509AuthorityKeyIdentifierExtension(
                ski.SubjectKeyIdentifier.FromHexString(),
                issuerCaCertificate.IssuerName,
                issuerCaCertificate.GetSerialNumber());
        }

        /// <summary>
        /// Build the CRL number.
        /// </summary>
        public static X509Extension BuildCRLNumber(BigInteger crlNumber)
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteInteger(crlNumber);
            return new X509Extension(Oids.CrlNumber, writer.Encode(), false);
        }

        /// <summary>
        /// Patch serial number in a Url. byte version.
        /// </summary>
        public static string PatchExtensionUrl(string extensionUrl, byte[] serialNumber)
        {
            return PatchExtensionUrl(extensionUrl, serialNumber.ToHexString());
        }

        /// <summary>
        /// Patch serial number in a Url. string version.
        /// </summary>
        public static string PatchExtensionUrl(string extensionUrl, string serial)
        {
            return extensionUrl.Replace("%serial%", serial.ToLower());
        }
    }
}
