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
        public static X509SubjectAltNameExtension GetSubjectAltNameExtension(byte[] attributes)
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
