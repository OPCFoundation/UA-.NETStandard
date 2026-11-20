// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Utility class for parsing PKCS#10 CSR attributes and extensions.
    /// </summary>
    public static class Pkcs10Utils
    {
        /// <summary>
        /// OID for PKCS#9 Extension Request attribute
        /// </summary>
        private const string kPkcs9AtExtensionRequest = "1.2.840.113549.1.9.14";

        /// <summary>
        /// OID for Subject Alternative Name extension
        /// </summary>
        private const string kSubjectAlternativeNameOid = "2.5.29.17";

        /// <summary>
        /// Extracts the Subject Alternative Name extension from CSR attributes.
        /// </summary>
        /// <param name="attributes">The CSR attributes encoded as DER bytes.</param>
        /// <returns>The X509SubjectAltNameExtension if found; otherwise, null.</returns>
        /// <exception cref="CryptographicException"></exception>
        public static X509SubjectAltNameExtension? GetSubjectAltNameExtension(byte[]? attributes)
        {
            if (attributes == null || attributes.Length == 0)
            {
                return null;
            }

            try
            {
                // Attributes are encoded as [0] IMPLICIT Attributes
                // which is a SET OF Attribute
                var reader = new AsnReader(attributes, AsnEncodingRules.DER);

                // Read the context-specific tag [0]
                AsnReader attributesReader = reader.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));

                while (attributesReader.HasData)
                {
                    // Each attribute is a SEQUENCE
                    AsnReader attributeReader = attributesReader.ReadSequence();

                    // Read the attribute type (OID)
                    string attributeOid = attributeReader.ReadObjectIdentifier();

                    // Read the attribute values (SET)
                    AsnReader valuesReader = attributeReader.ReadSetOf();

                    // Check if this is an Extension Request attribute
                    if (attributeOid == kPkcs9AtExtensionRequest)
                    {
                        // The extension request contains a SEQUENCE of extensions
                        AsnReader extensionsSequenceReader = valuesReader.ReadSequence();

                        while (extensionsSequenceReader.HasData)
                        {
                            // Each extension is a SEQUENCE
                            AsnReader extensionReader = extensionsSequenceReader.ReadSequence();

                            // Read extension OID
                            string extensionOid = extensionReader.ReadObjectIdentifier();

                            // Check for critical flag (optional BOOLEAN, default FALSE)
                            bool critical = false;
                            if (extensionReader.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
                            {
                                critical = extensionReader.ReadBoolean();
                            }

                            // Read extension value (OCTET STRING)
                            byte[] extensionValue = extensionReader.ReadOctetString();

                            // Check if this is the Subject Alternative Name extension
                            if (extensionOid == kSubjectAlternativeNameOid)
                            {
                                var asnEncodedData = new AsnEncodedData(
                                    kSubjectAlternativeNameOid,
                                    extensionValue);
                                return new X509SubjectAltNameExtension(asnEncodedData, critical);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException(
                    "Failed to parse CSR attributes for Subject Alternative Name extension.",
                    ex);
            }

            return null;
        }
    }
}
