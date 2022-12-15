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
using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Stores the authority key identifier extension.
    /// </summary>
    /// <remarks>
    ///     id-ce-authorityKeyIdentifier OBJECT IDENTIFIER ::=  { id-ce 35 }
    ///     AuthorityKeyIdentifier ::= SEQUENCE {
    ///         keyIdentifier[0] KeyIdentifier           OPTIONAL,
    ///         authorityCertIssuer[1] GeneralNames            OPTIONAL,
    ///         authorityCertSerialNumber[2] CertificateSerialNumber OPTIONAL
    ///         }
    ///     KeyIdentifier::= OCTET STRING
    /// </remarks>
    public class X509AuthorityKeyIdentifierExtension : X509Extension
    {
        #region Constructors
        /// <summary>
        /// Creates an empty extension.
        /// </summary>
        protected X509AuthorityKeyIdentifierExtension()
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(AsnEncodedData encodedExtension, bool critical)
        :
            this(encodedExtension.Oid, encodedExtension.RawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(string oid, byte[] rawData, bool critical)
        :
            this(new Oid(oid, kFriendlyName), rawData, critical)
        {
        }

        /// <summary>
        /// Build the X509 Authority Key extension.
        /// </summary>
        /// <param name="subjectKeyIdentifier">The subject key identifier</param>
        public X509AuthorityKeyIdentifierExtension(
            byte[] subjectKeyIdentifier
            )
        {
            if (subjectKeyIdentifier == null) throw new ArgumentNullException(nameof(subjectKeyIdentifier));
            m_keyIdentifier = subjectKeyIdentifier;
            base.Oid = new Oid(AuthorityKeyIdentifier2Oid, kFriendlyName);
            base.Critical = false;
            base.RawData = Encode();
        }

        /// <summary>
        /// Build the X509 Authority Key extension.
        /// </summary>
        /// <remarks>
        /// A null value for one of the parameters indicates that the optional
        /// identifier can be ignored. Only keyId should be used for PKI use.
        /// </remarks>
        /// <param name="subjectKeyIdentifier">The subject key identifier as a byte array.</param>
        /// <param name="authorityName">The distinguished name of the issuer.</param>
        /// <param name="serialNumber">The serial number of the issuer certificate as little endian byte array.</param>
        public X509AuthorityKeyIdentifierExtension(
            byte[] subjectKeyIdentifier,
            X500DistinguishedName authorityName,
            byte[] serialNumber
            )
        {
            m_issuer = authorityName;
            m_keyIdentifier = subjectKeyIdentifier;
            m_serialNumber = serialNumber;
            base.Oid = new Oid(AuthorityKeyIdentifier2Oid, kFriendlyName);
            base.Critical = false;
            base.RawData = Encode();
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(Oid oid, byte[] rawData, bool critical)
        :
            base(oid, rawData, critical)
        {
            Decode(rawData);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a formatted version of the Authority Key Identifier as a string.
        /// </summary>
        public override string Format(bool multiLine)
        {
            StringBuilder buffer = new StringBuilder();

            if (m_keyIdentifier != null && m_keyIdentifier.Length > 0)
            {
                if (buffer.Length > 0)
                {
                    if (multiLine)
                    {
                        buffer.AppendLine();
                    }
                    else
                    {
                        buffer.Append(", ");
                    }
                }

                buffer.Append(kKeyIdentifier);
                buffer.Append('=');
                buffer.Append(m_keyIdentifier.ToHexString());
            }

            if (m_issuer != null)
            {
                if (multiLine)
                {
                    buffer.AppendLine();
                }
                else
                {
                    buffer.Append(", ");
                }

                buffer.Append(kIssuer);
                buffer.Append('=');
                buffer.Append(m_issuer.Format(true));
            }

            if (m_serialNumber != null && m_serialNumber.Length > 0)
            {
                if (buffer.Length > 0)
                {
                    if (!multiLine)
                    {
                        buffer.Append(", ");
                    }
                }

                buffer.Append(kSerialNumber);
                buffer.Append('=');
                buffer.Append(m_serialNumber.ToHexString(true));
            }
            return buffer.ToString();
        }

        /// <summary>
        /// Initializes the extension from ASN.1 encoded data.
        /// </summary>
        public override void CopyFrom(AsnEncodedData asnEncodedData)
        {
            if (asnEncodedData == null) throw new ArgumentNullException(nameof(asnEncodedData));
            base.Oid = asnEncodedData.Oid;
            base.RawData = asnEncodedData.RawData;
            Decode(asnEncodedData.RawData);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifierOid = "2.5.29.1";

        /// <summary>
        /// The alternate OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifier2Oid = "2.5.29.35";

        /// <summary>
        /// The identifier for the key as a little endian hexadecimal string.
        /// </summary>
        public string KeyIdentifier => m_keyIdentifier.ToHexString();

        /// <summary>
        /// The identifier for the key as a byte array.
        /// </summary>
        public byte[] GetKeyIdentifier() => m_keyIdentifier;

        /// <summary>
        /// A list of distinguished names for the issuer.
        /// </summary>
        public X500DistinguishedName Issuer => m_issuer;

        /// <summary>
        /// The serial number of the authority key as a big endian hexadecimal string.
        /// </summary>
        public string SerialNumber => m_serialNumber.ToHexString(true);

        /// <summary>
        /// The serial number of the authority key as a byte array in little endian order.
        /// </summary>
        public byte[] GetSerialNumber() => m_serialNumber;
        #endregion

        #region Private Methods
        private byte[] Encode()
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();

            if (m_keyIdentifier != null)
            {
                Asn1Tag keyIdTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                writer.WriteOctetString(m_keyIdentifier, keyIdTag);
            }

            if (m_issuer != null)
            {
                Asn1Tag issuerNameTag = new Asn1Tag(TagClass.ContextSpecific, 1);
                writer.PushSequence(issuerNameTag);

                // Add the issuer to constructed context-specific 4 (GeneralName.directoryName)
                // NOTE: rewrite using sequence
                // X.680 2015-08 31.2.7: "The tagging construction specifies explicit tagging if any of the following holds:
                // ... (c) ... the type defined by "Type" is an untagged choice type, ... "
                // Since this is a Context-Specific tag the output is the same
                Asn1Tag directoryNameTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
                writer.PushSetOf(directoryNameTag);
                writer.WriteEncodedValue(m_issuer.RawData);
                writer.PopSetOf(directoryNameTag);
                writer.PopSequence(issuerNameTag);
            }

            if (m_serialNumber != null)
            {
                Asn1Tag issuerSerialTag = new Asn1Tag(TagClass.ContextSpecific, 2);
                BigInteger issuerSerial = new BigInteger(m_serialNumber);
                writer.WriteInteger(issuerSerial, issuerSerialTag);
            }

            writer.PopSequence();
            return writer.Encode();
        }


        private void Decode(byte[] data)
        {
            if (base.Oid.Value == AuthorityKeyIdentifierOid ||
                base.Oid.Value == AuthorityKeyIdentifier2Oid)
            {
                try
                {
                    AsnReader dataReader = new AsnReader(data, AsnEncodingRules.DER);
                    var akiReader = dataReader.ReadSequence();
                    dataReader.ThrowIfNotEmpty();
                    if (akiReader != null)
                    {
                        Asn1Tag keyIdTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                        Asn1Tag dnameSequencyTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
                        Asn1Tag serialNumberTag = new Asn1Tag(TagClass.ContextSpecific, 2);
                        while (akiReader.HasData)
                        {
                            Asn1Tag peekTag = akiReader.PeekTag();
                            if (peekTag == keyIdTag)
                            {
                                m_keyIdentifier = akiReader.ReadOctetString(keyIdTag);
                                continue;
                            }

                            if (peekTag == dnameSequencyTag)
                            {
                                AsnReader issuerReader = akiReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1));
                                if (issuerReader != null)
                                {
                                    Asn1Tag directoryNameTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
                                    m_issuer = new X500DistinguishedName(issuerReader.ReadSequence(directoryNameTag).ReadEncodedValue().ToArray());
                                    issuerReader.ThrowIfNotEmpty();
                                }
                                continue;
                            }

                            if (peekTag == serialNumberTag)
                            {
                                m_serialNumber = akiReader.ReadInteger(serialNumberTag).ToByteArray();
                                continue;
                            }
                            throw new AsnContentException("Unknown tag in sequence.");
                        }
                        akiReader.ThrowIfNotEmpty();
                        return;
                    }
                    throw new CryptographicException("No valid data in the extension.");
                }
                catch (AsnContentException ace)
                {
                    throw new CryptographicException("Failed to decode the AuthorityKeyIdentifier extension.", ace);
                }
            }
            throw new CryptographicException("Invalid AuthorityKeyIdentifierOid.");
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Authority Key Identifier extension string
        /// definitions see RFC 5280 4.2.1.1
        /// </summary>
        private const string kKeyIdentifier = "KeyID";
        private const string kIssuer = "Issuer";
        private const string kSerialNumber = "SerialNumber";
        private const string kFriendlyName = "Authority Key Identifier";
        private byte[] m_keyIdentifier;
        private X500DistinguishedName m_issuer;
        private byte[] m_serialNumber;
        #endregion
    }
}
